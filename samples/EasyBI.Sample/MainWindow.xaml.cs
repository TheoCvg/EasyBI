using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Renderers.LiveCharts2;
using EasyBI.Renderers.OxyPlot;
using EasyBI.Wpf;
using EasyBI.Wpf.Dashboard;
using Microsoft.Win32;

namespace EasyBI.Sample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        // --- All the WPF developer has to write: a model + renderers ---
        Context = EasyBIContext.Create()
            .AddModel(SalesData.CreateModel(SalesData.Generate(), App.TextResources))
            .UseRenderer(new LiveCharts2Renderer())
            .UseRenderer(new OxyPlotRenderer());

        InitializeComponent();
        DataContext = this;
        RendererPicker.ItemsSource = Context.Renderers;
        RendererPicker.SelectedItem = Context.ActiveRenderer;
        LanguagePicker.ItemsSource = App.Languages;
        LanguagePicker.SelectedItem = App.Languages.FirstOrDefault(l => l.Equals(EasyBILocalization.Culture));
        SetPublishLabel("AddToDashboard");
        Dashboard.Load(DefaultDashboard());
    }

    private static string Text(string key) => App.Texts[key];

    private void SetPublishLabel(string key) =>
        PublishButton.SetBinding(ContentProperty, new Binding($"[{key}]") { Source = App.Texts });

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguagePicker.SelectedItem is CultureInfo culture) App.SetLanguage(culture);
    }

    public EasyBIContext Context { get; }

    /// <summary>Global filters and cross-filtering of the dashboard.</summary>
    public FilterContext Filters { get; } = new();

    private void OnRendererChanged(object sender, SelectionChangedEventArgs e) =>
        Context.ActiveRenderer = (IVisualRenderer?)RendererPicker.SelectedItem;

    private void OnNewVisual(object sender, RoutedEventArgs e)
    {
        Designer.Clear();
        SetPublishLabel("AddToDashboard");
    }

    private void OnPublishVisual(object sender, RoutedEventArgs e)
    {
        if (Designer.Visual is not { } visual) return;
        if (VisualValidator.Validate(visual) is { } problem)
        {
            MessageBox.Show(this, problem, Text("IncompleteVisual"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // One change (one undo step): fill an empty item, or add one at the end
        using (var edit = Dashboard.BeginEdit())
        {
            var item = Dashboard.Items.FirstOrDefault(i => i.Kind == DashboardItemKind.Empty) ?? Dashboard.AddItem(Dashboard.Panel.BandCount - 1);
            Dashboard.SetVisual(item, visual);
            edit.Commit();
        }
        Designer.Clear();
        SetPublishLabel("AddToDashboard");
        Tabs.SelectedIndex = 0;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = Text("ReportFileFilter"), FileName = Text("DefaultFileName") };
        if (dialog.ShowDialog(this) != true) return;
        // The report (visuals, filters) stays in the easybi/v1 format; the dashboard adds its layout next to it
        var document = Dashboard.Capture();
        document = document with { Report = document.Report with { Title = Text("ReportTitle"), Filters = Filters.Filters.ToArray() } };
        File.WriteAllText(dialog.FileName, DashboardSerializer.Serialize(document));
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Text("OpenFileFilter") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            // A plain report (easybi/v1) opens too: its visuals get a default layout
            var document = DashboardSerializer.Deserialize(File.ReadAllText(dialog.FileName));
            if (Context.Validate(document.Report) is { Count: > 0 } problems)
            {
                MessageBox.Show(this, Text("ReportIncompleteMessage") + "\n\n• " + string.Join("\n• ", problems),
                    Text("ReportIncomplete"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Filters.Load(document.Report.Filters);
            Dashboard.Load(document);
            Tabs.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text("ReportUnreadable"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Titles and texts are user content: they are written once, in the language active at startup
    private static DashboardDocument DefaultDashboard()
    {
        VisualDefinition title = new()
        {
            Mark = MarkType.Text,
            Text = new TextContent { Text = Text("DashboardTitle"), FontSize = 24, IsBold = true },
        };
        VisualDefinition kpis = new()
        {
            Title = Text("KeyIndicators"),
            Model = "sales",
            Mark = MarkType.Kpi,
            Values = [new("Amount"), new("Margin"), new("Count")],
        };
        VisualDefinition quarters = new()
        {
            Title = Text("RevenueByQuarter"),
            Model = "sales",
            Mark = MarkType.Line,
            Axis = [new("Date", DateGrain.Quarter)],
            Values = [new("Amount")],
        };
        VisualDefinition regions = new()
        {
            Title = Text("RevenueByRegionAndChannel"),
            Model = "sales",
            Mark = MarkType.Bar,
            Axis = [new("Region")],
            Legend = new("Channel"),
            Values = [new("Amount")],
            Sort = new("Region"),
        };
        VisualDefinition categories = new()
        {
            Title = Text("RevenueByCategory"),
            Model = "sales",
            Mark = MarkType.Pie,
            Axis = [new("Product.Category")],
            Values = [new("Amount")],
        };
        VisualDefinition note = new()
        {
            Mark = MarkType.Text,
            Text = new TextContent { Text = Text("DashboardNote"), FontSize = 14 },
        };

        return new DashboardDocument
        {
            Report = new ReportDefinition { Title = Text("ReportTitle"), Visuals = [title, kpis, quarters, regions, categories, note] },
            Layout = new DashboardLayoutDefinition
            {
                Bands =
                [
                    new BandLayout
                    {
                        Items =
                        [
                            new ItemLayout { VisualId = title.Id, RowSpan = 1, IsFullRow = true },
                            new ItemLayout { VisualId = kpis.Id, Row = 1, ColumnSpan = 12, RowSpan = 2 },
                            new ItemLayout { VisualId = quarters.Id, Row = 3 },
                            new ItemLayout { VisualId = regions.Id, Row = 3, Column = 6 },
                        ],
                    },
                    new BandLayout
                    {
                        Mode = BandMode.Scroll,
                        Items =
                        [
                            new ItemLayout { VisualId = categories.Id, Width = 520 },
                            new ItemLayout { VisualId = note.Id, Width = 360 },
                        ],
                    },
                ],
            },
        };
    }
}
