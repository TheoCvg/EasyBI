using System.Windows;

namespace EasyBI.Wpf;

/// <summary>
/// Declares the <see cref="Slot"/>s of a component, to add content to it, replace or hide some of its parts:
/// <code>
/// &lt;bi:ReportDesigner&gt;
///     &lt;bi:Slots.Items&gt;
///         &lt;bi:Slot Name="ReportDesigner.Header"&gt;&lt;Button Content="Export" /&gt;&lt;/bi:Slot&gt;
///         &lt;bi:Slot Name="ReportDesigner.FiltersShelf" Mode="Hide" /&gt;
///     &lt;/bi:Slots.Items&gt;
/// &lt;/bi:ReportDesigner&gt;
/// </code>
/// or in code: <c>Slots.Add(designer, ReportDesignerSlots.Header, button)</c>.
/// </summary>
/// <remarks>
/// The slots are inherited: declared on a window, they apply to every component in it. The nearest declaration
/// replaces the others, they are not merged.
/// </remarks>
public static class Slots
{
    // Registered under another name, as Interaction.Behaviors does: XAML then goes through GetItems, which creates
    // the collection, instead of reading the property directly and finding it null
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.RegisterAttached(
        "ShadowItems", typeof(SlotCollection), typeof(Slots),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnItemsChanged));

    /// <summary>The slots declared on <paramref name="element"/>, created on first access.</summary>
    public static SlotCollection GetItems(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        // An inherited collection belongs to an ancestor: the element gets its own
        if (element.ReadLocalValue(ItemsProperty) is not SlotCollection items)
            element.SetValue(ItemsProperty, items = []);
        return items;
    }

    public static void SetItems(DependencyObject element, SlotCollection? value) => element.SetValue(ItemsProperty, value);

    /// <summary>Adds <paramref name="content"/> to the slot <paramref name="name"/> of <paramref name="element"/>.</summary>
    public static Slot Add(DependencyObject element, string name, object? content, SlotMode mode = SlotMode.After)
    {
        var slot = new Slot(name, content, mode);
        GetItems(element).Add(slot);
        return slot;
    }

    /// <summary>Shows <paramref name="template"/> in the slot <paramref name="name"/>, applied to its data context.</summary>
    public static Slot AddTemplate(DependencyObject element, string name, DataTemplate template, SlotMode mode = SlotMode.After)
    {
        var slot = new Slot(name, mode: mode) { ContentTemplate = template };
        GetItems(element).Add(slot);
        return slot;
    }

    /// <summary>Hides the default content of the slot <paramref name="name"/>.</summary>
    public static Slot Hide(DependencyObject element, string name)
    {
        var slot = new Slot(name, mode: SlotMode.Hide);
        GetItems(element).Add(slot);
        return slot;
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SlotHost host) host.OnSlotsChanged((SlotCollection?)e.OldValue, (SlotCollection?)e.NewValue);
    }
}
