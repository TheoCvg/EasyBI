using System.Diagnostics;
using EasyBI.Abstractions;

namespace EasyBI.Sample;

/// <summary>Queries that ran together (a dashboard refresh): how many, how long in all, the slowest one.</summary>
public sealed record QueryBatch(int Queries, TimeSpan Duration, TimeSpan Slowest);

/// <summary>
/// The demo's model: forwards every query to the data source currently chosen (in memory or a database) and times
/// it. The sources declare the same fields under the same name, so visuals keep working after a switch.
/// </summary>
public sealed class SwitchableModel(ISemanticModel source) : ISemanticModel
{
    private readonly object _lock = new();
    private readonly Stopwatch _batch = new();
    private int _running;
    private int _batchQueries;
    private TimeSpan _slowest;

    public ISemanticModel Source { get; set; } = source;

    /// <summary>Raised, on any thread, when a query starts or ends, with the number of queries running.</summary>
    public event EventHandler<int>? RunningChanged;

    /// <summary>Raised, on any thread, when the last running query ends.</summary>
    public event EventHandler<QueryBatch>? BatchCompleted;

    public ModelMetadata Metadata => Source.Metadata;

    public async Task<ResultSet> ExecuteAsync(AnalyticQuery query, CancellationToken cancellationToken = default)
    {
        int running;
        lock (_lock)
        {
            if (_running == 0)
            {
                _batch.Restart();
                _batchQueries = 0;
                _slowest = TimeSpan.Zero;
            }
            running = ++_running;
        }
        RunningChanged?.Invoke(this, running);

        var watch = Stopwatch.StartNew();
        try
        {
            return await Source.ExecuteAsync(query, cancellationToken);
        }
        finally
        {
            QueryBatch? completed = null;
            lock (_lock)
            {
                _batchQueries++;
                if (watch.Elapsed > _slowest) _slowest = watch.Elapsed;
                running = --_running;
                if (running == 0) completed = new QueryBatch(_batchQueries, _batch.Elapsed, _slowest);
            }
            RunningChanged?.Invoke(this, running);
            if (completed is not null) BatchCompleted?.Invoke(this, completed);
        }
    }

    public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default) =>
        Source.GetDistinctValuesAsync(field, cancellationToken);
}
