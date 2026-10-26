using System.Windows;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

public class ComboChartDesignerTests
{
    private sealed record Sale(string Region, decimal Amount, int Quantity);

    private static EasyBIContext Context() => EasyBIContext.Create().AddModel(
        SemanticModel.For(new[] { new Sale("North", 1m, 1) }, "sales")
            .Dimension(s => s.Region, "Region")
            .Measure(s => s.Amount, "Revenue")
            .Measure(s => s.Quantity, "Units"));

    private sealed class FakeRenderer(string name, params VisualType[] types) : IVisualRenderer
    {
        public string Name => name;

        public RendererCapabilities Capabilities { get; } = new(types.ToHashSet());

        public FrameworkElement Render(RenderRequest request) => throw new NotSupportedException();
    }

    [Fact]
    public void Each_measure_can_choose_its_chart_type_and_axis()
    {
        var designer = new DesignerViewModel(Context());
        designer.AddToDefaultShelf(designer.Measures.First());
        designer.AddToDefaultShelf(designer.Measures.Last());

        var units = designer.Values[1];
        units.SeriesType = VisualType.Line;
        units.SecondaryAxis = true;

        Assert.Equal([(null, false), (VisualType.Line, true)], designer.Visual!.Values.Select(v => (v.SeriesType, v.SecondaryAxis)));
        Assert.Same(ShelfItemViewModel.SeriesTypeOptions[0], designer.Values[0].SeriesTypeOption); // "as the visual"
    }

    [Fact]
    public void Chart_type_options_only_show_for_cartesian_charts()
    {
        var designer = new DesignerViewModel(Context());
        designer.AddToDefaultShelf(designer.Measures.First());
        Assert.True(designer.Values[0].ShowSeriesOptions);

        designer.SelectedType = VisualType.Pie;
        Assert.False(designer.Values[0].ShowSeriesOptions);

        designer.SelectedType = VisualType.Area;
        Assert.True(designer.Values[0].ShowSeriesOptions);
    }

    [Fact]
    public void Editing_a_combined_chart_keeps_the_chart_types_of_its_measures()
    {
        var designer = new DesignerViewModel(Context());
        designer.Load(new VisualDefinition
        {
            Model = "sales",
            Type = VisualType.Bar,
            Axis = [new("Region")],
            Values = [new("Amount") { SeriesType = VisualType.StackedBar }, new("Quantity") { SeriesType = VisualType.Scatter, SecondaryAxis = true }],
        });

        Assert.Equal([(VisualType.StackedBar, false), (VisualType.Scatter, true)], designer.Values.Select(v => (v.SeriesType, v.SecondaryAxis)));
        Assert.Equal(new[] { new MeasureRef("Amount") { SeriesType = VisualType.StackedBar }, new MeasureRef("Quantity") { SeriesType = VisualType.Scatter, SecondaryAxis = true } }, designer.Visual!.Values);
    }

    [Fact]
    public void The_renderer_of_a_combined_chart_supports_every_chart_type_it_uses()
    {
        var barsOnly = new FakeRenderer("bars", VisualType.Bar);
        var combo = new FakeRenderer("combo", VisualType.Bar, VisualType.Line);
        var context = Context().UseRenderer(barsOnly).UseRenderer(combo);
        var visual = new VisualDefinition { Model = "sales", Type = VisualType.Bar, Values = [new("Amount"), new("Quantity") { SeriesType = VisualType.Line }] };

        Assert.Same(barsOnly, context.ResolveRenderer(VisualType.Bar)); // the active renderer
        Assert.Same(combo, context.ResolveRenderer(visual));
        Assert.Null(context.ResolveRenderer(visual with { Values = [new("Amount") { SeriesType = VisualType.Area }] }));
    }
}
