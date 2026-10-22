using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>The scale of a gauge and its rules.</summary>
public class GaugeTests
{
    [Fact]
    public void Without_settings_a_gauge_goes_from_zero_to_a_round_number_above_the_value()
    {
        Assert.Equal((0d, 1000d), GaugeScale.Of(null, 870));
        Assert.Equal((0d, 250_000d), GaugeScale.Of(null, 210_000));
        Assert.Equal((0d, 1d), GaugeScale.Of(null, null)); // nothing to show: a scale all the same
    }

    [Fact]
    public void The_target_and_the_settings_shape_the_scale()
    {
        Assert.Equal((0d, 2000d), GaugeScale.Of(new GaugeSettings { Target = 1500 }, 870));
        Assert.Equal((500d, 900d), GaugeScale.Of(new GaugeSettings { Minimum = 500, Maximum = 900 }, 870));
        Assert.Equal((-50d, 0d), GaugeScale.Of(null, -50)); // a negative value lowers the minimum
    }

    [Fact]
    public void A_value_falls_on_the_scale_and_stays_within_it()
    {
        Assert.Equal(0.25, GaugeScale.Position(25, 0, 100));
        Assert.Equal(1, GaugeScale.Position(150, 0, 100));
        Assert.Equal(0, GaugeScale.Position(-5, 0, 100));
    }

    [Fact]
    public void A_gauge_shows_a_single_value_without_axis_or_legend()
    {
        var gauge = new VisualDefinition { Mark = MarkType.Gauge, Values = [new("Amount")] };

        Assert.Null(VisualValidator.Validate(gauge));
        Assert.NotNull(VisualValidator.Validate(gauge with { Values = [new("Amount"), new("Margin")] }));
        Assert.NotNull(VisualValidator.Validate(gauge with { Axis = [new("Region")] }));
    }

    [Fact]
    public void The_settings_of_a_gauge_are_saved_with_it_and_left_out_otherwise()
    {
        var gauge = new VisualDefinition { Mark = MarkType.Gauge, Values = [new("Amount")], Gauge = new GaugeSettings { Maximum = 100, Target = 80 } };
        string Json(VisualDefinition visual) => ReportSerializer.Serialize(new ReportDefinition { Visuals = [visual] });

        var loaded = ReportSerializer.Deserialize(Json(gauge)).Visuals[0];

        Assert.Equal((MarkType.Gauge, (double?)100, (double?)80, (double?)null), (loaded.Mark, loaded.Gauge!.Maximum, loaded.Gauge.Target, loaded.Gauge.Minimum));
        Assert.DoesNotContain("\"gauge\"", Json(gauge with { Gauge = null }));
    }
}
