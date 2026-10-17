using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Xaml.Behaviors;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Base of the dashboard behaviors: gives access to the view, its panel and the shared <see cref="DashboardOverlay"/>,
/// and removes the overlay elements it added when it is detached. Derive from it to add an interaction of your own.
/// </summary>
public abstract class DashboardBehavior : Behavior<DashboardView>
{
    private readonly List<FrameworkElement> _added = [];
    private DashboardOverlay? _overlay;

    protected DashboardView View => AssociatedObject;

    protected DashboardPanel Panel => AssociatedObject.Panel;

    protected DashboardOverlay Overlay => _overlay ?? throw new InvalidOperationException("The behavior is not attached.");

    /// <summary>False when the dashboard is read-only: editing chrome stays hidden.</summary>
    protected bool CanEdit => !AssociatedObject.IsReadOnly;

    protected sealed override void OnAttached()
    {
        base.OnAttached();
        _overlay = DashboardOverlay.For(AssociatedObject);
        OnAttach();
    }

    protected sealed override void OnDetaching()
    {
        OnDetach();
        foreach (var element in _added) Overlay.Remove(element);
        _added.Clear();
        _overlay = null;
        base.OnDetaching();
    }

    /// <summary>Hooks the behavior up (the view, its panel and the overlay are available).</summary>
    protected abstract void OnAttach();

    /// <summary>Unhooks what <see cref="OnAttach"/> hooked; overlay elements added through <see cref="AddToOverlay"/> are removed automatically.</summary>
    protected virtual void OnDetach()
    {
    }

    protected T AddToOverlay<T>(T element, OverlayPlacement placement) where T : FrameworkElement
    {
        Overlay.Add(element, placement);
        _added.Add(element);
        return element;
    }

    protected void RemoveFromOverlay(FrameworkElement element)
    {
        Overlay.Remove(element);
        _added.Remove(element);
    }

    /// <summary>A button with the overridable <paramref name="styleKey"/> style and a tooltip from EasyBI's texts.</summary>
    protected static Button CreateButton(ComponentResourceKey styleKey, object? content, string? toolTipKey, Action click)
    {
        var button = new Button { Content = content };
        button.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        if (toolTipKey is not null) Localize(button, FrameworkElement.ToolTipProperty, toolTipKey);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Binds a property to one of EasyBI's texts, so that it follows language changes.</summary>
    protected static void Localize(FrameworkElement element, DependencyProperty property, string key) =>
        element.SetBinding(property, new Binding($"[{key}]") { Source = LocalizedStrings.EasyBI, Mode = BindingMode.OneWay });

    /// <summary>A rectangle of <paramref name="size"/> centred on <paramref name="center"/>.</summary>
    protected static Rect Centered(Point center, Size size) => new(center.X - size.Width / 2, center.Y - size.Height / 2, size.Width, size.Height);
}
