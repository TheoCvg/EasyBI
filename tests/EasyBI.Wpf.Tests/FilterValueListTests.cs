using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>A filter's list of values to check, as in Excel: (Select all), the search, thousands of values.</summary>
public class FilterValueListTests
{
    private sealed record Purchase(string Supplier, decimal Amount);

    private static QueryableSemanticModel<Purchase> Model(int suppliers = 10) =>
        SemanticModel.For(Enumerable.Range(1, suppliers).Select(i => new Purchase($"Supplier {i:0000}", i)).ToArray(), "purchases")
            .Dimension(p => p.Supplier, "Fournisseur")
            .Measure(p => p.Amount, "Montant");

    private static string Name(int i) => $"Supplier {i:0000}";

    private static FilterItemViewModel Filter(
        Action? changed = null, int suppliers = 10, IEnumerable<string?>? selection = null, FilterOperator @operator = FilterOperator.In)
    {
        var model = Model(suppliers);
        // Created outside xUnit's synchronization context: the values load on the thread pool, not in a busy test slot
        var filter = Task.Run(() => new FilterItemViewModel(model.Metadata.GetField("Supplier"), model, changed ?? (() => { }), selection, @operator)).Result;
        Assert.True(SpinWait.SpinUntil(() => !filter.IsLoading, TimeSpan.FromSeconds(5)));
        return filter;
    }

    [Fact]
    public void Without_a_filter_every_value_is_checked_and_nothing_is_filtered()
    {
        var filter = Filter();

        Assert.All(filter.Values, v => Assert.True(v.IsChecked));
        Assert.True(filter.AllChecked);
        Assert.Null(filter.ToFilterSpec());
        Assert.Equal("(all)", filter.Summary);
    }

    [Fact]
    public void Unchecking_values_leaves_them_out_with_the_shorter_list()
    {
        var filter = Filter();

        filter.Values[0].IsChecked = false;

        Assert.Null(filter.AllChecked);
        Assert.Equal(new FilterSpec("Supplier", FilterOperator.NotIn, [Name(1)]), filter.ToFilterSpec(), FilterSpecComparer.Instance);
        Assert.Equal($"all except {Name(1)}", filter.Summary);

        foreach (var value in filter.Values.Skip(1).Take(7)) value.IsChecked = false;
        Assert.Equal(new FilterSpec("Supplier", FilterOperator.In, [Name(9), Name(10)]), filter.ToFilterSpec(), FilterSpecComparer.Instance);
    }

    [Fact]
    public void Select_all_checks_or_unchecks_every_value_with_one_change_of_the_filter()
    {
        var changes = 0;
        var filter = Filter(() => changes++, suppliers: 1000);

        filter.AllChecked = false;

        Assert.Equal(1, changes);
        Assert.All(filter.Values, v => Assert.False(v.IsChecked));
        Assert.Equal(new FilterSpec("Supplier", FilterOperator.In, []), filter.ToFilterSpec(), FilterSpecComparer.Instance);
        Assert.Equal("(none)", filter.Summary);

        filter.AllChecked = true;
        Assert.Equal(2, changes);
        Assert.Null(filter.ToFilterSpec());
    }

    [Fact]
    public void A_click_on_select_all_partly_checked_checks_every_value_as_in_excel()
    {
        var filter = Filter();
        filter.Values[3].IsChecked = false;

        // WPF unchecks a box partly checked when it is clicked
        filter.AllChecked = false;

        Assert.True(filter.AllChecked);
        Assert.Null(filter.ToFilterSpec());
    }

    [Fact]
    public void While_searching_select_all_filters_on_the_values_found()
    {
        var filter = Filter();
        filter.Search = "supplier 000";

        Assert.True(filter.IsSearching);
        Assert.Equal(9, filter.VisibleValues.Count);
        // Searching alone does not filter
        Assert.Null(filter.ToFilterSpec());

        filter.Values[0].IsChecked = false;

        // As Excel's OK: the values found checked, the others left out
        Assert.Equal(Enumerable.Range(2, 8).Select(Name), filter.Values.Where(v => v.IsChecked).Select(v => v.Label));
        Assert.Equal(new FilterSpec("Supplier", FilterOperator.NotIn, [Name(1), Name(10)]), filter.ToFilterSpec(), FilterSpecComparer.Instance);
    }

    [Fact]
    public void Add_current_selection_to_filter_keeps_the_values_not_found()
    {
        var filter = Filter(selection: [Name(10)]);
        filter.Search = "supplier 0001";
        filter.AddToFilter = true;

        filter.AllChecked = true;

        Assert.Equal([Name(1), Name(10)], filter.ToFilterSpec()!.Values);
    }

    [Fact]
    public void A_filter_all_except_some_values_checks_the_others()
    {
        var filter = Filter(selection: [Name(2)], @operator: FilterOperator.NotIn);

        Assert.Equal([Name(2)], filter.Values.Where(v => !v.IsChecked).Select(v => v.Label));
        Assert.Equal(new FilterSpec("Supplier", FilterOperator.NotIn, [Name(2)]), filter.ToFilterSpec(), FilterSpecComparer.Instance);
    }

    [Fact]
    public async Task Every_value_unchecked_keeps_no_row()
    {
        var model = Model();
        var filters = new FilterContext();

        filters.SetFilter("purchases", new FilterSpec("Supplier", FilterOperator.In, []));

        Assert.Single(filters.FiltersFor(new VisualDefinition { Model = "purchases" }));
        var visual = new VisualDefinition { Model = "purchases", Type = VisualType.Table, Axis = [new("Supplier")], Values = [new("Amount")] };
        var data = await model.GetDataAsync(filters.Apply(visual).ToDataRequest(model.Metadata));
        Assert.Empty(data.Result.Rows);
    }

    [Fact]
    public void A_list_of_thousands_of_values_only_creates_the_boxes_it_shows() => Sta.Run(() =>
    {
        var editor = new FilterEditor { DataContext = Filter(suppliers: 5000) }.Hosted();
        editor.Measure(new Size(400, 700));
        editor.Arrange(new Rect(0, 0, 400, 700));

        Assert.InRange(Descendants(editor).OfType<CheckBox>().Count(), 3, 60);
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class FilterSpecComparer : IEqualityComparer<FilterSpec?>
    {
        public static readonly FilterSpecComparer Instance = new();

        public bool Equals(FilterSpec? x, FilterSpec? y) =>
            x is not null && y is not null && x.Field == y.Field && x.Operator == y.Operator && x.Values.SequenceEqual(y.Values);

        public int GetHashCode(FilterSpec? obj) => 0;
    }
}
