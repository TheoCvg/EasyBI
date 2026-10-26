using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Tests;

/// <summary>Cross-filtering from a <see cref="VisualView"/>: what its renderer receives and reports.</summary>
public class VisualViewSelectionTests
{
    private sealed record Sale(string Region, decimal Amount);

    // Keeps the last request, so that the test can click like the renderer would
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
        Model = "sales",
        Type = VisualType.Bar,
        Axis = [new("Region")],
        Values = [new("Amount")],
    };

    // Loaded, queued refreshes and query continuations run on the dispatcher
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    // Shows the view, away from the screen, once its chart is drawn
    private static void Shown(Action<VisualView, RecordingRenderer, FilterContext> test) => Sta.Run(() =>
    {
        // Query continuations come back to the dispatcher, as in an application
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var renderer = new RecordingRenderer();
        var context = EasyBIContext.Create()
            .AddModel(SemanticModel.For(new Sale[] { new("Nord", 1m), new("Sud", 2m), new("Est", 3m) }, "sales")
                .Dimension(s => s.Region, "Région")
                .Measure(s => s.Amount, "CA"))
            .UseRenderer(renderer);
        var filters = new FilterContext();
        var view = new VisualView { Context = context, Definition = ByRegion, FilterContext = filters };
        var window = new Window { Content = view, Width = 300, Height = 200, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            // The query runs on the thread pool
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (renderer.Last is null && DateTime.UtcNow < deadline)
            {
                Pump();
                Thread.Sleep(5);
            }
            Assert.NotNull(renderer.Last);
            test(view, renderer, filters);
        }
        finally
        {
            window.Close();
        }
    });

    // Clicks like the renderer would, on the chart drawn last
    private static void Pick(RecordingRenderer renderer, string region)
    {
        var request = renderer.Last!;
        request.Select!(new CategorySelection(Category(request, region)));
        Pump();
    }

    private static int Category(RenderRequest request, string name) => request.Chart.Categories.ToList().IndexOf(name);

    [Fact]
    public void Ctrl_click_adds_an_element_and_the_redrawn_chart_emphasizes_both() => Shown((view, renderer, filters) =>
    {
        var picked = 0;
        view.DataSelected += (_, _) => picked++;
        Pick(renderer, "Nord");

        using (ModifierKeysState.Simulate(ModifierKeys.Control))
            Pick(renderer, "Sud");

        Assert.Equal(2, picked);
        Assert.Equal(2, Assert.Single(filters.Selections).Elements.Count);
        var drawn = renderer.Last!;
        Assert.True(drawn.IsEmphasized(Category(drawn, "Nord"), 0));
        Assert.True(drawn.IsEmphasized(Category(drawn, "Sud"), 0));
        Assert.False(drawn.IsEmphasized(Category(drawn, "Est"), 0));
    });

    [Fact]
    public void A_click_without_ctrl_replaces_the_selection() => Shown((_, renderer, filters) =>
    {
        Pick(renderer, "Nord");

        Pick(renderer, "Sud");

        Assert.Equal(["Sud"], Assert.Single(filters.Selections).Elements.Select(e => (string?)e.Values[0]));
    });
}
