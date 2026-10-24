using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>The categories of an axis with several fields, as short labels under groups of the outer fields.</summary>
public class CategoryLevelsTests
{
    private static ChartData Chart(params object?[][] keys) => new(
        "Month / Region",
        [.. keys.Select(k => string.Join(" / ", k))],
        [new ChartSeries("Amount", new double?[keys.Length], null)])
    {
        CategoryValues = keys,
    };

    [Fact]
    public void Each_category_is_labelled_by_its_innermost_field_under_groups_of_the_outer_one()
    {
        var levels = CategoryLevels.Of(Chart(["2024-01", "Est"], ["2024-01", "Nord"], ["2024-01", "Sud"], ["2024-02", "Est"], ["2024-02", "Nord"]))!;

        Assert.Equal(["Est", "Nord", "Sud", "Est", "Nord"], levels.Labels);
        Assert.Equal([new CategoryGroup("2024-01", 0, 3), new CategoryGroup("2024-02", 3, 2)], Assert.Single(levels.Groups));
        Assert.Equal((1d, 3.5), (levels.Groups[0][0].Center, levels.Groups[0][1].Center));
    }

    [Fact]
    public void Groups_of_a_middle_field_end_where_the_outer_groups_end()
    {
        var levels = CategoryLevels.Of(Chart(["2024", "Q1", "Est"], ["2024", "Q1", "Nord"], ["2025", "Q1", "Est"], ["2025", "Q1", "Nord"]))!;

        // Q1 of 2024 and Q1 of 2025 are two groups, next to each other
        Assert.Equal([new CategoryGroup("Q1", 0, 2), new CategoryGroup("Q1", 2, 2)], levels.Groups[0]);
        Assert.Equal([new CategoryGroup("2024", 0, 2), new CategoryGroup("2025", 2, 2)], levels.Groups[1]);
    }

    [Fact]
    public void A_single_field_or_categories_not_ordered_by_the_outer_field_have_no_levels()
    {
        Assert.Null(CategoryLevels.Of(Chart(["Est"], ["Nord"])));
        // Sorted on a measure: the months alternate, groups of one category say nothing
        Assert.Null(CategoryLevels.Of(Chart(["2024-01", "Est"], ["2024-02", "Est"], ["2024-01", "Nord"], ["2024-02", "Nord"])));
    }
}
