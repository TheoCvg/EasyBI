using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Renderers.LiveCharts2;
using EasyBI.Renderers.OxyPlot;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.Renderers;
using LiveChartsCore.SkiaSharpView.WPF;
using OxyPlot.Wpf;

namespace EasyBI.Wpf.Tests;

/// <summary>Legends: the position chosen in the designer, and their pages turned by a pager over the chart.</summary>
public class LegendUiTests
{
    private sealed record Sale(string Region, string Product, decimal Amount);

    // 20 series: too many for the legend of a 500 x 300 chart
    private static RenderRequest Request(LegendPager pager, ChartLegend position = ChartLegend.Right)
    {
        var visual = new VisualDefinition { Mark = MarkType.Bar, LegendPosition = position, Values = [new("Amount")] };
        var chart = new ChartData("Region", ["A", "B"],
            [.. Enumerable.Range(0, 20).Select(i => new ChartSeries($"Product {i}", [i, i + 1], null) { MeasureKey = "Amount", Mark = MarkType.Bar })]);
        return new RenderRequest(visual, new ResultSet([], []), chart, BiTheme.Default) { LegendPager = pager };
    }

    // Lays the chart out in a window away from the screen, as the legend is fitted to its actual size
    private static void Shown(FrameworkElement chart, Action test)
    {
        var window = new Window { Content = chart, Width = 500, Height = 300, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            test();
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void LiveCharts2_shows_the_legend_entries_of_the_pagers_page() => Sta.Run(() =>
    {
        var pager = new LegendPager();
        var view = (CartesianChart)new LiveCharts2Renderer().Render(Request(pager));
        Shown(view, () =>
        {
            var entries = view.Series.Take(20).ToArray();
            Assert.Equal(ChartLegend.Right, pager.Position);
            Assert.True(pager.PageCount > 1);
            Assert.True(entries[0].IsVisibleAtLegend);
            Assert.False(entries[19].IsVisibleAtLegend);

            pager.GoTo(pager.PageCount - 1);

            Assert.False(entries[0].IsVisibleAtLegend);
            Assert.True(entries[19].IsVisibleAtLegend);
        });
    });

    [Fact]
    public void OxyPlot_shows_the_legend_entries_of_the_pagers_page_where_the_visual_asks() => Sta.Run(() =>
    {
        var pager = new LegendPager();
        var view = (PlotView)new OxyPlotRenderer().Render(Request(pager, ChartLegend.Top));
        Shown(view, () =>
        {
            var model = view.Model!;
            Assert.Equal(OxyPlot.Legends.LegendPosition.TopCenter, model.Legends.Single().LegendPosition);
            Assert.Equal(ChartLegend.Top, pager.Position);
            Assert.True(pager.PageCount > 1);
            Assert.False(model.Series[19].RenderInLegend);

            pager.GoTo(pager.PageCount - 1);

            Assert.False(model.Series[0].RenderInLegend);
            Assert.True(model.Series[19].RenderInLegend);
        });
    });

    [Fact]
    public void The_pager_shows_while_the_legend_has_pages_next_to_it_and_turns_them() => Sta.Run(() =>
    {
        var pager = new LegendPager();
        var control = new ChartLegendPager { Pager = pager };
        Assert.Equal(Visibility.Collapsed, control.Visibility);

        pager.Update(ChartLegend.Left, 3);
        Assert.Equal((Visibility.Visible, "1/3", HorizontalAlignment.Left), (control.Visibility, control.PageText, control.HorizontalAlignment));
        Assert.False(control.PreviousCommand.CanExecute(null));

        control.NextCommand.Execute(null);
        Assert.Equal((1, "2/3"), (pager.Page, control.PageText));
        control.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
        Assert.Equal(2, pager.Page);
        Assert.False(control.NextCommand.CanExecute(null));

        pager.Update(ChartLegend.Top, 3);
        Assert.Equal(VerticalAlignment.Top, control.VerticalAlignment);
        pager.Update(ChartLegend.Top, 1);
        Assert.Equal(Visibility.Collapsed, control.Visibility);
    });

    [Fact]
    public void A_visual_view_gives_charts_a_pager_over_them() => Sta.Run(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var context = EasyBIContext.Create()
            .AddModel(SemanticModel.For(new Sale[] { new("Nord", "Vélo", 1m) }, "sales")
                .Dimension(s => s.Region, "Région")
                .Measure(s => s.Amount, "CA"))
            .UseRenderer(new OxyPlotRenderer());
        var view = new VisualView
        {
            Context = context,
            Definition = new VisualDefinition { Model = "sales", Mark = MarkType.Bar, Axis = [new("Region")], Values = [new("Amount")] },
        };
        Shown(view, () =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            ChartLegendPager? pager = null;
            while (pager is null && DateTime.UtcNow < deadline)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                pager = Descendants(view).OfType<ChartLegendPager>().FirstOrDefault();
                Thread.Sleep(5);
            }

            Assert.NotNull(pager?.Pager);
            Assert.Contains(Descendants(view), e => e is PlotView);
        });
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Fact]
    public void The_designer_chooses_the_legend_position_of_charts()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new Sale("Nord", "Vélo", 1m) }, "sales")
                .Dimension(s => s.Region, "Region")
                .Dimension(s => s.Product, "Product")
                .Measure(s => s.Amount, "Amount")));
        designer.Load(new VisualDefinition { Model = "sales", Axis = [new("Region")], Legend = new("Product"), Values = [new("Amount")] });
        var property = designer.Properties.Single(s => s.Key == DesignerPropertySections.Visual)["LegendPosition"]!;
        Assert.True(property.IsAvailable);

        property.Value = ChartLegend.Bottom;
        Assert.Equal(ChartLegend.Bottom, designer.Visual!.LegendPosition);
        designer.Load(designer.Visual);
        Assert.Equal(ChartLegend.Bottom, designer.LegendPosition);

        // A KPI has no legend: the choice is hidden and not saved
        designer.SelectedMark = MarkType.Kpi;
        Assert.False(property.IsAvailable);
        Assert.Equal(ChartLegend.Auto, designer.Visual!.LegendPosition);
    }
}
