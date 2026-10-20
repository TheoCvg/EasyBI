using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Keys of the icons of the visual types (a <see cref="Geometry"/> each, drawn in a 16×16 box with the foreground); an
/// application replaces an icon by defining a geometry with the same key.
/// </summary>
public static class VisualTypeIcons
{
    public static ComponentResourceKey KeyOf(MarkType mark) => new(typeof(VisualTypeIcons), mark.ToString());
}

/// <summary>The icon of a visual type (see <see cref="VisualTypeIcons"/>); lookless, its default style is in <c>Themes/ReportDesigner.xaml</c>.</summary>
public class VisualTypeIcon : Control
{
    public static readonly DependencyProperty MarkProperty = DependencyProperty.Register(
        nameof(Mark), typeof(MarkType), typeof(VisualTypeIcon),
        new PropertyMetadata(MarkType.Bar, (d, e) => ((VisualTypeIcon)d).SetResourceReference(GeometryProperty, VisualTypeIcons.KeyOf((MarkType)e.NewValue))));

    /// <summary>The geometry of the <see cref="Mark"/>'s icon, taken from the resources (it follows a change of theme).</summary>
    public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register(
        nameof(Geometry), typeof(Geometry), typeof(VisualTypeIcon), new PropertyMetadata(null));

    static VisualTypeIcon() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VisualTypeIcon), new FrameworkPropertyMetadata(typeof(VisualTypeIcon)));

    public VisualTypeIcon() => SetResourceReference(GeometryProperty, VisualTypeIcons.KeyOf(Mark));

    public MarkType Mark
    {
        get => (MarkType)GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    public Geometry? Geometry
    {
        get => (Geometry?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }
}
