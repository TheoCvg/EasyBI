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
using EasyBI.Wpf.Designer;
using Microsoft.Win32;

namespace EasyBI.Sample;

public partial class MainWindow : Window
{
    private readonly ISemanticModel _memorySales;
    private readonly SwitchableModel _sales;

    public MainWindow()
    {
        // The demo switches between data sources and times the queries; an application would add its model directly
        _memorySales = SalesData.CreateModel(SalesData.Generate(), App.TextResources);
        _sales = new SwitchableModel(_memorySales);
        _sales.RunningChanged += (_, running) => Dispatcher.BeginInvoke(() =>
        {
            if (running > 0) QueryStatus.Text = string.Format(EasyBILocalization.FormatCulture, Text("QueriesRunning"), running);
        });
        _sales.BatchCompleted += (_, batch) => Dispatcher.BeginInvoke(() =>
            QueryStatus.Text = string.Format(EasyBILocalization.FormatCulture, Text("LastRefresh"),
                batch.Queries, batch.Duration.TotalMilliseconds, batch.Slowest.TotalMilliseconds));

        // --- All the WPF developer has to write: a model + renderers ---
        Context = EasyBIContext.Create()
            .AddModel(_sales)
            .UseRenderer(new LiveCharts2Renderer())
            .UseRenderer(new OxyPlotRenderer())
            // A visual type of the application, with the renderer drawing it
            .UseRenderer(new RankingRenderer());
        // The designer's state: the application keeps it, the ReportDesigner shows it
        Designer = new DesignerViewModel(Context);

        InitializeComponent();
        DataContext = this;
        // The charting libraries to choose from: not the renderer of the ranking alone
        RendererPicker.ItemsSource = Context.Renderers.Where(r => r.Capabilities.Types.Contains(VisualType.Bar)).ToArray();
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

    public DesignerViewModel Designer { get; }

    /// <summary>Global filters and cross-filtering of the dashboard.</summary>
    public FilterContext Filters { get; } = new();

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && LibraryPicker.SelectedItem is ComboBoxItem { Tag: string library } && ThemePicker.SelectedItem is ComboBoxItem { Tag: string mode })
            App.SetTheme(Enum.Parse<ThemeLibrary>(library), Enum.Parse<EasyBIThemeMode>(mode));
    }

    private async void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SourcePicker.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        var rows = int.Parse(tag, CultureInfo.InvariantCulture);
        SourcePicker.IsEnabled = false;
        try
        {
            if (rows > 0 && !SalesDatabase.Exists(rows))
            {
                QueryStatus.Text = string.Format(EasyBILocalization.FormatCulture, Text("GeneratingDatabase"), rows);
                // SQLite runs synchronously even through the async API: keep the UI responsive
                await Task.Run(() => SalesDatabase.EnsureCreatedAsync(rows));
            }
            _sales.Source = rows == 0 ? _memorySales : SalesDatabase.CreateModel(rows, App.TextResources);
            // Same queries, other data: the dashboard and the designer's preview query again
            Context.RefreshData(_sales.Metadata.Name);
        }
        catch (Exception ex)
        {
            QueryStatus.Text = "";
            MessageBox.Show(this, ex.Message, Text("DataSourceUnavailable"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SourcePicker.IsEnabled = true;
        }
    }

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
        if (VisualValidator.Validate(visual, Context.VisualTypes) is { } problem)
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
            Type = VisualType.Text,
            Text = new TextContent { Text = Text("DashboardTitle"), FontSize = 24, IsBold = true },
        };
        VisualDefinition kpis = new()
        {
            Title = Text("KeyIndicators"),
            Model = "sales",
            Type = VisualType.Kpi,
            Values = [new("Amount"), new("Margin"), new("MarginRate"), new("Count")],
        };
        VisualDefinition quarters = new()
        {
            Title = Text("RevenueByQuarter"),
            Model = "sales",
            Type = VisualType.Bar,
            Axis = [new("Date", DateGrain.Quarter)],
            // A combined chart: revenue in bars, margin as a line, number of sales as points on the right axis
            Values = [new("Amount"), new("Margin") { SeriesType = VisualType.Line }, new("Count") { SeriesType = VisualType.Scatter, SecondaryAxis = true }],
        };
        VisualDefinition regions = new()
        {
            Title = Text("RevenueByRegionAndChannel"),
            Model = "sales",
            Type = VisualType.Bar,
            Axis = [new("Region")],
            Legend = new("Channel"),
            Values = [new("Amount")],
            Sort = new("Region"),
        };
        VisualDefinition categories = new()
        {
            Title = Text("RevenueByCategory"),
            Model = "sales",
            Type = VisualType.Pie,
            Axis = [new("Product.Category")],
            Values = [new("Amount")],
        };
        // A matrix: categories by year, with the total of each row and each column
        VisualDefinition matrix = new()
        {
            Title = Text("RevenueByCategoryAndYear"),
            Model = "sales",
            Type = VisualType.Matrix,
            Axis = [new("Product.Category")],
            Legend = new("Date", DateGrain.Year),
            Values = [new("Amount")],
        };
        // A visual type of the application (RankingRenderer)
        VisualDefinition topProducts = new()
        {
            Title = Text("TopProducts"),
            Model = "sales",
            Type = RankingRenderer.Ranking,
            Axis = [new("Product.Name")],
            Values = [new("Amount")],
            Top = 8,
            Sort = new("Amount", Descending: true),
        };
        VisualDefinition note = new()
        {
            Type = VisualType.Text,
            Text = new TextContent { Text = Text("DashboardNote"), FontSize = 14 },
        };
        // The demo's sales end in 2025: the last full year, and how it changed since the year before
        VisualDefinition lastYear = new()
        {
            Title = Text("LastYearComparison"),
            Model = "sales",
            Type = VisualType.Kpi,
            Values = [new("Amount"), new("Amount") { Time = TimeCalculation.PercentChangeFromPreviousYear }],
            Filters = [PeriodFilters.Between("Date", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))],
        };

        return new DashboardDocument
        {
            Report = new ReportDefinition { Title = Text("ReportTitle"), Visuals = [title, kpis, quarters, regions, categories, topProducts, lastYear, matrix, note] },
            Layout = new DashboardLayoutDefinition
            {
                Bands =
                [
                    new BandLayout
                    {
                        Items =
                        [
                            new LayoutNode { VisualId = title.Id, RowSpan = 1 },
                            new LayoutNode { VisualId = kpis.Id, RowSpan = 2 },
                            LayoutNode.Row(LayoutNode.Item(quarters.Id), LayoutNode.Item(regions.Id)),
                            new LayoutNode { VisualId = matrix.Id, RowSpan = 2 },
                        ],
                    },
                    new BandLayout
                    {
                        Mode = BandMode.Scroll,
                        Items = [LayoutNode.Item(categories.Id), LayoutNode.Item(topProducts.Id), LayoutNode.Item(lastYear.Id), new LayoutNode { VisualId = note.Id, Width = 360 }],
                    },
                ],
            },
        };
    }
}
