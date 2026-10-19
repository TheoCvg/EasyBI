using System.Windows;
using System.Windows.Controls;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>Raised before the menu of a band opens; add, remove or change its items.</summary>
public sealed class BandMenuOpeningEventArgs(int band, ContextMenu menu) : EventArgs
{
    public int Band { get; } = band;

    public ContextMenu Menu { get; } = menu;
}

/// <summary>
/// "⋮" button in the left margin of the hovered band, opening its menu: mode (fit, scroll, wrap), insert a band below,
/// remove the band. <see cref="MenuOpening"/> lets an application customize the menu.
/// </summary>
public sealed class BandMenuBehavior : DashboardBehavior
{
    private int _band = -1;

    public event EventHandler<BandMenuOpeningEventArgs>? MenuOpening;

    protected override void OnAttach()
    {
        var button = CreateButton(DashboardOverlay.IconButtonStyleKey, "⋮", "BandOptions", () => { });
        button.FontSize = 14;
        button.Click += (_, _) => Open(button);
        AddToOverlay(button, Place);
    }

    private Rect? Place(DashboardOverlay overlay, Size size)
    {
        if (!CanEdit || overlay.IsLocked || overlay.HoveredBand < 0) return null;
        _band = overlay.HoveredBand;
        var bounds = Panel.GetBandBounds(_band);
        // In the panel's left margin
        return new Rect(-size.Width - 4, bounds.Top + 4, size.Width, size.Height);
    }

    private void Open(Button button)
    {
        var band = _band;
        if (band < 0) return;
        var menu = new ContextMenu { PlacementTarget = button };
        foreach (var (mode, label) in new[] { (BandMode.Fit, Strings.BandFit), (BandMode.Scroll, Strings.BandScroll), (BandMode.Wrap, Strings.BandWrap) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = Panel.ModeOf(band) == mode };
            item.Click += (_, _) => View.SetBandMode(band, mode);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var insert = new MenuItem { Header = Strings.InsertBand };
        insert.Click += (_, _) => View.InsertBand(band + 1);
        menu.Items.Add(insert);
        var remove = new MenuItem { Header = Strings.RemoveBand };
        remove.Click += (_, _) => View.RemoveBand(band);
        menu.Items.Add(remove);

        MenuOpening?.Invoke(this, new BandMenuOpeningEventArgs(band, menu));
        menu.IsOpen = true;
    }
}
