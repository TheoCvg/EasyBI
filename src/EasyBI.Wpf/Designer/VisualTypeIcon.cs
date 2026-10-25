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
    public static ComponentResourceKey KeyOf(VisualType type) => new(typeof(VisualTypeIcons), type.Id);
}

/// <summary>The icon of a visual type (see <see cref="VisualTypeIcons"/>); lookless, its default style is in <c>Themes/ReportDesigner.xaml</c>.</summary>
public class VisualTypeIcon : Control
{
    public static readonly DependencyProperty TypeProperty = DependencyProperty.Register(
        nameof(Type), typeof(VisualType), typeof(VisualTypeIcon),
        new PropertyMetadata(VisualType.Bar, (d, e) => ((VisualTypeIcon)d).SetResourceReference(GeometryProperty, VisualTypeIcons.KeyOf((VisualType)e.NewValue))));

    /// <summary>The geometry of the <see cref="Type"/>'s icon, taken from the resources (it follows a change of theme).</summary>
    public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register(
        nameof(Geometry), typeof(Geometry), typeof(VisualTypeIcon), new PropertyMetadata(null));

    static VisualTypeIcon() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VisualTypeIcon), new FrameworkPropertyMetadata(typeof(VisualTypeIcon)));

    public VisualTypeIcon() => SetResourceReference(GeometryProperty, VisualTypeIcons.KeyOf(Type));

    public VisualType Type
    {
        get => (VisualType)GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }

    public Geometry? Geometry
    {
        get => (Geometry?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }
}
