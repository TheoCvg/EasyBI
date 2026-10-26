using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using EasyBI.Core;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Colors the formula of a <see cref="TextBox"/> and underlines its problem. It lies over the text box, whose text is
/// made transparent, and draws each character where the text box places it (<see cref="TextBox.GetRectFromCharacterIndex(int)"/>):
/// the text box keeps the application's style, its caret, selection, scrolling and undo.
/// </summary>
internal sealed class FormulaHighlighter : FrameworkElement
{
    public static readonly DependencyProperty ProblemProperty = DependencyProperty.Register(
        nameof(Problem), typeof(FormulaProblem), typeof(FormulaHighlighter),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = Brush(nameof(Foreground));
    public static readonly DependencyProperty FieldBrushProperty = Brush(nameof(FieldBrush));
    public static readonly DependencyProperty DimensionBrushProperty = Brush(nameof(DimensionBrush));
    public static readonly DependencyProperty FunctionBrushProperty = Brush(nameof(FunctionBrush));
    public static readonly DependencyProperty TextBrushProperty = Brush(nameof(TextBrush));
    public static readonly DependencyProperty NumberBrushProperty = Brush(nameof(NumberBrush));
    public static readonly DependencyProperty ErrorBrushProperty = Brush(nameof(ErrorBrush));

    private readonly TextBox _box;
    private readonly Func<string, bool> _isDimension;
    private bool _redrawPending;

    /// <param name="box">The text box of the formula, which this element must cover exactly.</param>
    /// <param name="isDimension">Whether a field written in brackets (its caption) is a dimension.</param>
    public FormulaHighlighter(TextBox box, Func<string, bool> isDimension)
    {
        _box = box;
        _isDimension = isDimension;
        IsHitTestVisible = false;
        SetResourceReference(ForegroundProperty, EasyBITheme.ForegroundBrushKey);
        SetResourceReference(FieldBrushProperty, EasyBITheme.MeasureBrushKey);
        SetResourceReference(DimensionBrushProperty, EasyBITheme.FormulaDimensionBrushKey);
        SetResourceReference(FunctionBrushProperty, EasyBITheme.FormulaFunctionBrushKey);
        SetResourceReference(TextBrushProperty, EasyBITheme.FormulaTextBrushKey);
        SetResourceReference(NumberBrushProperty, EasyBITheme.FormulaNumberBrushKey);
        SetResourceReference(ErrorBrushProperty, EasyBITheme.ErrorBrushKey);
        // The text box draws its caret and selection, not its text
        box.Foreground = Brushes.Transparent;
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, EasyBITheme.ForegroundBrushKey);
        box.TextChanged += (_, _) => InvalidateVisual();
        box.SizeChanged += (_, _) => InvalidateVisual();
        box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => InvalidateVisual()));
    }

    public FormulaProblem? Problem
    {
        get => (FormulaProblem?)GetValue(ProblemProperty);
        set => SetValue(ProblemProperty, value);
    }

    public Brush? Foreground => (Brush?)GetValue(ForegroundProperty);
    public Brush? FieldBrush => (Brush?)GetValue(FieldBrushProperty);
    public Brush? DimensionBrush => (Brush?)GetValue(DimensionBrushProperty);
    public Brush? FunctionBrush => (Brush?)GetValue(FunctionBrushProperty);
    public Brush? TextBrush => (Brush?)GetValue(TextBrushProperty);
    public Brush? NumberBrush => (Brush?)GetValue(NumberBrushProperty);
    public Brush? ErrorBrush => (Brush?)GetValue(ErrorBrushProperty);

    protected override void OnRender(DrawingContext context)
    {
        var text = _box.Text;
        if (text.Length == 0) return;
        var rects = Enumerable.Range(0, text.Length).Select(Position).ToArray();
        // The text box lays its lines out after this element is drawn when the text wraps onto another line: no
        // position is known yet, so draw again once the layout is done
        if (!_box.IsArrangeValid || rects.Any(r => r.IsEmpty))
        {
            RedrawAfterLayout();
            return;
        }
        context.PushClip(new RectangleGeometry(new Rect(RenderSize)));

        // One brush (and weight) per character
        var brushes = new Brush?[text.Length];
        var bold = new bool[text.Length];
        Compat.Fill(brushes, Foreground);
        foreach (var token in FormulaEditing.Tokenize(text))
        {
            var brush = token.Kind switch
            {
                FormulaTokenKind.Field => _isDimension(FieldCaption(text.Substring(token.Start, token.Length))) ? DimensionBrush : FieldBrush,
                FormulaTokenKind.Function or FormulaTokenKind.Keyword => FunctionBrush,
                FormulaTokenKind.Text => TextBrush,
                FormulaTokenKind.Number => NumberBrush,
                _ => Foreground,
            };
            for (var i = token.Start; i < token.Start + token.Length; i++)
            {
                brushes[i] = brush;
                bold[i] = token.Kind == FormulaTokenKind.Function;
            }
        }

        var typeface = new Typeface(_box.FontFamily, _box.FontStyle, FontWeights.Normal, _box.FontStretch);
        var boldface = new Typeface(_box.FontFamily, _box.FontStyle, FontWeights.SemiBold, _box.FontStretch);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // Runs of characters on the same line with the same look, drawn where the text box places their first one
        for (var start = 0; start < text.Length;)
        {
            var end = start + 1;
            while (end < text.Length && brushes[end] == brushes[start] && bold[end] == bold[start] && rects[end].Top == rects[start].Top) end++;
            var run = new FormattedText(text[start..end], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                bold[start] ? boldface : typeface, _box.FontSize, brushes[start] ?? Brushes.Black, dpi);
            context.DrawText(run, rects[start].TopLeft);
            start = end;
        }

        if (Problem is { } problem && ErrorBrush is { } error) Underline(context, text, problem, rects, error);
        context.Pop();
    }

    // A wavy line under the problem; a problem at the end (something is missing) is marked after the last character
    private void Underline(DrawingContext context, string text, FormulaProblem problem, Rect[] rects, Brush brush)
    {
        var pen = new Pen(brush, 1.2);
        pen.Freeze();
        var start = Compat.Clamp(problem.Start, 0, text.Length);
        var end = Compat.Clamp(problem.Start + problem.Length, start, text.Length);
        if (start == end)
        {
            var at = start < text.Length ? rects[start] : Position(text.Length - 1, trailing: true);
            if (!at.IsEmpty) Wave(context, pen, at.Left, at.Left + 10, at.Bottom);
            return;
        }
        for (var i = start; i < end;)
        {
            var j = i + 1;
            while (j < end && rects[j].Top == rects[i].Top) j++;
            var right = j < text.Length && rects[j].Top == rects[i].Top ? rects[j].Left : Position(j - 1, trailing: true).Left;
            Wave(context, pen, rects[i].Left, Math.Max(right, rects[i].Left + 4), rects[i].Bottom);
            i = j;
        }
    }

    private static void Wave(DrawingContext context, Pen pen, double left, double right, double bottom)
    {
        if (!Compat.IsFinite(left) || !Compat.IsFinite(right) || !Compat.IsFinite(bottom)) return;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(left, bottom - 1), false, false);
            var up = true;
            for (var x = left + 2; x <= right; x += 2, up = !up)
                g.LineTo(new Point(x, up ? bottom - 3 : bottom - 1), true, true);
        }
        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private Rect Position(int index) => Position(index, trailing: false);

    // Where the text box draws a character, in this element's coordinates
    private Rect Position(int index, bool trailing)
    {
        var rect = _box.GetRectFromCharacterIndex(index, trailing);
        if (rect.IsEmpty) return Rect.Empty;
        var origin = _box.TranslatePoint(rect.TopLeft, this);
        return new Rect(origin, rect.Size);
    }

    private void RedrawAfterLayout()
    {
        if (_redrawPending) return;
        _redrawPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _redrawPending = false;
            InvalidateVisual();
        });
    }

    // "[Région]" → "Région" ("]]" stands for "]")
    private static string FieldCaption(string token) =>
        token.TrimStart('[').TrimEnd(']').Replace("]]", "]").Trim();

    private static DependencyProperty Brush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(FormulaHighlighter), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}
