using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            .AddModel(SalesData.CreateModel(SalesData.Generate()))
            .UseRenderer(new LiveCharts2Renderer())
            .UseRenderer(new OxyPlotRenderer());

        InitializeComponent();
        DataContext = this;
        Dashboard.ItemsSource = _visuals;
        RendererPicker.ItemsSource = Context.Renderers;
        RendererPicker.SelectedItem = Context.ActiveRenderer;
        foreach (var visual in DefaultVisuals()) _visuals.Add(visual);
    }

    public EasyBIContext Context { get; }

    /// <summary>Global filters and cross-filtering of the dashboard.</summary>
    public FilterContext Filters { get; } = new();

    private void OnRendererChanged(object sender, SelectionChangedEventArgs e) =>
        Context.ActiveRenderer = (IVisualRenderer?)RendererPicker.SelectedItem;

    private void OnNewVisual(object sender, RoutedEventArgs e)
    {
        Designer.Clear();
        PublishButton.Content = "Ajouter au tableau de bord";
    }

    private void OnPublishVisual(object sender, RoutedEventArgs e)
    {
        if (Designer.Visual is not { } visual) return;
        if (VisualValidator.Validate(visual) is { } problem)
        {
            MessageBox.Show(this, problem, "Visuel incomplet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var index = _visuals.ToList().FindIndex(v => v.Id == visual.Id);
        if (index >= 0) _visuals[index] = visual;
        else _visuals.Add(visual);
        Designer.Clear();
        PublishButton.Content = "Ajouter au tableau de bord";
        Tabs.SelectedIndex = 0;
    }

    private void OnEditVisual(object sender, RoutedEventArgs e)
    {
        Designer.Edit((VisualDefinition)((FrameworkElement)sender).DataContext);
        PublishButton.Content = "Mettre à jour le tableau de bord";
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
        var dialog = new SaveFileDialog { Filter = "Rapport EasyBI (*.easybi.json)|*.easybi.json", FileName = "rapport.easybi.json" };
        if (dialog.ShowDialog(this) != true) return;
        var report = new ReportDefinition { Title = "Tableau de bord", Visuals = _visuals.ToArray(), Filters = Filters.Filters.ToArray() };
        File.WriteAllText(dialog.FileName, ReportSerializer.Serialize(report));
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Rapport EasyBI (*.easybi.json)|*.easybi.json|JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var report = ReportSerializer.Deserialize(File.ReadAllText(dialog.FileName));
            if (Context.Validate(report) is { Count: > 0 } problems)
            {
                MessageBox.Show(this, "Le rapport référence des éléments inconnus ; les visuels concernés afficheront une erreur.\n\n• "
                    + string.Join("\n• ", problems), "Rapport incomplet", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            _visuals.Clear();
            foreach (var visual in report.Visuals) _visuals.Add(visual);
            Filters.Load(report.Filters);
            Tabs.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Rapport illisible", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static IEnumerable<VisualDefinition> DefaultVisuals() =>
    [
        new()
        {
            Title = "Indicateurs clés",
            Model = "sales",
            Mark = MarkType.Kpi,
            Values = [new("Amount"), new("Margin"), new("Count")],
        },
        new()
        {
            Title = "CA par trimestre",
            Model = "sales",
            Mark = MarkType.Line,
            Axis = [new("Date", DateGrain.Quarter)],
            Values = [new("Amount")],
        },
        new()
        {
            Title = "CA par région et canal",
            Model = "sales",
            Mark = MarkType.Bar,
            Axis = [new("Region")],
            Legend = new("Channel"),
            Values = [new("Amount")],
            Sort = new("Region"),
        },
        new()
        {
            Title = "Répartition du CA par catégorie",
            Model = "sales",
            Mark = MarkType.Pie,
            Axis = [new("Product.Category")],
            Values = [new("Amount")],
        },
    ];
}
