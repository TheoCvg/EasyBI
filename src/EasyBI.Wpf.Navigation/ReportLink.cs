using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Navigation;

/// <summary>
/// A link from the visuals of a report to another report, offered in their menu ("Go to ›"): the target report opens
/// on the data the user points at. Fields and parameters go by their name; <see cref="Fields"/> and
/// <see cref="Parameters"/> map those named otherwise in the target.
/// </summary>
/// <param name="Target">The id of the report to open, which the application's loader knows.</param>
/// <param name="Caption">What the menu shows.</param>
public sealed record ReportLink(string Target, string Caption)
{
    /// <summary>The model whose visuals offer the link; null for any.</summary>
    public string? Model { get; init; }

    /// <summary>True to offer the link only on an element (a bar, a row), not on the visual as a whole.</summary>
    public bool RequiresElement { get; init; }

    /// <summary>The model of the target's filters; null for the visual's.</summary>
    public string? TargetModel { get; init; }

    /// <summary>Fields of the source named otherwise in the target: source name → target name.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    /// <summary>Parameters of the source named otherwise in the target: source name → target name.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    /// <summary>Whether the menu of <paramref name="interaction"/> offers the link.</summary>
    public bool AppliesTo(InteractionContext interaction) =>
        (Model is null || Model == interaction.Visual.Model) && (!RequiresElement || interaction.Element is not null);

    /// <summary>
    /// What the target receives from <paramref name="interaction"/>: the filters selecting the data the user points at
    /// (those of the visual and of its page, then those of the element, the more precise last), on the dimensions the
    /// target's model has (<paramref name="target"/>; all when null), and the values of the parameters.
    /// </summary>
    public NavigationArguments ArgumentsFor(InteractionContext interaction, ModelMetadata? target = null)
    {
        var model = TargetModel ?? interaction.Visual.Model;
        var filters = new List<ReportFilter>();
        foreach (var filter in interaction.DataFilters.Select(f => Map(f, target)).OfType<FilterSpec>())
        {
            // On the same field, the more precise filter (the element's, after the page's) is kept
            filters.RemoveAll(f => f.Filter.Field == filter.Field && filter.Operator != FilterOperator.AnyOf);
            filters.Add(new ReportFilter(model, filter));
        }
        var parameters = interaction.Parameters.ToDictionary(p => Parameters.TryGetValue(p.Key, out var name) ? name : p.Key, p => p.Value);
        return new NavigationArguments(filters, parameters);
    }

    // The filter on the target's field; null when the target has no such dimension (a filter on a measure stays behind)
    private FilterSpec? Map(FilterSpec filter, ModelMetadata? target)
    {
        if (filter.Operator == FilterOperator.AnyOf)
        {
            var groups = new List<IReadOnlyList<FilterSpec>>();
            foreach (var group in filter.Alternatives ?? [])
            {
                var mapped = group.Select(f => Map(f, target)).ToArray();
                if (mapped.Any(f => f is null)) return null;
                groups.Add([.. mapped.OfType<FilterSpec>()]);
            }
            return FilterSpec.AnyOf(groups);
        }
        var field = Fields.TryGetValue(filter.Field, out var renamed) ? renamed : filter.Field;
        if (target is not null && target.Fields.FirstOrDefault(f => f.Name == field) is not { Kind: FieldKind.Dimension }) return null;
        return filter with { Field = field };
    }
}

/// <summary>What a report receives when it is opened from another one: filters and values of parameters to impose.</summary>
public sealed record NavigationArguments(IReadOnlyList<ReportFilter> Filters, IReadOnlyDictionary<string, string?> Parameters)
{
    public static NavigationArguments None { get; } = new([], new Dictionary<string, string?>());
}
