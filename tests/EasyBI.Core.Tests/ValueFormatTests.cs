using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>A measure's format: fixed style, rules, color scale, data bars and icons.</summary>
public class ValueFormatTests
{
    private static readonly BiTheme Theme = BiTheme.Default with { Success = "#00AA00", Warning = "#FFAA00", Error = "#CC0000" };

    // The values shown: 0 to 100
    private static ValueStyle Style(ValueFormat format, double? value) => new ValueStyler(format, [0, 25, 50, 100, null], Theme).Style(value);

    [Fact]
    public void The_first_matching_rule_styles_the_value_and_overrides_the_fixed_color()
    {
        var format = new ValueFormat
        {
            Color = ThemeColors.Muted,
            IsItalic = true,
            Rules =
            [
                new FormatRule { Operator = FilterOperator.LessThan, Value = 10, Color = ThemeColors.Error, IsBold = true },
                new FormatRule { Operator = FilterOperator.LessThan, Value = 50, Background = "#FFEEEE" },
            ],
        };

        var low = Style(format, 5);
        Assert.Equal(("#CC0000", (string?)null, true, true), (low.Foreground, low.Background, low.IsBold, low.IsItalic));
        var middle = Style(format, 30);
        Assert.Equal((Theme.MutedForeground ?? "#6B7280", "#FFEEEE", false), (middle.Foreground, middle.Background, middle.IsBold));
        Assert.Null(Style(format, 80).Background);
    }

    [Fact]
    public void A_range_rule_may_leave_one_end_open()
    {
        var atLeast = new FormatRule { Operator = FilterOperator.Range, Value = 10 };
        var between = new FormatRule { Operator = FilterOperator.Range, Value = 10, Maximum = 20 };

        Assert.True(ValueStyler.Matches(atLeast, 1000));
        Assert.False(ValueStyler.Matches(atLeast, 9));
        Assert.True(ValueStyler.Matches(between, 20));
        Assert.False(ValueStyler.Matches(between, 20.5));
        Assert.False(ValueStyler.Matches(new FormatRule { Operator = FilterOperator.GreaterThan }, 1)); // no number: no match
    }

    [Fact]
    public void A_color_scale_goes_from_the_smallest_to_the_largest_value_shown()
    {
        var format = new ValueFormat { Scale = new ColorScale { Minimum = "#000000", Maximum = "#FFFFFF" } };

        Assert.Equal("#000000", Style(format, 0).Background);
        Assert.Equal("#808080", Style(format, 50).Background);
        Assert.Equal("#FFFFFF", Style(format, 100).Background);
        // A rule's background comes first
        var withRule = format with { Rules = [new FormatRule { Operator = FilterOperator.GreaterThan, Value = 90, Background = ThemeColors.Success }] };
        Assert.Equal(Soft("#00AA00"), Style(withRule, 100).Background);
    }

    [Fact]
    public void A_three_color_scale_passes_through_its_middle_color()
    {
        var format = new ValueFormat { Scale = new ColorScale { Minimum = ThemeColors.Error, Middle = "#FFFFFF", Maximum = ThemeColors.Success } };

        Assert.Equal(Soft("#CC0000"), Style(format, 0).Background);
        Assert.Equal("#FFFFFF", Style(format, 50).Background);
        Assert.Equal(Soft("#00AA00"), Style(format, 100).Background);
    }

    // A theme color used as a background is softened with the theme's (white here)
    private static string? Soft(string color) => ValueStyler.Mix("#FFFFFF", color, ValueStyler.FillStrength);

    [Fact]
    public void A_theme_color_in_a_background_is_softened_and_a_custom_one_kept()
    {
        var format = new ValueFormat
        {
            Rules =
            [
                new FormatRule { Operator = FilterOperator.LessThan, Value = 10, Background = ThemeColors.Error },
                new FormatRule { Operator = FilterOperator.LessThan, Value = 50, Background = "#FF0000" },
            ],
        };
        var dark = new ValueStyler(format, [0, 100], Theme with { Background = "#000000" });

        Assert.Equal("#F0B2B2", Style(format, 5).Background);
        Assert.Equal("#3D0000", dark.Style(5).Background);
        Assert.Equal("#FF0000", Style(format, 30).Background);
    }

