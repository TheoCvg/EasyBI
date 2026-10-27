using System.Windows;
using System.Windows.Controls;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Dashboard;

public enum DashboardItemKind { Empty, Visual, Text, Image, Variable }

/// <summary>
/// A cell of a <see cref="DashboardView"/>: shows a <see cref="Visual"/> (chart, text or image) or, without one,
/// offers to add content. Lookless: its default style lives in <c>Themes/Generic.xaml</c> and an application can
/// replace it with an implicit style for <see cref="DashboardItem"/>.
/// </summary>
public class DashboardItem : Control
{
    public static readonly DependencyProperty VisualProperty = DependencyProperty.Register(
        nameof(Visual), typeof(VisualDefinition), typeof(DashboardItem),
        new PropertyMetadata(null, (d, e) => ((DashboardItem)d).Kind = KindOf((VisualDefinition?)e.NewValue)));

    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(DashboardItem), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey KindPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Kind), typeof(DashboardItemKind), typeof(DashboardItem), new PropertyMetadata(DashboardItemKind.Empty));

    public static readonly DependencyProperty KindProperty = KindPropertyKey.DependencyProperty;

    /// <summary>
    /// Whether the item is in (or near) the visible part of its dashboard: the default template only lets its visual
    /// query while it is (see <see cref="DashboardView.LoadsVisibleItemsOnly"/>). True for an item outside a dashboard.
    /// </summary>
    public static readonly DependencyProperty IsInViewportProperty = DependencyProperty.Register(
        nameof(IsInViewport), typeof(bool), typeof(DashboardItem), new PropertyMetadata(true));

    private static readonly DependencyPropertyKey IsSelectedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsSelected), typeof(bool), typeof(DashboardItem), new PropertyMetadata(false));

    /// <summary>True while the item is in <see cref="DashboardView.SelectedItems"/> (for template triggers).</summary>
    public static readonly DependencyProperty IsSelectedProperty = IsSelectedPropertyKey.DependencyProperty;

    static DashboardItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DashboardItem), new FrameworkPropertyMetadata(typeof(DashboardItem)));
    }

    /// <summary>The visual shown by the item; null for an empty item.</summary>
    public VisualDefinition? Visual
    {
        get => (VisualDefinition?)GetValue(VisualProperty);
        set => SetValue(VisualProperty, value);
    }

    public EasyBIContext? Context
    {
        get => (EasyBIContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public DashboardItemKind Kind
    {
        get => (DashboardItemKind)GetValue(KindProperty);
        private set => SetValue(KindPropertyKey, value);
    }

    public bool IsInViewport
    {
        get => (bool)GetValue(IsInViewportProperty);
        set => SetValue(IsInViewportProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        internal set => SetValue(IsSelectedPropertyKey, value);
    }

    public static DashboardItemKind KindOf(VisualDefinition? visual) => visual?.Type switch
    {
        null => DashboardItemKind.Empty,
        var type when type == VisualType.Text => DashboardItemKind.Text,
        var type when type == VisualType.Image => DashboardItemKind.Image,
        var type when type == VisualType.Variable => DashboardItemKind.Variable,
        _ => DashboardItemKind.Visual,
    };
}
