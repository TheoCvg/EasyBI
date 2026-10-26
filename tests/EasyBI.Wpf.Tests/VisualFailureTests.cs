using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core.Tests;

namespace EasyBI.Wpf.Tests;

// In the culture collection: one test switches the language
[Collection(CultureCollection.Name)]
public class VisualFailureTests
{
    private static readonly ResultColumn[] Columns =
    [
        new("Region", "Region", FieldKind.Dimension, DataType.Text),
        new("Amount", "Amount", FieldKind.Measure, DataType.Number),
    ];

    /// <summary>A model whose first queries fail.</summary>
    private sealed class FailingModel(int failures) : ISemanticModel
    {
        public int Queries { get; private set; }

        public ModelMetadata Metadata { get; } = new("sales", "sales",
        [
            new FieldDescriptor("Region", "Region", FieldKind.Dimension, DataType.Text),
            new FieldDescriptor("Amount", "Amount", FieldKind.Measure, DataType.Number, Aggregation.Sum),
        ]);

        public Task<ResultSet> ExecuteAsync(AnalyticQuery query, CancellationToken cancellationToken = default) =>
            ++Queries <= failures
                ? Task.FromException<ResultSet>(new InvalidOperationException("The LINQ expression could not be translated.",
                    new NotSupportedException("GetValueOrDefault")))
                : Task.FromResult(new ResultSet(Columns, [["North", (object?)1d]]));

        public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<object?>>([]);
    }

    private sealed class FailingRenderer : IVisualRenderer
    {
        public string Name => "Broken";

        public RendererCapabilities Capabilities { get; } = new(new HashSet<VisualType> { VisualType.Bar });

        public FrameworkElement Render(RenderRequest request) => throw new ArgumentException("No color for this series.");
    }

    private static VisualDefinition Bar => new() { Title = "Sales", Model = "sales", Type = VisualType.Bar, Axis = [new("Region")], Values = [new("Amount")] };

    private static VisualDefinition Table => Bar with { Type = VisualType.Table };

    private static void Shown(VisualView view, Action test)
    {
        var window = new Window { Content = view, Width = 400, Height = 300, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            test();
        }
        finally
        {
            window.Close();
        }
    }

    // The query's continuation comes back on the dispatcher
    private static void Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        while (!condition() && DateTime.UtcNow < deadline);
    }

    private static IEnumerable<T> Find<T>(object element)
    {
        if (element is T found) yield return found;
        if (element is not DependencyObject parent) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
            foreach (var match in Find<T>(child))
                yield return match;
    }

    private static IReadOnlyList<string> Texts(VisualView view) => Find<TextBlock>(view).Select(t => t.Text).ToList();

    private static IReadOnlyList<string> Buttons(VisualView view) => Find<Button>(view).Select(b => (string)b.Content).ToList();

    [Fact]
    public void A_failed_query_shows_a_short_message_instead_of_the_exception() => Sta.Run(() =>
    {
        var view = new VisualView { Context = EasyBIContext.Create().AddModel(new FailingModel(1)), Definition = Table };
        Shown(view, () =>
        {
            Until(() => view.Failure is not null);

            Assert.Equal(VisualFailureStage.Query, view.Failure!.Stage);
            Assert.Contains("This visual could not be displayed.", Texts(view));
            Assert.DoesNotContain(Texts(view), t => t.Contains("LINQ"));
            Assert.Equal(["Retry", "See what happened"], Buttons(view));
        });
    });

    [Fact]
    public void Retry_runs_the_query_again_and_shows_the_visual() => Sta.Run(() =>
    {
        var model = new FailingModel(1);
        var view = new VisualView { Context = EasyBIContext.Create().AddModel(model), Definition = Table };
        Shown(view, () =>
        {
            Until(() => view.Failure is not null);

            Find<Button>(view).First().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Until(() => view.Failure is null);

            Assert.Null(view.Failure);
            Assert.Equal(2, model.Queries);
        });
    });

    [Fact]
    public void The_application_is_told_of_the_failure_and_can_change_the_message_and_hide_the_details() => Sta.Run(() =>
    {
        var context = EasyBIContext.Create().AddModel(new FailingModel(1));
        VisualFailedEventArgs? failed = null;
        context.VisualFailed += (_, e) =>
        {
            failed = e;
            e.Message = "The sales are unavailable.";
            e.ShowDetails = false;
        };
        var view = new VisualView { Context = context, Definition = Table };
        Shown(view, () =>
        {
            Until(() => view.Failure is not null);

            Assert.Equal("The LINQ expression could not be translated.", failed!.Exception.Message);
            Assert.Same(view.Definition, failed.Visual);
            Assert.Contains("The sales are unavailable.", Texts(view));
            Assert.Equal(["Retry"], Buttons(view));
        });
    });

    [Fact]
    public void A_renderer_that_throws_is_reported_with_its_name() => Sta.Run(() =>
    {
        var context = EasyBIContext.Create().AddModel(new FailingModel(0)).UseRenderer(new FailingRenderer());
        var view = new VisualView { Context = context, Definition = Bar };
        Shown(view, () =>
        {
            Until(() => view.Failure is not null);

            Assert.Equal((VisualFailureStage.Render, "Broken"), (view.Failure!.Stage, view.Failure.Renderer));
            Assert.Contains("This visual could not be displayed.", Texts(view));
        });
    });

    [Fact]
    public void The_details_name_the_visual_the_stage_and_every_exception() => Sta.Run(() =>
    {
        var failure = new VisualFailedEventArgs(Bar, new InvalidOperationException("Outer", new NotSupportedException("Inner")), VisualFailureStage.Render, "Broken");

        var window = new VisualFailureWindow(failure);

        Assert.Equal("The renderer Broken could not draw this visual.", window.Summary);
        Assert.Contains("Visual: Sales (Bar), model 'sales'", window.Details);
        Assert.Contains("Stage: Render, renderer 'Broken'", window.Details);
        Assert.Contains("System.InvalidOperationException: Outer", window.Details);
        Assert.Contains("System.NotSupportedException: Inner", window.Details);
        window.Close();
    });

    [Fact]
    public void The_message_follows_the_language() => Sta.Run(() =>
    {
        using (TestCulture.Use("fr-FR"))
        {
            var failure = new VisualFailedEventArgs(Bar, new InvalidOperationException("Outer"), VisualFailureStage.Query, null);

            Assert.Equal("Ce visuel n'a pas pu être affiché.", failure.Message);
            Assert.Equal("La source de données n'a pas pu exécuter la requête de ce visuel.", new VisualFailureWindow(failure).Summary);
        }
    });
}
