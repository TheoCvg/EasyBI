using System.Collections.ObjectModel;
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
    private readonly ObservableCollection<VisualDefinition> _visuals = [];

    public MainWindow()
    {
        // --- All the WPF developer has to write: a model + renderers ---
        Context = EasyBIContext.Create()
            .AddModel(SalesData.CreateModel(SalesData.Generate(), App.TextResources))
            .UseRenderer(new LiveCharts2Renderer())
            .UseRenderer(new OxyPlotRenderer());

        InitializeComponent();
        DataContext = this;
        Dashboard.ItemsSource = _visuals;
        RendererPicker.ItemsSource = Context.Renderers;
        RendererPicker.SelectedItem = Context.ActiveRenderer;
        LanguagePicker.ItemsSource = App.Languages;
        LanguagePicker.SelectedItem = App.Languages.FirstOrDefault(l => l.Equals(EasyBILocalization.Culture));
        SetPublishLabel("AddToDashboard");
        foreach (var visual in DefaultVisuals()) _visuals.Add(visual);
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
        var index = _visuals.ToList().FindIndex(v => v.Id == visual.Id);
        if (index >= 0) _visuals[index] = visual;
        else _visuals.Add(visual);
        Designer.Clear();
        SetPublishLabel("AddToDashboard");
        Tabs.SelectedIndex = 0;
    }

    private void OnEditVisual(object sender, RoutedEventArgs e)
    {
        Designer.Edit((VisualDefinition)((FrameworkElement)sender).DataContext);
        SetPublishLabel("UpdateDashboard");
        Tabs.SelectedIndex = 1;
    }

    private void OnRemoveVisual(object sender, RoutedEventArgs e)
    {
        var visual = (VisualDefinition)((FrameworkElement)sender).DataContext;
        _visuals.Remove(visual);
        if (Filters.Selection?.SourceVisualId == visual.Id) Filters.ClearSelection();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = Text("ReportFileFilter"), FileName = Text("DefaultFileName") };
        if (dialog.ShowDialog(this) != true) return;
        var report = new ReportDefinition { Title = Text("ReportTitle"), Visuals = _visuals.ToArray(), Filters = Filters.Filters.ToArray() };
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
            _visuals.Clear();
            foreach (var visual in report.Visuals) _visuals.Add(visual);
            Filters.Load(report.Filters);
            Tabs.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text("ReportUnreadable"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Visual titles are user content: they are written once, in the language active at startup
    private static IEnumerable<VisualDefinition> DefaultVisuals() =>
    [
        new()
        {
            Title = Text("KeyIndicators"),
            Model = "sales",
            Mark = MarkType.Kpi,
            Values = [new("Amount"), new("Margin"), new("Count")],
        },
        new()
        {
            Title = Text("RevenueByQuarter"),
            Model = "sales",
            Mark = MarkType.Line,
            Axis = [new("Date", DateGrain.Quarter)],
            Values = [new("Amount")],
        },
        new()
        {
            Title = Text("RevenueByRegionAndChannel"),
            Model = "sales",
            Mark = MarkType.Bar,
            Axis = [new("Region")],
            Legend = new("Channel"),
            Values = [new("Amount")],
            Sort = new("Region"),
        },
        new()
        {
            Title = Text("RevenueByCategory"),
            Model = "sales",
            Mark = MarkType.Pie,
            Axis = [new("Product.Category")],
            Values = [new("Amount")],
        },
    ];
}
