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
        PublishButton.SetBinding(ContentProperty, new Binding("[AddToDashboard]") { Source = App.Texts });
        Dashboard.Report = DefaultReport();
    }

    public EasyBIContext Context { get; }

    /// <summary>Global filters and cross-filtering of the dashboard.</summary>
    public FilterContext Filters { get; } = new();

    private static string Text(string key) => App.Texts[key];

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguagePicker.SelectedItem is CultureInfo culture) App.SetLanguage(culture);
    }

    private void OnRendererChanged(object sender, SelectionChangedEventArgs e) =>
        Context.ActiveRenderer = (IVisualRenderer?)RendererPicker.SelectedItem;

    private void OnNewVisual(object sender, RoutedEventArgs e) => Designer.Clear();

    /// <summary>The designer tab adds visuals at the end of the dashboard; editing happens on the dashboard itself.</summary>
    private void OnPublishVisual(object sender, RoutedEventArgs e)
    {
        if (Designer.Visual is not { } visual) return;
        if (VisualValidator.Validate(visual) is { } problem)
        {
            MessageBox.Show(this, problem, Text("IncompleteVisual"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var report = Dashboard.Report ?? new ReportDefinition();
        Dashboard.Report = ReportLayout.Normalize(report with { Visuals = [.. report.Visuals.Where(v => v.Id != visual.Id), visual] });
        Designer.Clear();
        Tabs.SelectedIndex = 0;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = Text("ReportFileFilter"), FileName = Text("DefaultFileName") };
        if (dialog.ShowDialog(this) != true) return;
        // The dashboard's report holds the visuals and their layout; the filters live in the FilterContext
        var report = (Dashboard.Report ?? new ReportDefinition()) with { Title = Text("ReportTitle"), Filters = Filters.Filters.ToArray() };
        File.WriteAllText(dialog.FileName, ReportSerializer.Serialize(report));
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Text("OpenFileFilter") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var report = ReportSerializer.Deserialize(File.ReadAllText(dialog.FileName));
            if (Context.Validate(report) is { Count: > 0 } problems)
            {
                MessageBox.Show(this, Text("ReportIncompleteMessage") + "\n\n• " + string.Join("\n• ", problems),
                    Text("ReportIncomplete"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Dashboard.Report = report;
            Filters.Load(report.Filters);
            Tabs.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text("ReportUnreadable"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Visual titles are user content: they are written once, in the language active at startup
    private static ReportDefinition DefaultReport()
    {
        VisualDefinition kpis = new()
        {
            Title = Text("KeyIndicators"),
            Model = "sales",
            Mark = MarkType.Kpi,
            Values = [new("Amount"), new("Margin"), new("Count")],
        };
        VisualDefinition byQuarter = new()
        {
            Title = Text("RevenueByQuarter"),
            Model = "sales",
            Mark = MarkType.Line,
            Axis = [new("Date", DateGrain.Quarter)],
            Values = [new("Amount")],
        };
        VisualDefinition byRegion = new()
        {
            Title = Text("RevenueByRegionAndChannel"),
            Model = "sales",
            Mark = MarkType.Bar,
            Axis = [new("Region")],
            Legend = new("Channel"),
            Values = [new("Amount")],
            Sort = new("Region"),
        };
        VisualDefinition byCategory = new()
        {
            Title = Text("RevenueByCategory"),
            Model = "sales",
            Mark = MarkType.Pie,
            Axis = [new("Product.Category")],
            Values = [new("Amount")],
        };
        VisualDefinition byProduct = new()
        {
            Title = Text("RevenueByProduct"),
            Model = "sales",
            Mark = MarkType.Table,
            Axis = [new("Product.Name")],
            Values = [new("Amount"), new("Quantity")],
            Sort = new("Amount", Descending: true),
        };

        static LayoutItem Show(VisualDefinition visual, int column, int row, int columnSpan, int rowSpan) =>
            new() { VisualId = visual.Id, Column = column, Row = row, ColumnSpan = columnSpan, RowSpan = rowSpan };
        return new ReportDefinition
        {
            Visuals = [kpis, byQuarter, byRegion, byCategory, byProduct],
            Layout = new DashboardLayout
            {
                Bands =
                [
                    // A grid band: heading and KPIs take the full row, the quarterly trend covers four rows
                    new LayoutBand
                    {
                        Items =
                        [
                            new LayoutItem
                            {
                                IsFullRow = true,
                                RowSpan = 1,
                                Text = new TextContent { Text = Text("DashboardHeading"), FontSize = 22, IsBold = true },
                            },
                            Show(kpis, 0, 1, 12, 2) with { IsFullRow = true },
                            Show(byQuarter, 0, 3, 8, 4),
                            Show(byCategory, 8, 3, 4, 4),
                        ],
                    },
                    // A scrolling band: details side by side at a fixed width
                    new LayoutBand
                    {
                        Mode = BandMode.Scroll,
                        Items =
                        [
                            new LayoutItem { VisualId = byRegion.Id, Width = 760, RowSpan = 3 },
                            new LayoutItem { VisualId = byProduct.Id, Width = 520, RowSpan = 3 },
                        ],
                    },
                ],
            },
        };
    }
}
