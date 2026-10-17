using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Wpf.DragAndDrop;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Makes visuals from fields dragged onto the dashboard (a <see cref="ModelField"/>, as a <see cref="FieldList"/>
/// drags them). Dropped where an item could go (an item's edge, between two lines, a free space, a scrolling or
/// wrapping band), the field makes a new visual (<see cref="CreateVisual"/>), the other items making room live around a
/// placeholder; dropped on an empty item, it fills it; dropped on a visual, it is added to it (<see cref="AddField"/>).
/// Everything a drop changes is one undo step; leaving the dashboard or Escape cancels it.
/// </summary>
public sealed class FieldDropBehavior : DashboardBehavior
{
    private readonly UIElement _pending = new Border();
    private DashboardEdit? _edit;
    private DashboardLayoutSnapshot? _snapshot;
    private DashboardPanelGeometry? _before;
    private DashboardDropTarget? _target;
    private DashboardItem? _onItem;
    private AutoScroller? _scroller;
    private ScrollViewer? _viewer;

    /// <summary>Distance from an item's edge within which the field goes next to it rather than into it.</summary>
    public double EdgeZone { get; set; } = 40;

    /// <summary>The visual a field dropped on a free place makes; null refuses the drop. <see cref="FieldVisuals.Create"/> by default.</summary>
    public Func<ModelField, VisualDefinition?> CreateVisual { get; set; } = field => FieldVisuals.Create(field.Model, field.Field);

    /// <summary>A visual with a field dropped on it; null refuses the drop. <see cref="FieldVisuals.AddField"/> by default.</summary>
    public Func<VisualDefinition, ModelField, VisualDefinition?> AddField { get; set; } = (visual, field) => FieldVisuals.AddField(visual, field.Model, field.Field);

    protected override void OnAttach()
    {
        var placeholder = new ContentControl { Focusable = false, IsHitTestVisible = false };
        placeholder.SetResourceReference(FrameworkElement.StyleProperty, DashboardOverlay.DropPlaceholderStyleKey);
        AddToOverlay(placeholder, (_, _) => PlaceholderBounds());
        View.AllowDrop = true;
        View.DragEnter += OnDragOver;
        View.DragOver += OnDragOver;
        View.DragLeave += OnDragLeave;
        View.Drop += OnDrop;
        // A drag cancelled with Escape or released elsewhere ends the preview
        DragSource.DragEnded += OnDragEnded;
    }

    protected override void OnDetach()
    {
        End(commit: false);
        DragSource.DragEnded -= OnDragEnded;
        View.DragEnter -= OnDragOver;
        View.DragOver -= OnDragOver;
        View.DragLeave -= OnDragLeave;
        View.Drop -= OnDrop;
    }

    private Rect? PlaceholderBounds() =>
        _onItem is { } item ? Panel.GetItemBounds(item)
        : _target is not null ? Panel.GetItemBounds(_pending)
        : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Handled || !CanEdit || DragSource.ItemOf(e.Data) is not ModelField field) return;
        e.Handled = true;
        if (_edit is null)
        {
            _edit = View.BeginEdit();
            _snapshot = DashboardLayout.Capture(Panel);
            _before = Panel.CaptureGeometry();
        }
        var position = e.GetPosition(Panel);
        e.Effects = Aim(field, position) ? DragDropEffects.Copy : DragDropEffects.None;
        Scroll(position);
        Overlay.Refresh();
    }

    /// <summary>Finds and previews where the field goes; false when it cannot be dropped here.</summary>
    private bool Aim(ModelField field, Point position)
    {
        // The place made for the new visual holds while the pointer is over it, since the items around it moved
        if (_target is { Kind: not DashboardDropKind.Flow } && Widened(Panel.GetItemBounds(_pending)).Contains(position)) return true;

        if (DashboardDropTarget.Find(_before!, Panel, position, EdgeZone) is { } target)
        {
            if (CreateVisual(field) is null) return Clear();
            if (target != _target)
            {
                Restore();
                target.Add(Panel, _pending);
                _target = target;
                _onItem = null;
            }
            return true;
        }

        // In the middle of an item: an empty one is filled, a visual gets the field
        var item = _before!.ChildrenOf(_before.BandAt(position.Y)).FirstOrDefault(c => c.Bounds.Contains(position)).Element as DashboardItem;
        var accepted = item switch
        {
            { Kind: DashboardItemKind.Empty } => CreateVisual(field) is not null,
            { Visual: { } visual } => AddField(visual, field) is not null,
            _ => false,
        };
        if (!accepted) return Clear();
        if (item != _onItem)
        {
            Restore();
            _target = null;
            _onItem = item;
        }
        return true;
    }

    private bool Clear()
    {
        if (_target is not null) Restore();
        _target = null;
        _onItem = null;
        return false;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        // Leaving a child of the view (the preview changes what is under the pointer) also raises DragLeave
        var position = e.GetPosition(View);
        if (!new Rect(View.RenderSize).Contains(position)) End(commit: false);
    }

    private void OnDragEnded(object? sender, EventArgs e) => End(commit: false);

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Handled || _edit is null || DragSource.ItemOf(e.Data) is not ModelField field) return;
        e.Handled = true;
        var target = _target;
        var item = _onItem;
        Restore();
        if (target is not null && CreateVisual(field) is { } visual)
        {
            View.SetVisual(View.AddAt(target), visual);
            e.Effects = DragDropEffects.Copy;
        }
        else if (item is not null && (item.Kind == DashboardItemKind.Empty ? CreateVisual(field) : AddField(item.Visual!, field)) is { } changed)
        {
            View.SetVisual(item, changed);
            e.Effects = DragDropEffects.Copy;
        }
        End(commit: true);
    }

    /// <summary>Puts the layout from before the drag back, without the pending item.</summary>
    private void Restore()
    {
        _snapshot?.Restore();
        Panel.Children.Remove(_pending);
    }

    private void End(bool commit)
    {
        _scroller?.Stop();
        if (_edit is null) return;
        var edit = _edit;
        _edit = null;
        // A drop already put the layout back before adding the visual
        if (!commit) Restore();
        _snapshot = null;
        _before = null;
        _target = null;
        _onItem = null;
        if (commit) edit.Commit();
        else edit.Cancel();
        Overlay.Refresh();
    }

    private void Scroll(Point position)
    {
        _viewer ??= Ancestor<ScrollViewer>(Panel);
        if (_viewer is not { } viewer) return;
        _scroller ??= AutoScroller.For(viewer);
        var viewport = AutoScroller.ViewportOf(viewer, Panel);
        _scroller.Update(viewport, position, horizontal: false);
    }

    private Rect Widened(Rect bounds)
    {
        if (!bounds.IsEmpty) bounds.Inflate(Panel.Gap / 2, Panel.Gap / 2);
        return bounds;
    }

    private static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var e = VisualTreeHelper.GetParent(element); e is not null; e = VisualTreeHelper.GetParent(e))
            if (e is T found) return found;
        return null;
    }
}