    [Fact]
    public void Data_bars_compare_each_value_with_the_largest_one_and_show_negative_values_apart()
    {
        var format = new ValueFormat { Bar = new DataBar() };
        var styler = new ValueStyler(format, [-50, 0, 100], Theme);

        Assert.Equal((1d, Theme.ColorAt(0)), (styler.Style(100).Bar!.Value, styler.Style(100).BarColor));
        Assert.Equal(0.25, styler.Style(25).Bar);
        Assert.Equal((-0.5, "#CC0000"), (styler.Style(-50).Bar!.Value, styler.Style(-50).BarColor));
    }

    [Fact]
    public void Icons_split_the_values_in_thirds_unless_thresholds_are_given()
    {
        var automatic = new ValueFormat { Icons = new IconSet { Kind = IconKind.Triangles } };
        Assert.Equal(("▲", "#00AA00"), (Style(automatic, 90).Icon, Style(automatic, 90).IconColor));
        Assert.Equal(("▬", "#FFAA00"), (Style(automatic, 50).Icon, Style(automatic, 50).IconColor));
        Assert.Equal(("▼", "#CC0000"), (Style(automatic, 10).Icon, Style(automatic, 10).IconColor));

        var thresholds = new ValueFormat { Icons = new IconSet { Kind = IconKind.Arrows, Low = 5, High = 95 } };
        Assert.Equal("→", Style(thresholds, 90).Icon);
        Assert.Equal("↑", Style(thresholds, 95).Icon);
    }

    [Fact]
    public void A_missing_value_only_takes_the_fixed_style()
    {
        var format = new ValueFormat { IsBold = true, Bar = new DataBar(), Icons = new IconSet(), Scale = new ColorScale() };

        var style = Style(format, null);

        Assert.True(style.IsBold);
        Assert.Equal((null, null, (string?)null), (style.Bar, style.Icon, style.Background));
    }

    [Fact]
    public void Theme_colors_are_named_and_custom_colors_kept()
    {
        Assert.Equal(Theme.ColorAt(0), Theme.Resolve(ThemeColors.Accent));
        Assert.Equal("#FFAA00", Theme.Resolve(ThemeColors.Warning));
        Assert.Equal("#123456", Theme.Resolve("#123456"));
        Assert.Null(Theme.Resolve(null));
        Assert.Equal("#2BA37B", BiTheme.Default.Resolve(ThemeColors.Success)); // EasyBI's own when the theme has none
    }

    [Fact]
    public void A_format_is_saved_with_its_measure_and_does_not_change_the_query()
    {
        var format = new ValueFormat { IsBold = true, Rules = [new FormatRule { Operator = FilterOperator.Range, Value = 1, Color = ThemeColors.Error }] };
        var visual = new VisualDefinition { Model = "m", Type = VisualType.Table, Values = [new("Amount") { Format = format }] };

        var json = ReportSerializer.Serialize(new ReportDefinition { Visuals = [visual] });
        var loaded = ReportSerializer.Deserialize(json).Visuals[0].Values[0].Format!;

        Assert.True(loaded.IsBold);
        Assert.Equal((FilterOperator.Range, (double?)1, (double?)null, "error"), (loaded.Rules[0].Operator, loaded.Rules[0].Value, loaded.Rules[0].Maximum, loaded.Rules[0].Color));
        Assert.Null(Assert.Single(visual.ToQuery().Measures).Format);
        Assert.DoesNotContain("\"format\"", ReportSerializer.Serialize(new ReportDefinition { Visuals = [visual with { Values = [new("Amount")] }] }));
    }
}
