using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

public class DesignerSearchTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount, decimal Margin);

    private static readonly string[] Regions = ["Est", "Île-de-France", "Nord", "Nord-Est", "Ouest", "Sud", "Sud-Est", "Sud-Ouest", "Centre", "Corse"];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Regions.Select(r => new Sale(r, "Web", 1m, 1m)).ToArray(), "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Measure(s => s.Amount, "Chiffre d'affaires")
            .Measure(s => s.Margin, "Marge");

    private static FilterItemViewModel Filter(string field, IEnumerable<string?>? checkedValues = null)
    {
        var model = Model();
        // Created outside xUnit's synchronization context: the values load on the thread pool, not in a busy test slot
        var filter = Task.Run(() => new FilterItemViewModel(model.Metadata.GetField(field), model, () => { }, checkedValues)).Result;
        Assert.True(SpinWait.SpinUntil(() => !filter.IsLoading, TimeSpan.FromSeconds(5)));
        return filter;
    }

    [Fact]
    public void The_field_list_shows_the_fields_matching_the_search() => Sta.Run(() =>
    {
        var fields = new FieldList { Context = EasyBIContext.Create().AddModel(Model()) };

        fields.Search = "chif";

        Assert.Empty(fields.Dimensions);
        Assert.Equal(["Chiffre d'affaires"], fields.Measures.Select(f => f.Caption));

        fields.Search = "";
        Assert.Equal(2, fields.Dimensions.Count);
        Assert.Equal(2, fields.Measures.Count);
    });

    [Fact]
    public void A_filter_shows_the_values_matching_the_search_and_keeps_the_others_checked()
    {
        var filter = Filter("Region", ["Nord", "Sud"]);

        filter.Search = "ouest";

        Assert.Equal(["Ouest", "Sud-Ouest"], filter.VisibleValues.Select(v => v.Label));
        Assert.Equal(["Nord", "Sud"], filter.ToFilterSpec()!.Values);

        filter.Search = "";
        Assert.Equal(Regions.Length, filter.VisibleValues.Count);
    }

    [Fact]
    public void Only_long_lists_of_values_offer_a_search()
    {
        Assert.True(Filter("Region").ShowSearch);
        Assert.False(Filter("Channel").ShowSearch);
    }
}
