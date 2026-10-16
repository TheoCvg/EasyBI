namespace EasyBI.Abstractions;

/// <summary>
/// Arrangement of a dashboard on a grid: <see cref="Columns"/> columns sharing the width, rows of
/// <see cref="RowHeight"/>. Each item covers a rectangle of cells and shows a visual, a text or an image.
/// </summary>
public sealed record DashboardLayout
{
    public const int DefaultColumns = 12;
    public const double DefaultRowHeight = 110;

    public int Columns { get; init; } = DefaultColumns;

    /// <summary>Height of one grid row, in device-independent pixels.</summary>
    public double RowHeight { get; init; } = DefaultRowHeight;

    public IReadOnlyList<LayoutItem> Items { get; init; } = [];
}

/// <summary>
/// A rectangle of the grid. It shows <see cref="VisualId"/>, <see cref="Text"/> or <see cref="Image"/>
/// (at most one of them); with none, it is an empty placeholder waiting for content.
/// </summary>
public sealed record LayoutItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>First column (0-based).</summary>
    public int Column { get; init; }

    /// <summary>First row (0-based).</summary>
    public int Row { get; init; }

    public int ColumnSpan { get; init; } = 6;

    public int RowSpan { get; init; } = 3;

    /// <summary>Id of the displayed <see cref="VisualDefinition"/>.</summary>
    public string? VisualId { get; init; }

    public TextContent? Text { get; init; }

    public ImageContent? Image { get; init; }
}

public enum TextAlign { Left, Center, Right }

/// <summary>Free text placed on the dashboard (title, comment...).</summary>
public sealed record TextContent
{
    public string Text { get; init; } = "";

    public double FontSize { get; init; } = 16;

    public bool IsBold { get; init; }

    public TextAlign Alignment { get; init; } = TextAlign.Left;
}

public enum ImageFit
{
    /// <summary>Whole image visible, proportions kept.</summary>
    Contain,

    /// <summary>Fills the item, proportions kept, edges cropped.</summary>
    Cover,

    /// <summary>Fills the item, proportions not kept.</summary>
    Stretch,
}

/// <summary>Image embedded in the report, so that the report file stays self-contained.</summary>
public sealed record ImageContent
{
    /// <summary>Image bytes, base64-encoded.</summary>
    public required string Data { get; init; }

    /// <summary>Media type of <see cref="Data"/>, e.g. "image/png".</summary>
    public string MediaType { get; init; } = "image/png";

    public ImageFit Fit { get; init; } = ImageFit.Contain;
}
