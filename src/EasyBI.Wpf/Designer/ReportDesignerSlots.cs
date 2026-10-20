namespace EasyBI.Wpf.Designer;

/// <summary>
/// Names of the <see cref="Slot"/>s of a <see cref="ReportDesigner"/>. Their content takes the
/// <see cref="DesignerViewModel"/> as data context.
/// </summary>
public static class ReportDesignerSlots
{
    /// <summary>The left panel, with the field list.</summary>
    public const string FieldPanel = "ReportDesigner.FieldPanel";

    /// <summary>The field list (vertical: before is above it, after below it).</summary>
    public const string Fields = "ReportDesigner.Fields";

    /// <summary>The row with the title and the visual types, above the shelves.</summary>
    public const string Header = "ReportDesigner.Header";

    /// <summary>The title of the visual.</summary>
    public const string Title = "ReportDesigner.Title";

    /// <summary>The visual types (bar, line, table...).</summary>
    public const string VisualTypes = "ReportDesigner.VisualTypes";

    /// <summary>All the shelves (vertical).</summary>
    public const string Shelves = "ReportDesigner.Shelves";

    /// <summary>The Axis shelf, with its label.</summary>
    public const string AxisShelf = "ReportDesigner.AxisShelf";

    /// <summary>The Legend shelf, with its label.</summary>
    public const string LegendShelf = "ReportDesigner.LegendShelf";

    /// <summary>The Values shelf, with its label.</summary>
    public const string ValuesShelf = "ReportDesigner.ValuesShelf";

    /// <summary>The Filters shelf, with its label.</summary>
    public const string FiltersShelf = "ReportDesigner.FiltersShelf";

    /// <summary>The active sort (chosen on the chips of the Axis and Values shelves) and the Top N box, under the shelves.</summary>
    public const string Sort = "ReportDesigner.Sort";

    /// <summary>
    /// The options of the renderer drawing the visual (<see cref="Abstractions.RendererCapabilities.Options"/>), under the
    /// shelves; hidden when it has none for the visual.
    /// </summary>
    public const string RendererOptions = "ReportDesigner.RendererOptions";

    /// <summary>The preview of the visual (vertical).</summary>
    public const string Preview = "ReportDesigner.Preview";
}
