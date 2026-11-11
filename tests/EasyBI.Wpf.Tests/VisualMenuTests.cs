using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Renderers;

namespace EasyBI.Wpf.Tests;

/// <summary>The menu of a visual, which the application fills with entries acting on what the user points at.</summary>
public class VisualMenuTests
{
    private sealed record Sale(string Region, decimal Amount);

    // Keeps the last request, so that the test can right click like the renderer would
    private sealed class RecordingRenderer : IVisualRenderer
    {
        public RenderRequest? Last { get; private set; }

        public string Name => "Recording";

        public RendererCapabilities Capabilities { get; } = new(new HashSet<VisualType> { VisualType.Bar });

        public FrameworkElement Render(RenderRequest request)
        {
            Last = request;
            return new Border();
        }
    }

    private static readonly VisualDefinition ByRegion = new()
    {
        Id = "regions",
        Model = "sales",
        Type = VisualType.Bar,
        Axis = [new("Region")],
        Values = [new("Amount")],
    };

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    // Shows the view, away from the screen, once its chart is drawn
    private static void Shown(Action<VisualView, RecordingRenderer, EasyBIContext> test) => Sta.Run(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var renderer = new RecordingRenderer();
        var context = EasyBIContext.Create()
            .AddModel(SemanticModel.For(new Sale[] { new("Nord", 1m), new("Sud", 2m) }, "sales")
                .Dimension(s => s.Region, "Région")
                .Measure(s => s.Amount, "CA"))
            .UseRenderer(renderer);
        context.Variables.Load([new ReportVariable { Name = "N", Model = "sales", Formula = "3" }]);
        var filters = new FilterContext();
        filters.Load([new ReportFilter("sales", new("Region", FilterOperator.NotIn, ["Est"]))]);
        var view = new VisualView { Context = context, Definition = ByRegion, FilterContext = filters };
        var window = new Window { Content = view, Width = 300, Height = 200, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (renderer.Last is null && DateTime.UtcNow < deadline)
            {
                Pump();
                Thread.Sleep(5);
            }
            Assert.NotNull(renderer.Last);
            test(view, renderer, context);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_right_click_on_a_bar_asks_the_application_for_the_entries_of_that_bar() => Shown((view, renderer, context) =>
    {
        var opening = new List<VisualMenuOpeningEventArgs>();
        context.VisualMenuOpening += (_, e) =>
        {
            opening.Add(e);
            e.Menu.Items.Add(new MenuItem { Header = "Go to the region" });
            // Tests do not open popups: an open one outlives the test's window
            e.Cancel = true;
        };
        var request = renderer.Last!;

        request.OpenMenu!(new CategorySelection(request.Chart.Categories.ToList().IndexOf("Sud")));
        view.OpenMenu(null, fromKeyboard: true);

        var bar = opening[0].Interaction;
        Assert.Same(view, opening[0].View);
        Assert.Equal(["Sud"], bar.Element!.Values);
        Assert.Equal(ByRegion, bar.Visual);
        Assert.Equal("Amount", bar.Measure!.Measure);
        Assert.Equal(["Region"], bar.Filters.Select(f => f.Field));
        Assert.Equal("3", bar.Parameters["N"]);
        Assert.Null(opening[1].Interaction.Element);
    });

    [Fact]
    public void A_menu_with_entries_is_given_to_the_view() => Sta.Run(() =>
    {
        var context = EasyBIContext.Create();
        var view = new VisualView { Context = context, Definition = ByRegion };
        context.VisualMenuOpening += (_, e) => e.Menu.Items.Add(new MenuItem { Header = "Entry" });

        var menu = context.MenuFor(view, view.InteractionFor(null)!);

        Assert.Single(menu!.Items);
    });

    [Fact]
    public void Without_entries_or_when_cancelled_no_menu_opens() => Shown((view, _, context) =>
    {
        Assert.Null(view.OpenMenu(new CategorySelection(0)));

        context.VisualMenuOpening += (_, e) =>
        {
            e.Menu.Items.Add(new MenuItem { Header = "Entry" });
            e.Cancel = true;
        };

        Assert.Null(view.OpenMenu(new CategorySelection(0)));
    });

    [Fact]
    public void The_interaction_of_a_visual_is_offered_without_a_menu() => Shown((view, renderer, _) =>
    {
        var interaction = view.InteractionFor(new CategorySelection(renderer.Last!.Chart.Categories.ToList().IndexOf("Nord")))!;

        Assert.Equal(["Nord"], interaction.Element!.Values);
        Assert.Equal(["Region", "Region"], interaction.DataFilters.Select(f => f.Field));
    });

    [Fact]
    public void A_table_reports_a_right_click_on_a_row_and_on_its_empty_space() => Sta.Run(() =>
    {
        var reported = new List<DataSelection?>();
        var result = new ResultSet([new ResultColumn("Region", "Région", FieldKind.Dimension, DataType.Text), new ResultColumn("Amount", "CA", FieldKind.Measure, DataType.Number)],
            [["Nord", 1m], ["Sud", 2m]]);
        var visual = ByRegion with { Type = VisualType.Table };
        var request = new RenderRequest(visual, result, ChartDataBuilder.Build(visual, result), BiTheme.Default) { OpenMenu = reported.Add };
        var grid = (DataGrid)new TableRenderer().Render(request);
        var window = new Window { Content = grid, Width = 300, Height = 200, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            Pump();
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1);
            RightClick(Descendant<TextBlock>(row));
            RightClick(grid);

            Assert.Equal([new RowSelection(1), null], reported);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_dashboard_saves_the_values_the_imposed_ones_replaced() => Sta.Run(() =>
    {
        var context = EasyBIContext.Create();
        var view = new Dashboard.DashboardView { Context = context };
        view.Load(new Dashboard.DashboardDocument
        {
            Report = new ReportDefinition { Variables = [new ReportVariable { Name = "N", Model = "sales", Formula = "3", Value = "4" }] },
        });

        context.Variables.Impose(new Dictionary<string, string?> { ["N"] = "9" });

        Assert.Equal("9", context.Variables.Find("N")!.Value);
        Assert.Equal("4", Assert.Single(view.Capture().Report.Variables).Value);
    });

    private static T Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (Descendant<T>(child) is { } deeper) return deeper;
        }
        return null!;
    }

    private static void RightClick(UIElement element) =>
        element.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
        {
            // The right button's own event is raised by each element from this one, as it tunnels
            RoutedEvent = UIElement.PreviewMouseUpEvent,
            Source = element,
        });
}
