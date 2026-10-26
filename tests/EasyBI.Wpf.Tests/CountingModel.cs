using EasyBI.Abstractions;

namespace EasyBI.Wpf.Tests;

/// <summary>A model whose data can change behind the same query: it counts the queries it receives.</summary>
internal sealed class CountingModel(string name) : ISemanticModel
{
    public int Queries { get; private set; }

    public ModelMetadata Metadata { get; } = new(name, name,
    [
        new FieldDescriptor("Region", "Region", FieldKind.Dimension, DataType.Text),
        new FieldDescriptor("Amount", "Amount", FieldKind.Measure, DataType.Number, Aggregation.Sum),
    ]);

    public Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
    {
        Queries++;
        return Task.FromResult(new VisualData(new ResultSet(
            [new ResultColumn("Region", "Region", FieldKind.Dimension, DataType.Text), new ResultColumn("Amount", "Amount", FieldKind.Measure, DataType.Number)],
            [["North", (object?)(double)Queries]])));
    }

    public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<object?>>([]);
}
