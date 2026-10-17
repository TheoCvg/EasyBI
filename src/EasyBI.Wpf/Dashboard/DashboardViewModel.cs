using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Dashboard;

public enum DashboardItemKind { Empty, Visual, Text, Image }

/// <summary>
/// Live state of a <see cref="DashboardPanel"/>. Every change goes through a <see cref="ReportLayout"/>
/// operation on the immutable report, then the band and item view-models are synchronized by id: they are
/// reused, so items that did not change keep their rendering and visuals do not query again.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    /// <summary>Space between items and between bands, in device-independent pixels.</summary>
    public const double Gap = 14;

    private const int MaxHistory = 50;

    private readonly Dictionary<string, DashboardBandViewModel> _bands = [];
    private readonly Dictionary<string, DashboardItemViewModel> _items = [];
    private readonly LinkedList<ReportDefinition> _undo = new();
    private readonly Stack<ReportDefinition> _redo = new();
    private ReportDefinition _report = ReportLayout.Normalize(new ReportDefinition());

    public DashboardViewModel() => Sync(_report);

    /// <summary>Raised after a change made through the dashboard (not after <see cref="Load"/> or a preview).</summary>
    public event EventHandler? ReportChanged;

    public ReportDefinition Report => _report;

    public ObservableCollection<DashboardBandViewModel> Bands { get; } = [];

    public int Columns => _report.Layout!.Columns;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public DashboardItemViewModel? FindItem(string itemId) => _items.GetValueOrDefault(itemId);

    /// <summary>Shows another report; the undo history starts over.</summary>
    public void Load(ReportDefinition report)
    {
        _report = ReportLayout.Normalize(report);
        _undo.Clear();
        _redo.Clear();
        Sync(_report);
        OnHistoryChanged();
    }

    public void Apply(Func<ReportDefinition, ReportDefinition> operation)
    {
        // Reports are immutable: the history is simply the previous reports
        _undo.AddLast(_report);
        if (_undo.Count > MaxHistory) _undo.RemoveFirst();
        _redo.Clear();
        Show(operation(_report));
    }

    /// <summary>Shows where items would go (while dragging or resizing) without changing the report.</summary>
    public void Preview(Func<ReportDefinition, ReportDefinition> operation) => Sync(operation(_report));

    /// <summary>Puts items back where the report says (drag cancelled).</summary>
    public void EndPreview() => Sync(_report);

    public void Undo()
    {
        if (_undo.Last is not { } previous) return;
        _undo.RemoveLast();
        _redo.Push(_report);
        Show(previous.Value);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var next)) return;
        _undo.AddLast(_report);
        Show(next);
    }

    private void Show(ReportDefinition report)
    {
        _report = report;
        Sync(report);
        OnHistoryChanged();
        ReportChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void Sync(ReportDefinition report)
    {
        var layout = report.Layout!;
        var bands = layout.Bands.Select((band, index) =>
        {
            var bandVm = _bands.TryGetValue(band.Id, out var b) ? b : _bands[band.Id] = new DashboardBandViewModel(band.Id);
            bandVm.Update(band, index == 0, layout);
            Reconcile(bandVm.Items, band.Items.Select(item =>
            {
                var itemVm = _items.TryGetValue(item.Id, out var i) ? i : _items[item.Id] = new DashboardItemViewModel(item.Id);
                itemVm.Update(bandVm, item, ReportLayout.VisualOf(report, item), layout.RowHeight);
                return itemVm;
            }).ToList());
            return bandVm;
        }).ToList();
        Reconcile(Bands, bands);

        foreach (var id in _bands.Keys.Except(layout.Bands.Select(b => b.Id)).ToList()) _bands.Remove(id);
        foreach (var id in _items.Keys.Except(layout.Bands.SelectMany(b => b.Items).Select(i => i.Id)).ToList()) _items.Remove(id);
        OnPropertyChanged(nameof(Columns));
    }

    /// <summary>Makes <paramref name="target"/> equal to <paramref name="items"/> with minimal moves, keeping item instances.</summary>
    private static void Reconcile<T>(ObservableCollection<T> target, IReadOnlyList<T> items) where T : class
    {
        for (var i = target.Count - 1; i >= 0; i--)
            if (!items.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < items.Count; i++)
        {
            var current = target.IndexOf(items[i]);
            if (current < 0) target.Insert(i, items[i]);
            else if (current != i) target.Move(current, i);
        }
    }
}

public sealed class DashboardBandViewModel(string id) : ObservableObject
{
    private BandMode _mode;
    private bool _isFirst;
    private int _columns = DashboardLayout.DefaultColumns;
    private double _rowHeight = DashboardLayout.DefaultRowHeight;

    public string Id { get; } = id;

    public ObservableCollection<DashboardItemViewModel> Items { get; } = [];

    public BandMode Mode
    {
        get => _mode;
        private set => SetProperty(ref _mode, value);
    }

