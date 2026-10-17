using System.Windows;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// "+" buttons: on the right edge of the hovered item (and on its bottom edge in a grid band) to add an item next to it,
/// and in the space below a band to insert a band. Each appears when the pointer comes near its edge.
/// </summary>
public sealed class AddButtonsBehavior : DashboardBehavior
{
    /// <summary>Distance from an edge within which its "+" appears.</summary>
    public double EdgeZone { get; set; } = 18;

    /// <summary>False hides the "+" between bands.</summary>
    public bool CanInsertBands { get; set; } = true;

    // The item or band each button was last shown for: the pointer may already be over a neighbour when it clicks
    private DashboardItem? _rightOf;
    private DashboardItem? _below;
    private int _bandAfter = -1;

    protected override void OnAttach()
    {
        AddToOverlay(CreateButton(DashboardOverlay.PlusButtonStyleKey, null, "AddRight", () => Add(ItemSide.Right)), PlaceRight);
        AddToOverlay(CreateButton(DashboardOverlay.PlusButtonStyleKey, null, "AddBelow", () => Add(ItemSide.Below)), PlaceBelow);
        AddToOverlay(CreateButton(DashboardOverlay.PlusButtonStyleKey, null, "AddBand", InsertBand), PlaceBand);
    }

    private void Add(ItemSide side)
    {
        if ((side == ItemSide.Right ? _rightOf : _below) is { } item && item.Parent == Panel) View.AddNextTo(item, side);
    }

    private void InsertBand()
    {
        if (_bandAfter >= 0) View.InsertBand(_bandAfter + 1);
    }

    private Rect? PlaceRight(DashboardOverlay overlay, Size size)
    {
        if (!CanEdit || overlay.IsLocked || overlay.HoveredItem is null || overlay.Pointer is not { } p) return null;
        var bounds = overlay.HoveredBounds;
        var nearEdge = p.X >= bounds.Right - EdgeZone && p.X <= bounds.Right + Panel.Gap
            && p.Y >= bounds.Top + 24 && p.Y <= bounds.Bottom - 20;
        if (!nearEdge) return null;
        _rightOf = overlay.HoveredItem;
        // Centred in the gap after the item
        return Centered(new Point(bounds.Right + Panel.Gap / 2, bounds.Top + bounds.Height / 2), size);
    }

    private Rect? PlaceBelow(DashboardOverlay overlay, Size size)
    {
        if (!CanEdit || overlay.IsLocked || overlay.HoveredItem is not { } item || overlay.Pointer is not { } p) return null;
        // Items follow each other in scrolling and wrapping bands: only "+" on the right there
        if (Panel.ModeOf(Panel.BandIndexOf(item)) != BandMode.Fit) return null;
        var bounds = overlay.HoveredBounds;
        var nearEdge = p.Y >= bounds.Bottom - EdgeZone && p.Y <= bounds.Bottom + Panel.Gap / 2
            && p.X >= bounds.Left + 24 && p.X <= bounds.Right - 24;
        if (!nearEdge) return null;
        _below = item;
        return Centered(new Point(bounds.Left + bounds.Width / 2, bounds.Bottom + Panel.Gap / 2), size);
    }

    private Rect? PlaceBand(DashboardOverlay overlay, Size size)
    {
        if (!CanEdit || !CanInsertBands || overlay.IsLocked || overlay.HoveredBand < 0 || overlay.Pointer is not { } p) return null;
        var bounds = Panel.GetBandBounds(overlay.HoveredBand);
        var last = overlay.HoveredBand == Panel.BandCount - 1;
        var gap = last ? Panel.Margin.Bottom : Panel.BandSpacing;
        // Right under the band, the "+" of the item's bottom edge has priority
        if (p.Y <= bounds.Bottom + Panel.Gap / 2 || p.Y > bounds.Bottom + gap) return null;
        _bandAfter = overlay.HoveredBand;
        return Centered(new Point(bounds.Left + bounds.Width / 2, bounds.Bottom + gap / 2), size);
    }
}
