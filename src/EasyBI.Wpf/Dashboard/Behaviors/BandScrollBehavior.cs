using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Scrolls the <see cref="BandMode.Scroll"/> bands: a scroll bar under each band whose items overflow, and the mouse
/// wheel with Shift (or a horizontal wheel) over the band. Works in read-only dashboards too.
/// </summary>
public sealed class BandScrollBehavior : DashboardBehavior
{
    private readonly List<ScrollBar> _bars = [];
    private bool _syncing;

    /// <summary>Pixels scrolled per wheel notch.</summary>
    public double WheelStep { get; set; } = 120;

    protected override void OnAttach()
    {
        Panel.Arranged += OnArranged;
        View.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
    }

    protected override void OnDetach()
    {
        Panel.Arranged -= OnArranged;
        View.RemoveHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
        _bars.Clear();
    }

    private void OnArranged(object? sender, EventArgs e)
    {
        // One bar per band, created as bands appear
        while (_bars.Count < Panel.Bands.Count)
        {
            var index = _bars.Count;
            var bar = new ScrollBar { Orientation = Orientation.Horizontal, Height = 10, Minimum = 0, SmallChange = 40 };
            bar.ValueChanged += (_, args) =>
            {
                if (!_syncing && index < Panel.Bands.Count) Panel.Bands[index].HorizontalOffset = args.NewValue;
            };
            _bars.Add(AddToOverlay(bar, (_, size) => Place(index)));
        }
        _syncing = true;
        try
        {
            for (var i = 0; i < Panel.Bands.Count; i++)
            {
                var band = Panel.Bands[i];
                var bar = _bars[i];
                bar.Maximum = Math.Max(0, band.ExtentWidth - band.ViewportWidth);
                bar.ViewportSize = band.ViewportWidth;
                bar.LargeChange = Math.Max(1, band.ViewportWidth * 0.8);
                bar.Value = band.HorizontalOffset;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private Rect? Place(int index)
    {
        if (index >= Panel.Bands.Count || !Panel.Bands[index].CanScroll) return null;
        var bounds = Panel.GetBandBounds(index);
        return new Rect(bounds.Left, bounds.Bottom + 2, bounds.Width, 10);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || !ModifierKeysState.Shift) return;
        var band = Panel.BandAt(e.GetPosition(Panel).Y);
        if (band >= Panel.Bands.Count || !Panel.Bands[band].CanScroll) return;
        var definition = Panel.Bands[band];
        definition.HorizontalOffset = Compat.Clamp(definition.HorizontalOffset - e.Delta / 120.0 * WheelStep, 0, definition.ExtentWidth - definition.ViewportWidth);
        e.Handled = true;
    }
}
