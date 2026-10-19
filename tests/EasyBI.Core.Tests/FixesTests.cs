using System.Collections;
using System.Linq.Expressions;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Regression tests for engine fixes (null values, dates, pivot, sort, cancellation, concurrency).</summary>
public class FixesTests
{
    private sealed record Row(string? Region, string? Category, decimal Amount);

    private sealed record Dated(DateOnly Day, DateTimeOffset? At, decimal Amount);

    [Fact]
    public async Task Filter_on_empty_value_matches_nulls()
    {
        var model = SemanticModel.For(new Row[] { new("Nord", "A", 10m), new(null, "A", 5m), new("", "A", 1m) }, "m")
            .Dimension(r => r.Region, "Région")
            .Measure(r => r.Amount, "CA");

        var distinct = await model.GetDistinctValuesAsync("Region");
        Assert.Contains(null, distinct);

        var onlyNull = new AnalyticQuery([], [new("Amount")],
            Filters: [new("Region", FilterOperator.In, [InvariantValue.Format(null)])]);
        Assert.Equal(5m, Assert.Single((await model.ExecuteAsync(onlyNull)).Rows)[0]);

        var onlyEmpty = new AnalyticQuery([], [new("Amount")],
            Filters: [new("Region", FilterOperator.In, [InvariantValue.Format("")])]);
        Assert.Equal(1m, Assert.Single((await model.ExecuteAsync(onlyEmpty)).Rows)[0]);
    }

    [Fact]
    public async Task DateOnly_and_DateTimeOffset_are_dates_with_grains()
    {
        var model = SemanticModel.For(new Dated[]
            {
                new(new(2024, 2, 1), new DateTimeOffset(2024, 2, 1, 10, 0, 0, TimeSpan.Zero), 1m),
                new(new(2024, 5, 1), null, 2m),
            }, "m")
            .Dimension(d => d.Day, "Jour")
            .Dimension(d => d.At, "Horodatage")
            .Measure(d => d.Amount, "CA");

        Assert.Equal(DataType.Date, model.Metadata.GetField("Day").DataType);
        Assert.Equal(DataType.Date, model.Metadata.GetField("At").DataType);

        var byQuarter = await model.ExecuteAsync(new([new("Day", DateGrain.Quarter)], [new("Amount")]));
        Assert.Equal(["2024 Q1", "2024 Q2"], byQuarter.Rows.Select(r => r[0]!.ToString()));

        var byMonth = await model.ExecuteAsync(new([new("At", DateGrain.Month)], [new("Amount")],
            Filters: [new("At", FilterOperator.In, ["202402"], DateGrain.Month)]));
        Assert.Equal(1m, Assert.Single(byMonth.Rows)[1]);

        var byDay = await model.ExecuteAsync(new([new("Day", DateGrain.Day)], [new("Amount")],
            Filters: [new("Day", FilterOperator.In, [InvariantValue.Format(new DateOnly(2024, 5, 1))])]));
        Assert.Equal(new DateOnly(2024, 5, 1), Assert.Single(byDay.Rows)[0]);
    }

    [Fact]
    public async Task Categories_with_same_label_do_not_merge()
    {
        var model = SemanticModel.For(new Row[] { new("A / B", "C", 1m), new("A", "B / C", 2m) }, "m")
            .Dimension(r => r.Region, "Région")
            .Dimension(r => r.Category, "Catégorie")
            .Measure(r => r.Amount, "CA");
        var visual = new VisualDefinition { Model = "m", Axis = [new("Region"), new("Category")], Values = [new("Amount")] };

        var chart = (await VisualPipeline.PrepareAsync(model, visual)).Chart;

        Assert.Equal(["A / B / C", "A / B / C"], chart.Categories);
        Assert.Equal([2d, 1d], chart.Series[0].Values);
        Assert.Equal(["A", "B / C"], chart.CategoryValues[0]);
    }

