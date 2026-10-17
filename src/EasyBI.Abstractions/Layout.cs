namespace EasyBI.Abstractions;

/// <summary>How a dashboard band lays out its items.</summary>
public enum BandMode
{
    /// <summary>A grid of <see cref="DashboardLayout.Columns"/> columns that fits the width; items can span several rows and columns.</summary>
    Fit,

    /// <summary>Items keep their <see cref="LayoutItem.Width"/> side by side; the band scrolls horizontally.</summary>
    Scroll,

    /// <summary>Items keep their <see cref="LayoutItem.Width"/> and wrap onto new lines.</summary>
    Wrap,
}

/// <summary>
/// Arrangement of a dashboard: a stack of bands, each with its own <see cref="BandMode"/>.
/// Heights are counted in grid rows of <see cref="RowHeight"/>.
/// </summary>
public sealed record DashboardLayout
{
    public const int DefaultColumns = 12;
    public const double DefaultRowHeight = 110;

    /// <summary>Number of columns of <see cref="BandMode.Fit"/> bands.</summary>
    public int Columns { get; init; } = DefaultColumns;

    /// <summary>Height of one grid row, in device-independent pixels.</summary>
    public double RowHeight { get; init; } = DefaultRowHeight;

    public IReadOnlyList<LayoutBand> Bands { get; init; } = [];
}

/// <summary>A horizontal band of the dashboard.</summary>
public sealed record LayoutBand
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public BandMode Mode { get; init; } = BandMode.Fit;

    /// <summary>Items of the band; in <see cref="BandMode.Scroll"/> and <see cref="BandMode.Wrap"/> bands, in display order.</summary>
    public IReadOnlyList<LayoutItem> Items { get; init; } = [];
}

/// <summary>
/// An item of a band. It shows <see cref="VisualId"/>, <see cref="Text"/> or <see cref="Image"/>
/// (at most one of them); with none, it is an empty placeholder waiting for content.
/// </summary>
public sealed record LayoutItem
{
    public const double DefaultWidth = 360;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>First column in a <see cref="BandMode.Fit"/> band (0-based).</summary>
    public int Column { get; init; }

    /// <summary>First row in a <see cref="BandMode.Fit"/> band (0-based).</summary>
    public int Row { get; init; }

    /// <summary>Columns covered in a <see cref="BandMode.Fit"/> band.</summary>
    public int ColumnSpan { get; init; } = 6;

    /// <summary>Height, in grid rows (every band mode).</summary>
    public int RowSpan { get; init; } = 3;

    /// <summary>Width in <see cref="BandMode.Scroll"/> and <see cref="BandMode.Wrap"/> bands, in device-independent pixels.</summary>
    public double Width { get; init; } = DefaultWidth;

    /// <summary>The item takes the whole width of its band, alone on its line.</summary>
    public bool IsFullRow { get; init; }

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
