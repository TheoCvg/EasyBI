using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>The values a filter lists: at most a limit, telling when there are more, and those containing a search.</summary>
public class DistinctValuesTests
{
    private sealed record Purchase(string? Supplier, int Year);

    private static readonly Purchase[] Purchases =
    [
        new("Dupont", 2023), new("Durand", 2023), new("dupuis", 2024), new("Martin", 2024), new("Dupont", 2025), new(null, 2025),
    ];

    private static QueryableSemanticModel<Purchase> Model() =>
        SemanticModel.For(Purchases, "purchases").Dimension(p => p.Supplier, "Fournisseur").Dimension(p => p.Year, "Année");

    [Fact]
    public async Task Values_beyond_the_limit_are_left_out_and_said_to_be()
    {
        var model = Model();

        var first = await model.GetDistinctValuesAsync(new DistinctValuesRequest("Supplier") { Limit = 3 });
        Assert.Equal([null, "Dupont", "dupuis"], first.Values);
        Assert.True(first.IsTruncated);

        var all = await model.GetDistinctValuesAsync(new DistinctValuesRequest("Supplier") { Limit = 5 });
        Assert.Equal(5, all.Values.Count);
        Assert.False(all.IsTruncated);
    }

    [Fact]
    public async Task A_search_keeps_the_texts_containing_it_ignoring_case()
    {
        var found = await Model().GetDistinctValuesAsync(new DistinctValuesRequest("Supplier") { Search = " DUP " });

        Assert.Equal(["Dupont", "dupuis"], found.Values);
        Assert.False(found.IsTruncated);
    }

    [Fact]
    public async Task A_search_does_not_apply_to_values_other_than_texts()
    {
        var found = await Model().GetDistinctValuesAsync(new DistinctValuesRequest("Year") { Search = "2024" });

        Assert.Equal([2023, 2024, 2025], found.Values);
    }

    [Fact]
    public void The_request_and_the_values_travel_as_json()
    {
        var request = DataSerializer.DeserializeValuesRequest(DataSerializer.Serialize(new DistinctValuesRequest("Supplier") { Search = "dup", Limit = 50 }));
        Assert.Equal(new DistinctValuesRequest("Supplier") { Search = "dup", Limit = 50 }, request);
        Assert.Equal(DistinctValuesRequest.DefaultLimit, DataSerializer.DeserializeValuesRequest("""{"field":"Supplier"}""").Limit);

        var values = DataSerializer.DeserializeValues(DataSerializer.Serialize(new DistinctValues(["Dupont", null], IsTruncated: true)), DataType.Text);
        Assert.Equal(["Dupont", null], values.Values);
        Assert.True(values.IsTruncated);
    }
}