    public bool IsFirst
    {
        get => _isFirst;
        private set => SetProperty(ref _isFirst, value);
    }

    public int Columns
    {
        get => _columns;
        private set => SetProperty(ref _columns, value);
    }

    public double RowHeight
    {
        get => _rowHeight;
        private set => SetProperty(ref _rowHeight, value);
    }

    internal void Update(LayoutBand band, bool isFirst, DashboardLayout layout)
    {
        Mode = band.Mode;
        IsFirst = isFirst;
        Columns = layout.Columns;
        RowHeight = layout.RowHeight;
    }
}

public sealed class DashboardItemViewModel(string id) : ObservableObject
{
    private DashboardBandViewModel? _band;
    private int _column;
    private int _row;
    private int _columnSpan = 1;
    private int _rowSpan = 1;
    private double _width = LayoutItem.DefaultWidth;
    private double _height;
    private bool _isFullRow;
    private VisualDefinition? _visual;
    private TextContent? _text;
    private ImageContent? _image;
    private ImageSource? _imageSource;
    private bool _isEditingText;
    private string _editText = "";
    private bool _showFullRowWarning;

    public string Id { get; } = id;

    public DashboardBandViewModel Band => _band!;

    public int Column
    {
        get => _column;
        private set => SetProperty(ref _column, value);
    }

    public int Row
    {
        get => _row;
        private set => SetProperty(ref _row, value);
    }

    public int ColumnSpan
    {
        get => _columnSpan;
        private set => SetProperty(ref _columnSpan, value);
    }

    public int RowSpan
    {
        get => _rowSpan;
        private set => SetProperty(ref _rowSpan, value);
    }

    /// <summary>Width in scrolling and wrapping bands.</summary>
    public double Width
    {
        get => _width;
        private set => SetProperty(ref _width, value);
    }

    /// <summary>Height in pixels of <see cref="RowSpan"/> grid rows, used by scrolling and wrapping bands.</summary>
    public double Height
    {
        get => _height;
        private set => SetProperty(ref _height, value);
    }

    public bool IsFullRow
    {
        get => _isFullRow;
        private set => SetProperty(ref _isFullRow, value);
    }

    public DashboardItemKind Kind =>
        _visual is not null ? DashboardItemKind.Visual
        : _text is not null ? DashboardItemKind.Text
        : _image is not null ? DashboardItemKind.Image
        : DashboardItemKind.Empty;

    public VisualDefinition? Visual
    {
        get => _visual;
        private set
        {
            if (SetProperty(ref _visual, value)) OnPropertyChanged(nameof(Kind));
        }
    }

    public TextContent? Text
    {
        get => _text;
        private set
        {
            if (!SetProperty(ref _text, value)) return;
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(IsTextEmpty));
        }
    }

    public bool IsTextEmpty => string.IsNullOrWhiteSpace(_text?.Text);

    public ImageContent? Image
    {
        get => _image;
        private set
        {
            if (!SetProperty(ref _image, value)) return;
            OnPropertyChanged(nameof(Kind));
            ImageSource = Decode(value);
        }
    }

    public ImageSource? ImageSource
    {
        get => _imageSource;
        private set => SetProperty(ref _imageSource, value);
    }

    /// <summary>True while the text is edited in place.</summary>
    public bool IsEditingText
    {
        get => _isEditingText;
        set => SetProperty(ref _isEditingText, value);
    }

    /// <summary>Text being typed; applied to the report when the edit is committed.</summary>
    public string EditText
    {
        get => _editText;
        set => SetProperty(ref _editText, value);
    }

    /// <summary>True while the banner asks whether to give up the full row to add an item next to it.</summary>
    public bool ShowFullRowWarning
    {
        get => _showFullRowWarning;
        set => SetProperty(ref _showFullRowWarning, value);
    }

    internal void Update(DashboardBandViewModel band, LayoutItem item, VisualDefinition? visual, double rowHeight)
    {
        if (!ReferenceEquals(_band, band))
        {
            _band = band;
            OnPropertyChanged(nameof(Band));
        }
        Column = item.Column;
        Row = item.Row;
        ColumnSpan = item.ColumnSpan;
        RowSpan = item.RowSpan;
        Width = item.Width;
        Height = item.RowSpan * rowHeight + (item.RowSpan - 1) * DashboardViewModel.Gap;
        IsFullRow = item.IsFullRow;
        if (!item.IsFullRow) ShowFullRowWarning = false;
        Visual = visual;
        Text = item.Text;
        Image = item.Image;
    }

    /// <summary>Decodes the embedded image once; an unreadable image shows nothing rather than failing.</summary>
    internal static ImageSource? Decode(ImageContent? image)
    {
        if (image is null) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(Convert.FromBase64String(image.Data));
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException or ArgumentException)
        {
            return null;
        }
    }
}
