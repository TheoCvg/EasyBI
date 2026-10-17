using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// A band of a <see cref="DashboardPanel"/>, declared like a <c>RowDefinition</c> of a <c>Grid</c>:
/// children choose their band with the attached <see cref="DashboardPanel.BandProperty"/>.
/// </summary>
public sealed class DashboardBand : INotifyPropertyChanged
{
    private BandMode _mode;
    private double _horizontalOffset;
    private double _extentWidth;
    private double _viewportWidth;

    public event PropertyChangedEventHandler? PropertyChanged;

    public BandMode Mode
    {
        get => _mode;
        set => Set(ref _mode, value);
    }

    /// <summary>Horizontal scroll position of a <see cref="BandMode.Scroll"/> band (clamped by the panel).</summary>
    public double HorizontalOffset
    {
        get => _horizontalOffset;
        set => Set(ref _horizontalOffset, value);
    }

    /// <summary>Total width of the items of a <see cref="BandMode.Scroll"/> band, set by the panel.</summary>
    public double ExtentWidth
    {
        get => _extentWidth;
        internal set => Set(ref _extentWidth, value);
    }

    /// <summary>Visible width of the band, set by the panel.</summary>
    public double ViewportWidth
    {
        get => _viewportWidth;
        internal set => Set(ref _viewportWidth, value);
    }

    /// <summary>True when a <see cref="BandMode.Scroll"/> band has more items than it can show.</summary>
    public bool CanScroll => Mode == BandMode.Scroll && ExtentWidth > ViewportWidth + 0.5;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name != nameof(HorizontalOffset))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanScroll)));
    }
}
