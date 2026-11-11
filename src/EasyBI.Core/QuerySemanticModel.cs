using System.Globalization;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// A data source that runs <see cref="AnalyticQuery"/>s: EasyBI composes a visual's data from them (the comparisons
/// with a year earlier, the year-to-date totals, the levels of a matrix). Derive from it to plug a source that answers
/// queries (SQL, a REST API...); <see cref="QueryableSemanticModel{T}"/> runs them over an <c>IQueryable</c>.
/// </summary>
public abstract class QuerySemanticModel : ISemanticModel
{
    public abstract ModelMetadata Metadata { get; }

    /// <summary>
    /// The periods, presentations and accumulations the measures of a visual are computed with (<see cref="MeasureTime"/>):
    /// <see cref="TimeCatalog.Default"/> unless the application gives the model its own.
    /// </summary>
    public TimeCatalog Time { get; set; } = TimeCatalog.Default;

    /// <summary>Runs a query: its filters, groups, aggregates, sort and Top, and the calculated measures it defines.</summary>
    public abstract Task<ResultSet> ExecuteAsync(AnalyticQuery query, CancellationToken cancellationToken = default);

    public abstract Task<DistinctValues> GetDistinctValuesAsync(DistinctValuesRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the queries of the visual, in the request's cultures and on its date of today, with the periods and
    /// presentations of comparisons of the report it brings.
    /// </summary>
    public virtual async Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
    {
        using var cultures = EasyBILocalization.Use(
            request.UICulture is { } ui ? CultureInfo.GetCultureInfo(ui) : EasyBILocalization.UICulture,
            request.FormatCulture is { } format ? CultureInfo.GetCultureInfo(format) : EasyBILocalization.FormatCulture);
        var (visual, today) = (request.Visual, DateOnly.FromDateTime(request.Today));
        var time = Time.With(request.ComparisonPeriods, request.ComparisonOutputs);
        if (visual.Type == VisualType.Matrix)
        {
            var (cells, matrix) = await MatrixBuilder.ExecuteAsync(this, visual, request.Measures, today, time, cancellationToken).ConfigureAwait(false);
            return new VisualData(cells) { Matrix = matrix };
        }
        var query = visual.ToQuery(request.Measures, Metadata, request.HasTooltips);
        return new VisualData(await this.ExecuteWithTimeAsync(query, today, time, cancellationToken).ConfigureAwait(false));
    }
}
