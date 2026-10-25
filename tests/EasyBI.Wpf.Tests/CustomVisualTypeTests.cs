using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Shapes;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>A visual type declared by the application goes through the same paths as EasyBI's.</summary>
public class CustomVisualTypeTests
{
    private sealed record Sale(string Region, string Product, string Channel, decimal Amount, int Quantity);

    private static readonly VisualType TreemapType = new("test.treemap");

    private static VisualTypeDefinition Treemap() => new(TreemapType, () => "Treemap")
    {
        Group = VisualTypeGroup.Composition,
        Axis = new() { MinFields = 1, MaxFields = 2, Caption = () => "Groups" },
        Legend = VisualWell.Unused,
        Values = new() { MinFields = 1, MaxFields = 1, Caption = () => "Size" },
        HasTooltips = true,
        Options = [RendererOption.Toggle("labels", () => "Labels", @default: true)],
    };

    private sealed class TreemapRenderer(params VisualTypeDefinition[] brings) : IVisualRenderer
    {
        public string Name => "Treemap";

        public RendererCapabilities Capabilities { get; } = new(new HashSet<VisualType> { TreemapType }) { VisualTypes = brings };

        public FrameworkElement Render(RenderRequest request) => new Border();
    }

    private static EasyBIContext Context() => EasyBIContext.Create().AddModel(
        SemanticModel.For(new[] { new Sale("North", "Tea", "Web", 1m, 1) }, "sales")
            .Dimension(s => s.Region, "Region")
            .Dimension(s => s.Product, "Product")
            .Dimension(s => s.Channel, "Channel")
            .Measure(s => s.Amount, "Amount")
            .Measure(s => s.Quantity, "Quantity"));

    private static DesignerViewModel Designer(EasyBIContext context)
    {
        var designer = new DesignerViewModel(context);
        designer.Load(new VisualDefinition { Model = "sales", Type = TreemapType, Axis = [new("Region")], Values = [new("Amount")] });
        return designer;
    }

    [Fact]
    public void A_declared_type_is_offered_in_its_section_of_the_gallery_and_greyed_without_a_renderer()
    {
        var designer = new DesignerViewModel(Context().AddVisualType(Treemap()));

        var option = designer.TypeOptions.Single(o => o.Type == TreemapType);
        Assert.Equal("Treemap", option.Label);
        Assert.Contains(option, designer.TypeGroups.Single(g => g.Value == VisualTypeGroup.Composition).Options);
        Assert.False(option.IsAvailable);

        var drawn = new DesignerViewModel(Context().AddVisualType(Treemap()).UseRenderer(new TreemapRenderer()));
        Assert.True(drawn.TypeOptions.Single(o => o.Type == TreemapType).IsAvailable);
    }

    [Fact]
    public void A_renderer_declares_the_types_it_brings()
    {
        var context = Context().UseRenderer(new TreemapRenderer(Treemap()));

        Assert.Equal("Treemap", context.VisualTypes.Get(TreemapType).Caption());
        Assert.Same(context.Renderers[0], context.ResolveRenderer(TreemapType));

        // Plugged in again (another engine around it): declared once
        context.UseRenderer(new TreemapRenderer(Treemap()));
        Assert.Single(context.VisualTypes.Definitions, d => d.Type == TreemapType);
    }

    [Fact]
    public void A_hidden_type_leaves_the_gallery()
    {
        var context = Context();
        context.VisualTypes.Hide(VisualType.Waterfall);

        Assert.DoesNotContain(new DesignerViewModel(context).TypeOptions, o => o.Type == VisualType.Waterfall);
        Assert.Contains(new DesignerViewModel(Context()).TypeOptions, o => o.Type == VisualType.Waterfall);
    }

    [Fact]
    public void The_wells_take_the_names_and_the_limits_of_the_declared_type()
    {
        var designer = Designer(Context().AddVisualType(Treemap()));

        Assert.Equal(("Groups", false, "Size"), (designer.AxisWell.Label, designer.LegendWell.IsUsed, designer.ValuesWell.Label));
        Assert.True(designer.TooltipWell.IsUsed);

        var quantity = designer.Measures.Single(m => m.Name == "Quantity");
        Assert.False(designer.CanDrop(quantity, Shelf.Values));
        Assert.Equal("Size holds a single field", designer.RefusalReason(quantity, Shelf.Values));

        designer.Drop(designer.Dimensions.Single(d => d.Name == "Product"), Shelf.Axis);
        var channel = designer.Dimensions.Single(d => d.Name == "Channel");
        Assert.False(designer.CanDrop(channel, Shelf.Axis));
        Assert.Equal("Groups holds at most 2 fields", designer.RefusalReason(channel, Shelf.Axis));
    }

    [Fact]
    public void The_settings_of_a_type_are_offered_by_the_designer_and_saved_under_its_id()
    {
        var designer = Designer(Context().AddVisualType(Treemap()).UseRenderer(new TreemapRenderer()));

        var labels = designer.RendererOptions.Single(o => o.Option.Key == "labels");
        Assert.True(labels.IsOn);

        labels.IsOn = false;

        Assert.False(designer.Visual!.GetTypeOption("labels", true));
        Assert.True(designer.Visual!.Extensions!.ContainsKey("test.treemap"));
    }

    [Fact]
    public void A_field_dropped_on_a_visual_of_a_declared_type_follows_its_wells()
    {
        var context = Context().AddVisualType(Treemap());
        var model = context.Models[0];
        var visual = new VisualDefinition { Model = "sales", Type = TreemapType, Axis = [new("Region")], Values = [new("Amount")] };
        FieldDescriptor Field(string name) => model.Metadata.Fields.Single(f => f.Name == name);

        // No legend: the dimension goes on the axis; one value only
        Assert.Equal(["Region", "Product"], FieldVisuals.AddField(visual, model, Field("Product"), context.VisualTypes)!.Axis.Select(a => a.Field));
        Assert.Null(FieldVisuals.AddField(visual, model, Field("Quantity"), context.VisualTypes));
    }

    [Fact]
    public void A_type_without_an_icon_shows_the_default_one() => Sta.Run(() =>
    {
        var icon = new VisualTypeIcon { Type = TreemapType }.Hosted();
        icon.ApplyTemplate();

        Assert.Null(icon.Geometry);
        Assert.NotNull(((Path)icon.Template.FindName("Icon", icon)).Data);
    });

    [Fact]
    public void An_application_gives_an_icon_to_a_type_by_its_key() => Sta.Run(() =>
    {
        var resources = (ResourceDictionary)XamlReader.Parse("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:bi="https://github.com/TheoCvg/EasyBI">
                <Geometry x:Key="{bi:VisualTypeIconKey test.treemap}">M0,0 L16,16</Geometry>
            </ResourceDictionary>
            """);
        var icon = new VisualTypeIcon { Type = TreemapType, Resources = resources }.Hosted();

        Assert.NotNull(icon.Geometry);
    });
}
