using EasyBI.Abstractions;

namespace EasyBI.Sample;

/// <summary>
/// A data source that does not answer, to show how visuals fail: every query throws, as a database out of reach
/// would. It declares the same fields as the real sources so that the dashboard's visuals stay valid.
/// </summary>
public sealed class UnavailableModel(ModelMetadata metadata) : ISemanticModel
{
    public ModelMetadata Metadata { get; } = metadata;

    public async Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
    {
        await Task.Delay(300, cancellationToken);
        throw new TimeoutException("The sales database did not answer within 30 seconds.",
            new InvalidOperationException("A network-related error occurred while establishing a connection to the server."));
    }

    // The filter bar offers no values rather than failing
    public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<object?>>([]);
}
