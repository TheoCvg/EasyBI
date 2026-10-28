using System.Windows;
using System.Windows.Controls;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Toolbar at the top-right corner of the hovered item: text formatting (size, bold, alignment) for a text, fit for an
/// image, automatic size (for a resized item), edit and remove. Override <see cref="CreateButtons"/> to change the buttons,
/// or fill the slot <see cref="DashboardSlots.ItemToolbar"/> (its content takes the hovered item as data context).
/// </summary>
public class ItemToolbarBehavior : DashboardBehavior
{
    public const double MinFontSize = 8;
    public const double MaxFontSize = 96;

    private readonly StackPanel _toolbar = new() { Orientation = Orientation.Horizontal };
    private readonly SlotHost _host;
    private DashboardItem? _item;

    public ItemToolbarBehavior() => _host = new SlotHost { Slot = DashboardSlots.ItemToolbar, Content = _toolbar };

    protected override void OnAttach()
    {
        AddToOverlay(_host, Place);
        Overlay.HoverChanged += OnHoverChanged;
        Panel.ArrangementChanged += OnArrangementChanged;
        View.Changed += OnChanged;
    }

    protected override void OnDetach()
    {
        Overlay.HoverChanged -= OnHoverChanged;
        Panel.ArrangementChanged -= OnArrangementChanged;
        View.Changed -= OnChanged;
    }

    /// <summary>The buttons shown for an item.</summary>
    protected virtual IEnumerable<FrameworkElement> CreateButtons(DashboardItem item)
    {
        // Buttons read the item's content when clicked: it may have changed since they were created
        if (item.Kind == DashboardItemKind.Text)
        {
            yield return Button("A−", "TextSmaller", () => ChangeText(item, t => t with { FontSize = Math.Max(MinFontSize, t.FontSize - 2) }));
            yield return Button("A+", "TextLarger", () => ChangeText(item, t => t with { FontSize = Math.Min(MaxFontSize, t.FontSize + 2) }));
            var bold = Button("B", "TextBold", () => ChangeText(item, t => t with { IsBold = !t.IsBold }));
            bold.FontWeight = FontWeights.Bold;
            yield return bold;
            yield return Button("≡", "TextAlignment", () => ChangeText(item, t => t with { Alignment = (TextAlign)(((int)t.Alignment + 1) % 3) }));
        }
        if (item.Kind == DashboardItemKind.Image)
        {
            yield return Button("⤢", "ImageFit", () =>
            {
                if (item.Visual is { Image: { } image } visual)
                    View.SetVisual(item, visual with { Image = image with { Fit = (ImageFit)(((int)image.Fit + 1) % 3) } });
            });
        }

        // A constrained item can become greedy again
        if (DashboardLayout.IsConstrained(item)) yield return Button("↔", "AutoSize", () => View.SetAutoSize(item));

        var editTip = item.Kind switch
        {
            DashboardItemKind.Text => "EditText",
            DashboardItemKind.Image => "ChangeImage",
            DashboardItemKind.Variable => "EditVariable",
            DashboardItemKind.Visual => "EditVisual",
            _ => null,
        };
        if (editTip is not null) yield return Button("✎", editTip, () => View.EditItem(item));

        var remove = Button("✕", "RemoveItem", () => View.Remove(item));
        remove.FontSize = 10;
        yield return remove;
    }

    /// <summary>A toolbar button with the overridable icon button style.</summary>
    protected Button Button(object content, string toolTipKey, Action click) =>
        CreateButton(DashboardOverlay.IconButtonStyleKey, content, toolTipKey, click);

    private void ChangeText(DashboardItem item, Func<TextContent, TextContent> change)
    {
        if (item.Visual is { } visual && visual.Type == VisualType.Text) View.SetVisual(item, visual with { Text = change(visual.Text ?? new TextContent()) });
    }

    private Rect? Place(DashboardOverlay overlay, Size size)
    {
        if (!CanEdit || overlay.IsLocked || overlay.HoveredItem is null) return null;
        var bounds = overlay.HoveredBounds;
        return new Rect(bounds.Right - size.Width - 6, bounds.Top + 1, size.Width, size.Height);
    }

    private void OnHoverChanged(object? sender, EventArgs e)
    {
        if (Overlay.HoveredItem != _item) Rebuild();
    }

    // Size constraints and content (an empty item that became a text...) change the buttons
    private void OnArrangementChanged(object sender, RoutedEventArgs e) => Rebuild();

    private void OnChanged(object? sender, DashboardChangedEventArgs e) => Rebuild();

    /// <summary>Recreates the buttons for the hovered item (after its content changed).</summary>
    protected void Rebuild()
    {
        _item = Overlay.HoveredItem;
        _host.DataContext = _item;
        _toolbar.Children.Clear();
        if (_item is null) return;
        foreach (var button in CreateButtons(_item)) _toolbar.Children.Add(button);
    }
}