    [Fact]
    public async Task Sort_and_top_are_pushed_to_the_provider()
    {
        var source = new RecordingQueryable<Row>(new Row[] { new("Nord", "A", 1m), new("Sud", "A", 3m), new("Est", "A", 2m) });
        var model = SemanticModel.For(source, "m").Dimension(r => r.Region, "Région").Measure(r => r.Amount, "CA");

        var result = await model.ExecuteAsync(new([new("Region")], [new("Amount")], Sort: [new("Amount", true)], Top: 2));

        Assert.Equal(["Sud", "Est"], result.Rows.Select(r => r[0]));
        var expression = Assert.Single(source.Log.Executed);
        Assert.Contains("OrderByDescending", expression);
        Assert.Contains("Take(2)", expression);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task The_grouping_key_holds_only_the_grouped_fields(int fields)
    {
        // A constant in the key ends up in SQL's GROUP BY (and a subquery), which slows databases down
        var source = new RecordingQueryable<Row>(new Row[] { new("Nord", "A", 1m), new("Sud", "B", 3m), new("Nord", "B", 2m) });
        var model = SemanticModel.For(source, "m").Dimension(r => r.Region, "Région").Dimension(r => r.Category, "Catégorie").Measure(r => r.Amount, "CA");
        FieldRef[] groupBy = [new("Region"), new("Category")];

        var result = await model.ExecuteAsync(new(groupBy[..fields], [new("Amount")]));

        Assert.Equal(fields == 1 ? 2 : 3, result.Rows.Count);
        var expression = Assert.Single(source.Log.Executed);
        Assert.DoesNotMatch(@"K\d = 0\b", expression);
        Assert.Contains("K1 = x.Region", expression);
    }

    [Fact]
    public async Task Cancelled_query_throws()
    {
        var model = SemanticModel.For(new Row[] { new("Nord", "A", 1m) }, "m").Dimension(r => r.Region, "Région").Measure(r => r.Amount, "CA");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.ExecuteAsync(new([new("Region")], [new("Amount")]), cts.Token));
    }

    [Fact]
    public async Task Shared_source_is_never_queried_concurrently()
    {
        var source = new RecordingQueryable<Row>(new Row[] { new("Nord", "A", 1m) }, delay: TimeSpan.FromMilliseconds(30));
        var model = SemanticModel.For(source, "m").Dimension(r => r.Region, "Région").Measure(r => r.Amount, "CA");

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => model.ExecuteAsync(new([new("Region")], [new("Amount")]))));

        Assert.Equal(6, source.Log.Executed.Count);
        Assert.Equal(1, source.Log.MaxConcurrency);
    }

    [Fact]
    public async Task Context_factory_opens_and_disposes_one_context_per_query()
    {
        var log = new QueryLog();
        var created = 0;
        var model = SemanticModel.For(
                () => { Interlocked.Increment(ref created); return new FakeContext(log); },
                context => context.Rows,
                "m")
            .Dimension(r => r.Region, "Région").Measure(r => r.Amount, "CA");

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => model.ExecuteAsync(new([new("Region")], [new("Amount")]))));
        await model.GetDistinctValuesAsync("Region");

        Assert.Equal(5, created);
        Assert.Equal(5, log.Disposed);
    }

    private sealed class FakeContext(QueryLog log) : IDisposable
    {
        public IQueryable<Row> Rows { get; } = new RecordingQueryable<Row>(new Row[] { new("Nord", "A", 1m) }, log);

        public void Dispose() => Interlocked.Increment(ref log.Disposed);
    }
}

internal sealed class QueryLog
{
    private int _running;

    public int Disposed;

    public List<string> Executed { get; } = [];

    public int MaxConcurrency { get; private set; }

    public IDisposable Enter(Expression expression)
    {
        var running = Interlocked.Increment(ref _running);
        lock (Executed)
        {
            Executed.Add(expression.ToString());
            MaxConcurrency = Math.Max(MaxConcurrency, running);
        }
        return new Exit(this);
    }

    private sealed class Exit(QueryLog log) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref log._running);
    }
}

/// <summary>
/// IQueryable that is not LINQ to objects (like an EF Core source): it logs
/// the executed queries and measures their concurrency.
/// </summary>
internal sealed class RecordingQueryable<T> : IOrderedQueryable<T>, IQueryProvider
{
    private readonly IQueryable<T> _inner;

    public RecordingQueryable(IEnumerable<T> items, QueryLog? log = null, TimeSpan delay = default)
        : this(items.AsQueryable(), log ?? new QueryLog(), delay) { }

    private RecordingQueryable(IQueryable<T> inner, QueryLog log, TimeSpan delay)
    {
        _inner = inner;
        Log = log;
        Delay = delay;
    }

    public QueryLog Log { get; }

    public TimeSpan Delay { get; }

    public Type ElementType => typeof(T);

    public Expression Expression => _inner.Expression;

    public IQueryProvider Provider => this;

    public IEnumerator<T> GetEnumerator()
    {
        using (Log.Enter(Expression))
        {
            if (Delay > TimeSpan.Zero) Thread.Sleep(Delay);
            return _inner.ToList().GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IQueryable CreateQuery(Expression expression) =>
        (IQueryable)Activator.CreateInstance(
            typeof(RecordingQueryable<>).MakeGenericType(expression.Type.GetGenericArguments()[0]),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
            [_inner.Provider.CreateQuery(expression), Log, Delay], null)!;

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
        new RecordingQueryable<TElement>(_inner.Provider.CreateQuery<TElement>(expression), Log, Delay);

    public object? Execute(Expression expression) => _inner.Provider.Execute(expression);

    public TResult Execute<TResult>(Expression expression) => _inner.Provider.Execute<TResult>(expression);
}
