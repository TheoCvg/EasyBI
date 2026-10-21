using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Core.Tests;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>Number dimensions are filtered on values or compared, measures are compared, in the designer and the filter bar.</summary>
[Collection(CultureCollection.Name)]
public class NumericFilterUiTests
{
    private sealed record Sale(string Region, int Quantity, decimal Amount);

    // Counts the queries for distinct values: a measure has none
    private sealed class CountingModel(ISemanticModel inner) : ISemanticModel
    {
        public int DistinctQueries { get; private set; }

        public ModelMetadata Metadata => inner.Metadata;

        public Task<ResultSet> ExecuteAsync(AnalyticQuery query, CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(query, cancellationToken);

        public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default)
        {
            DistinctQueries++;
            return inner.GetDistinctValuesAsync(field, cancellationToken);
        }
    }

    private static CountingModel Model() => new(
        SemanticModel.For(new[] { new Sale("Nord", 1, 10m), new Sale("Sud", 3, 20m) }, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Quantity, "Quantité")
            .Measure(s => s.Amount, "CA", format: "N0"));

    private static FilterItemViewModel Filter(ISemanticModel model, string field, FilterSpec? loaded = null, Action? changed = null) =>
        // Created on the thread pool: its values load asynchronously, which xUnit's context could starve
        Task.Run(() => new FilterItemViewModel(model.Metadata.GetField(field), model, changed ?? (() => { }),
            loaded?.Values, loaded?.Operator ?? FilterOperator.In)).Result;

    private static void AssertSame(FilterSpec expected, FilterSpec? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal((expected.Field, expected.Operator), (actual.Field, actual.Operator));
        Assert.Equal(expected.Values, actual.Values);
    }

    [Fact]
    public void A_number_dimension_is_filtered_on_its_values_or_compared()
    {
        var changes = 0;
        var filter = Filter(Model(), "Quantity", changed: () => changes++);
        Assert.True(filter.HasConditions && filter.IsValueList && !filter.IsComparison);

        filter.Condition = filter.Conditions.Single(c => c.Value == FilterOperator.Range);
        Assert.True(filter.IsComparison && filter.ShowSecondNumber && !filter.IsValueList);
        Assert.Null(filter.ToFilterSpec()); // no number yet: no filter
        filter.Number = "1.5";
        filter.SecondNumber = "1,000";

        AssertSame(NumericFilters.Range("Quantity", 1.5, 1000), filter.ToFilterSpec());
        Assert.Equal("between 1.5 and 1,000", filter.Summary);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void An_unreadable_number_leaves_its_side_open()
    {
        var filter = Filter(Model(), "Quantity");
        filter.Condition = filter.Conditions.Single(c => c.Value == FilterOperator.GreaterThan);

        filter.Number = "abc";
        Assert.Null(filter.ToFilterSpec());

        filter.Number = "2";
        AssertSame(NumericFilters.Compare("Quantity", FilterOperator.GreaterThan, 2), filter.ToFilterSpec());
    }

    [Fact]
    public void A_measure_is_compared_and_loads_no_values()
    {
        var model = Model();
        var filter = Filter(model, "Amount");

        Assert.True(filter.IsMeasure && !filter.IsValueList);
        Assert.Null(filter.Condition.Value); // any value
        Assert.Null(filter.ToFilterSpec());
        Assert.Equal(0, model.DistinctQueries);

        filter.Condition = filter.Conditions.Single(c => c.Value == FilterOperator.GreaterThanOrEqual);
        filter.Number = "15";

        Assert.Equal("≥ 15", filter.Summary);
        AssertSame(NumericFilters.Compare("Amount", FilterOperator.GreaterThanOrEqual, 15), filter.ToFilterSpec());
    }

    [Fact]
    public void A_text_dimension_offers_no_condition()
    {
        Assert.False(Filter(Model(), "Region").HasConditions);
    }

    [Fact]
    public void A_saved_comparison_is_shown_in_the_users_language()
    {
        using var french = TestCulture.Use("fr-FR");

        var filter = Filter(Model(), "Quantity", NumericFilters.Range("Quantity", 2.5, null));

        Assert.Equal((FilterOperator?)FilterOperator.Range, filter.Condition.Value);
        Assert.Equal(("2,5", ""), (filter.Number, filter.SecondNumber));
        Assert.Equal("≥ 2,5", filter.Summary);
    }

    [Fact]
    public void The_designer_takes_a_measure_as_filter_and_keeps_its_comparison()
    {
        var model = Model();
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(model));
        var comparison = NumericFilters.Compare("Amount", FilterOperator.LessThan, 15);
        designer.Load(new VisualDefinition { Model = "sales", Axis = [new("Region")], Values = [new("Amount")], Filters = [comparison] });

        Assert.Equal("< 15", Assert.Single(designer.Filters).Summary);
        AssertSame(comparison, Assert.Single(designer.Visual!.Filters));
        Assert.False(designer.CanDrop(model.Metadata.GetField("Amount"), Shelf.Filters)); // already there

        designer.RemoveCommand.Execute(designer.Filters.Single());
        Assert.True(designer.CanDrop(model.Metadata.GetField("Amount"), Shelf.Filters));
    }
}
