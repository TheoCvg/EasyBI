using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Core.Tests;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

[Collection(CultureCollection.Name)]
public class RendererOptionsDesignerTests
{
    private sealed record Sale(string Region, decimal Amount);

    private sealed class OptionsRenderer : IVisualRenderer
    {
        public string Name => "Test";

        public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType> { MarkType.Bar, MarkType.Line, MarkType.Pie })
        {
            Options =
            [
                RendererOption.Toggle("scrollable", () => EasyBILocalization.UICulture.TwoLetterISOLanguageName == "fr" ? "Défilement" : "Scrolling",
                    appliesTo: v => v.Mark.IsCartesian()),
                RendererOption.Choice("style", () => "Style", [new("flat", () => "Flat"), new("smooth", () => "Smooth")]),
                RendererOption.Number("size", () => "Size", 8, minimum: 1, maximum: 20),
            ],
        };

        public FrameworkElement Render(RenderRequest request) => new Border();
    }

    private static DesignerViewModel Designer()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create()
            .AddModel(SemanticModel.For(new[] { new Sale("Nord", 1m) }, "sales")
                .Dimension(s => s.Region, "Region")
                .Measure(s => s.Amount, "Amount"))
            .UseRenderer(new OptionsRenderer()));
        designer.Load(new VisualDefinition { Model = "sales", Axis = [new("Region")], Values = [new("Amount")] });
        return designer;
    }

    private static RendererOptionViewModel Option(DesignerViewModel designer, string key) =>
        designer.RendererOptions.Single(o => o.Option.Key == key);

    [Fact]
    public void Designer_offers_the_options_of_the_renderer_drawing_the_visual()
    {
        var designer = Designer();

        Assert.True(designer.HasRendererOptions);
        Assert.Equal(["scrollable", "style", "size"], designer.RendererOptions.Select(o => o.Option.Key));
        Assert.Equal([false, true, false], designer.RendererOptions.Select(o => o.IsChoice));
        Assert.Equal("flat", Option(designer, "style").SelectedChoice!.Value);
        Assert.Equal("8", Option(designer, "size").NumberText);
    }

    [Fact]
    public void Options_that_do_not_apply_to_the_visual_are_not_offered()
    {
        var designer = Designer();

        designer.SelectedMark = MarkType.Pie;
        Assert.DoesNotContain(designer.RendererOptions, o => o.Option.Key == "scrollable");

        designer.SelectedMark = MarkType.Table; // drawn by the built-in table renderer, which has no options
        Assert.False(designer.HasRendererOptions);
    }

    [Fact]
    public void Changing_an_option_saves_it_with_the_visual()
    {
        var designer = Designer();

        Option(designer, "scrollable").IsOn = true;
        Option(designer, "style").SelectedChoice = Option(designer, "style").Choices[1];

        Assert.True(designer.Visual!.GetRendererOption<bool>("Test", "scrollable"));
        Assert.Equal("smooth", designer.Visual!.GetRendererOption<string>("Test", "style"));
    }

    [Fact]
    public void Going_back_to_the_default_value_removes_the_option_from_the_visual()
    {
        var designer = Designer();

        Option(designer, "scrollable").IsOn = true;
        Option(designer, "scrollable").IsOn = false;

        Assert.Null(designer.Visual!.Extensions);
    }

    [Fact]
    public void Option_view_models_are_kept_while_the_visual_changes()
    {
        var designer = Designer();
        var size = Option(designer, "size");

        size.NumberText = "12";
        designer.Title = "Renamed";

        Assert.Same(size, Option(designer, "size"));
        Assert.Equal(12d, designer.Visual!.GetRendererOption<double>("Test", "size"));
    }

    [Fact]
    public void Numbers_are_read_in_the_easybi_culture_and_kept_within_bounds()
    {
        using var _ = TestCulture.Use("fr-FR");
        var designer = Designer();
        var size = Option(designer, "size");

        size.NumberText = "2,5";
        Assert.Equal(2.5, designer.Visual!.GetRendererOption<double>("Test", "size"));

        size.NumberText = "50";
        Assert.Equal("20", size.NumberText);

        size.NumberText = "abc";
        Assert.Equal(20d, designer.Visual!.GetRendererOption<double>("Test", "size"));
    }

    [Fact]
    public void Loading_a_visual_shows_its_option_values()
    {
        var designer = Designer();
        var saved = designer.Visual!.WithRendererOption("Test", "scrollable", true).WithRendererOption("Test", "style", "smooth");

        designer.Load(saved);

        Assert.True(Option(designer, "scrollable").IsOn);
        Assert.Equal("smooth", Option(designer, "style").SelectedChoice!.Value);
    }

    [Fact]
    public void Option_labels_follow_the_language()
    {
        var designer = Designer();
        var scrollable = Option(designer, "scrollable");
        var changed = new List<string?>();
        scrollable.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        using (TestCulture.Use("fr-FR"))
        {
            Assert.Contains(nameof(RendererOptionViewModel.Label), changed);
            Assert.Equal("Défilement", scrollable.Label);
        }
    }

    [Fact]
    public void Designer_shows_the_options_under_the_shelves_in_their_slot() => Sta.Run(() =>
    {
        var designer = new ReportDesigner
        {
            Context = EasyBIContext.Create()
                .AddModel(SemanticModel.For(new[] { new Sale("Nord", 1m) }, "sales").Dimension(s => s.Region, "Region").Measure(s => s.Amount, "Amount"))
                .UseRenderer(new OptionsRenderer()),
        };
        designer.Measure(new Size(1000, 700));
        designer.Arrange(new Rect(0, 0, 1000, 700));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); // bindings

        var options = (ItemsControl)LogicalTreeHelper.FindLogicalNode(designer, "RendererOptionList");
        Assert.Same(designer.ViewModel!.RendererOptions, options.ItemsSource);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)options.Parent).Visibility);
    });
}
