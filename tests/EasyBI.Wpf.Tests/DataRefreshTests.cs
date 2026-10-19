using System.Windows;
using System.Windows.Threading;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Tests;

public class DataRefreshTests
{
    /// <summary>A model whose data can change behind the same query: it counts the queries it receives.</summary>
    private sealed class CountingModel(string name) : ISemanticModel
    {
        public int Queries { get; private set; }

        public ModelMetadata Metadata { get; } = new(name, name,
        [
            new FieldDescriptor("Region", "Region", FieldKind.Dimension, DataType.Text),
            new FieldDescriptor("Amount", "Amount", FieldKind.Measure, DataType.Number, Aggregation.Sum),
        ]);

        public Task<ResultSet> ExecuteAsync(AnalyticQuery query, CancellationToken cancellationToken = default)
        {
            Queries++;
            return Task.FromResult(new ResultSet(
                [new ResultColumn("Region", "Region", FieldKind.Dimension, DataType.Text), new ResultColumn("Amount", "Amount", FieldKind.Measure, DataType.Number)],
                [["North", (object?)(double)Queries]]));
        }

        public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<object?>>([]);
    }

    private static VisualDefinition Table(string model) =>
        new() { Model = model, Mark = MarkType.Table, Axis = [new("Region")], Values = [new("Amount")] };

    // Loaded, queued refreshes and query continuations run on the dispatcher
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    /// <summary>A visual only queries once loaded: shows it in a window away from the screen.</summary>
    private static Window Show(VisualView view)
    {
        var window = new Window { Content = view, Width = 300, Height = 200, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        Pump();
        return window;
    }

    [Fact]
    public void Refreshing_the_data_queries_the_visuals_again_although_their_query_did_not_change() => Sta.Run(() =>
    {
        var model = new CountingModel("sales");
        var context = EasyBIContext.Create().AddModel(model);
        var window = Show(new VisualView { Context = context, Definition = Table("sales") });
        Assert.Equal(1, model.Queries);

        context.RefreshData();
        Pump();

        Assert.Equal(2, model.Queries);
        window.Close();
    });

    [Fact]
    public void Refreshing_the_data_of_one_model_leaves_the_visuals_of_the_others_alone() => Sta.Run(() =>
    {
        var sales = new CountingModel("sales");
        var stock = new CountingModel("stock");
        var context = EasyBIContext.Create().AddModel(sales).AddModel(stock);
        var window = Show(new VisualView { Context = context, Definition = Table("sales") });

        context.RefreshData("stock");
        Pump();
        Assert.Equal(1, sales.Queries);

        context.RefreshData("sales");
        Pump();
        Assert.Equal(2, sales.Queries);
        window.Close();
    });
}
