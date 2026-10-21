using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Renderers;

/// <summary>
/// A value drawn with the <see cref="Core.ValueStyle"/> of its measure's format: text color and weight, background,
/// data bar, icon. Lookless: its default template is in <c>Themes/FormattedValue.xaml</c>; the parts it shows are
/// exposed as properties (<see cref="CellBackground"/>, <see cref="BarBrush"/>...) for a template to use.
/// </summary>
public class FormattedValue : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(FormattedValue), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueStyleProperty = DependencyProperty.Register(
        nameof(ValueStyle), typeof(ValueStyle), typeof(FormattedValue), new PropertyMetadata(null, (d, _) => ((FormattedValue)d).Apply()));

    private static readonly DependencyPropertyKey CellBackgroundKey = DependencyProperty.RegisterReadOnly(
        nameof(CellBackground), typeof(Brush), typeof(FormattedValue), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey BarBrushKey = DependencyProperty.RegisterReadOnly(
        nameof(BarBrush), typeof(Brush), typeof(FormattedValue), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey BarLengthKey = DependencyProperty.RegisterReadOnly(
        nameof(BarLength), typeof(GridLength), typeof(FormattedValue), new PropertyMetadata(new GridLength(0)));

    private static readonly DependencyPropertyKey BarRestKey = DependencyProperty.RegisterReadOnly(
        nameof(BarRest), typeof(GridLength), typeof(FormattedValue), new PropertyMetadata(new GridLength(1, GridUnitType.Star)));

    private static readonly DependencyPropertyKey IconKey = DependencyProperty.RegisterReadOnly(
        nameof(Icon), typeof(string), typeof(FormattedValue), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey IconBrushKey = DependencyProperty.RegisterReadOnly(
        nameof(IconBrush), typeof(Brush), typeof(FormattedValue), new PropertyMetadata(null));

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment), typeof(TextAlignment), typeof(FormattedValue), new PropertyMetadata(TextAlignment.Right));

    public static readonly DependencyProperty CellBackgroundProperty = CellBackgroundKey.DependencyProperty;
    public static readonly DependencyProperty BarBrushProperty = BarBrushKey.DependencyProperty;
    public static readonly DependencyProperty BarLengthProperty = BarLengthKey.DependencyProperty;
    public static readonly DependencyProperty BarRestProperty = BarRestKey.DependencyProperty;
    public static readonly DependencyProperty IconProperty = IconKey.DependencyProperty;
    public static readonly DependencyProperty IconBrushProperty = IconBrushKey.DependencyProperty;

    static FormattedValue()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FormattedValue), new FrameworkPropertyMetadata(typeof(FormattedValue)));
        FocusableProperty.OverrideMetadata(typeof(FormattedValue), new FrameworkPropertyMetadata(false));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ValueStyle? ValueStyle
    {
        get => (ValueStyle?)GetValue(ValueStyleProperty);
        set => SetValue(ValueStyleProperty, value);
    }

    /// <summary>Alignment of the text: the format's, otherwise the one set on the control (numbers to the right).</summary>
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public Brush? CellBackground => (Brush?)GetValue(CellBackgroundProperty);

    public Brush? BarBrush => (Brush?)GetValue(BarBrushProperty);

    /// <summary>The share of the width the data bar takes (a star length), next to <see cref="BarRest"/>.</summary>
    public GridLength BarLength => (GridLength)GetValue(BarLengthProperty);

    public GridLength BarRest => (GridLength)GetValue(BarRestProperty);

    public string? Icon => (string?)GetValue(IconProperty);

    public Brush? IconBrush => (Brush?)GetValue(IconBrushProperty);

    private void Apply()
    {
        var style = ValueStyle;
        SetValue(CellBackgroundKey, Brush(style?.Background));
        var bar = Math.Abs(style?.Bar ?? 0);
        SetValue(BarBrushKey, style?.Bar is null ? null : Brush(style.BarColor));
        SetValue(BarLengthKey, new GridLength(bar, GridUnitType.Star));
        SetValue(BarRestKey, new GridLength(1 - bar, GridUnitType.Star));
        SetValue(IconKey, style?.Icon);
        SetValue(IconBrushKey, Brush(style?.IconColor));

        // Only what the format sets is local: the rest comes from the grid and the theme
        if (Brush(style?.Foreground) is { } foreground) Foreground = foreground; else ClearValue(ForegroundProperty);
        if (style?.IsBold == true) FontWeight = FontWeights.Bold; else ClearValue(FontWeightProperty);
        if (style?.IsItalic == true) FontStyle = FontStyles.Italic; else ClearValue(FontStyleProperty);
        if (style?.Alignment is { } alignment) SetCurrentValue(TextAlignmentProperty, Alignment(alignment));
    }

    internal static TextAlignment Alignment(TextAlign alignment) => alignment switch
    {
        TextAlign.Left => TextAlignment.Left,
        TextAlign.Center => TextAlignment.Center,
        _ => TextAlignment.Right,
    };

    // Colors are resolved from the theme by Core (#RRGGBB)
    internal static SolidColorBrush? Brush(string? color)
    {
        if (color is null) return null;
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
