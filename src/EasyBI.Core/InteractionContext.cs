using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// What the end user acts on in a visual (a right click on a bar, a menu of the visual): the visual, the element under
/// the pointer, and everything that restricts its data. An application reads it to act on the data the user sees:
/// open another report on the same filters, show the rows behind a bar, export them...
/// </summary>
/// <param name="Visual">The visual as the report defines it (without the filters of its page).</param>
public sealed record InteractionContext(VisualDefinition Visual)
{
    /// <summary>The element as the renderer reported it; null for the visual as a whole.</summary>
    public DataSelection? Selection { get; init; }

    /// <summary>
    /// The dimensions identifying the element and their values (a bar: its category and its series); null for the visual
    /// as a whole, or an element that carries no dimension (a KPI, a total).
    /// </summary>
    public SelectedElement? Element { get; init; }

    /// <summary>The measure of the element: the one of its series, or the visual's only one; null when it cannot tell.</summary>
    public MeasureRef? Measure { get; init; }

    /// <summary>
    /// Every filter restricting the visual's data: its own, those of its page (report filters, selections made in the
    /// other visuals), with the values of the variables they reference. Not those of <see cref="Element"/>.
    /// </summary>
    public IReadOnlyList<FilterSpec> Filters { get; init; } = [];

    /// <summary>The value of each parameter of the report, in invariant form (see <see cref="ReportVariable.Value"/>).</summary>
    public IReadOnlyDictionary<string, string?> Parameters { get; init; } = new Dictionary<string, string?>();

    /// <summary>The filters selecting the element: equality on each of its dimensions; none for the visual as a whole.</summary>
    public IReadOnlyList<FilterSpec> ElementFilters => Element?.Filters ?? [];

    /// <summary>The filters selecting the data of the element: <see cref="Filters"/>, then <see cref="ElementFilters"/>.</summary>
    public IReadOnlyList<FilterSpec> DataFilters => [.. Filters, .. ElementFilters];

    /// <summary>
    /// The context of <paramref name="selection"/> in a drawn visual: <paramref name="request"/> is what its renderer
    /// drew (its <see cref="RenderRequest.Visual"/> is the visual as queried, page filters and variables applied).
    /// </summary>
    /// <param name="visual">The visual as the report defines it.</param>
    /// <param name="request">What the renderer drew; null when the visual is not drawn (its filters are then those of
    /// <paramref name="visual"/> and <paramref name="filters"/>).</param>
    /// <param name="selection">The element; null for the visual as a whole.</param>
    /// <param name="filters">The filters of the page.</param>
    /// <param name="variables">The variables of the report.</param>
    public static InteractionContext Create(
        VisualDefinition visual,
        RenderRequest? request,
        DataSelection? selection,
        FilterContext? filters = null,
        VariableContext? variables = null) =>
        new(visual)
        {
            Selection = selection,
            Element = request is not null && selection is not null ? SelectionFilters.Element(request, selection) : null,
            Measure = MeasureOf(visual, request, selection),
            Filters = request?.Visual.Filters ?? (filters?.Apply(visual) ?? visual).Filters,
            Parameters = ParametersOf(variables),
        };

    /// <summary>The value of each parameter of <paramref name="variables"/>, in invariant form.</summary>
    public static IReadOnlyDictionary<string, string?> ParametersOf(VariableContext? variables) =>
        (variables?.Variables ?? [])
            .Where(v => VariableFormula.Definition(v).Kind == VariableKind.Parameter)
            .ToDictionary(v => v.Name, VariableContext.ValueOf);

    private static MeasureRef? MeasureOf(VisualDefinition visual, RenderRequest? request, DataSelection? selection)
    {
        if (selection is CategorySelection { Series: { } series } && request is not null && series >= 0 && series < request.Chart.Series.Count
            && request.Chart.Series[series].MeasureKey is { } key)
            return visual.Values.FirstOrDefault(v => v.Key == key);
        return visual.Values.Count == 1 ? visual.Values[0] : null;
    }
}
