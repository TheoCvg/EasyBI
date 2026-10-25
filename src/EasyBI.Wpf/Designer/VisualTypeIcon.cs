using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Keys of the icons of the visual types (a <see cref="Geometry"/> each, drawn in a 16×16 box with the foreground). An
/// application gives an icon to its own types, or replaces one of EasyBI's, by defining a geometry with the type's key
/// (<c>x:Key="{bi:VisualTypeIconKey company.treemap}"</c>); a type without one shows <see cref="DefaultKey"/>'s.
/// </summary>
public static class VisualTypeIcons
{
    /// <summary>The icon of the types that have none.</summary>
    public static ComponentResourceKey DefaultKey { get; } = new(typeof(VisualTypeIcons), "(default)");

    public static ComponentResourceKey KeyOf(VisualType type) => new(typeof(VisualTypeIcons), type.Id);
}

/// <summary>The key of a visual type's icon in XAML: <c>{bi:VisualTypeIconKey company.treemap}</c> (see <see cref="VisualTypeIcons"/>).</summary>
[MarkupExtensionReturnType(typeof(ComponentResourceKey))]
public sealed class VisualTypeIconKeyExtension(string type) : MarkupExtension
{
    /// <summary>The id of the visual type.</summary>
    [ConstructorArgument("type")]
    public string Type { get; set; } = type;

    public override object ProvideValue(IServiceProvider serviceProvider) => VisualTypeIcons.KeyOf(VisualType.Parse(Type));
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
