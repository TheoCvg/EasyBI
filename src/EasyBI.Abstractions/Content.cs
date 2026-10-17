namespace EasyBI.Abstractions;

public enum TextAlign { Left, Center, Right }

/// <summary>Free text shown by a <see cref="MarkType.Text"/> visual (title, comment...).</summary>
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

    /// <summary>Fills the visual, proportions kept, edges cropped.</summary>
    Cover,

    /// <summary>Fills the visual, proportions not kept.</summary>
    Stretch,
}

/// <summary>Image shown by a <see cref="MarkType.Image"/> visual, embedded so that the report file stays self-contained.</summary>
public sealed record ImageContent
{
    /// <summary>Image bytes, base64-encoded.</summary>
    public required string Data { get; init; }

    /// <summary>Media type of <see cref="Data"/>, e.g. "image/png".</summary>
    public string MediaType { get; init; } = "image/png";

    public ImageFit Fit { get; init; } = ImageFit.Contain;
}
