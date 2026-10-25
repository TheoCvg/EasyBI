using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Renderers;

/// <summary>Built-in renderer of <see cref="VisualType.Image"/> visuals.</summary>
public sealed class ImageRenderer : IVisualRenderer
{
    public string Name => "EasyBI Image";

    public RendererCapabilities Capabilities { get; } = new(new HashSet<VisualType> { VisualType.Image });

    public FrameworkElement Render(RenderRequest request)
    {
        var image = request.Visual.Image;
        return new Image
        {
            Source = Decode(image),
            Stretch = image?.Fit switch
            {
                ImageFit.Cover => Stretch.UniformToFill,
                ImageFit.Stretch => Stretch.Fill,
                _ => Stretch.Uniform,
            },
        };
    }

    /// <summary>Decodes an embedded image; an unreadable image gives null rather than failing.</summary>
    public static ImageSource? Decode(ImageContent? image)
    {
        if (image is null) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(Convert.FromBase64String(image.Data));
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException or ArgumentException)
        {
            return null;
        }
    }
}
