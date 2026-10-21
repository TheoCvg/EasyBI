using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.Renderers;

namespace EasyBI.Wpf.Tests;

/// <summary>Formats of measures in the table, the matrix, the KPI and the designer.</summary>
public class ValueFormatUiTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount);

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(new[] { new Sale("Nord", "Web", 10m), new Sale("Nord", "Shop", 200m), new Sale("Sud", "Web", 50m) }, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Measure(s => s.Amount, "CA", format: "N0");

    private static readonly BiTheme Theme = BiTheme.Default with { Success = "#00AA00", Warning = "#FFAA00", Error = "#CC0000" };

    // Below 100 in red
    private static readonly ValueFormat LowInRed = new()
    {
        Rules = [new FormatRule { Operator = FilterOperator.LessThan, Value = 100, Color = ThemeColors.Error, IsBold = true }],
    };

    private static RenderRequest Request(VisualDefinition visual) =>
        Task.Run(() => VisualPipeline.PrepareAsync(Model(), visual, Theme)).Result;

    // The grid shown in a window away from the screen, so that its rows and cells exist
    private static DataGrid Shown(FrameworkElement element)
    {
        var window = new Window { Content = element, Width = 800, Height = 400, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        window.UpdateLayout();
        return (DataGrid)element;
    }

    private static FormattedValue Formatted(DataGrid grid, int line, int column)
    {
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(line);
        var presenter = (ContentPresenter)grid.Columns[column].GetCellContent(row)!;
        presenter.ApplyTemplate();
        return (FormattedValue)VisualTreeHelper.GetChild(presenter, 0);
    }

    private static Color? ColorOf(Brush? brush) => (brush as SolidColorBrush)?.Color;

    [Fact]
    public void A_table_draws_the_values_of_a_formatted_measure_with_their_style() => Sta.Run(() =>
    {
        var visual = new VisualDefinition { Model = "sales", Mark = MarkType.Table, Axis = [new("Region")], Values = [new("Amount") { Format = LowInRed }] };
        var grid = Shown(new TableRenderer().Render(Request(visual)));

        var sud = Formatted(grid, 1, 1); // 50
        Assert.Equal("50", sud.Text);
        Assert.Equal(Color.FromRgb(0xCC, 0, 0), ColorOf(sud.Foreground));
        Assert.Equal(FontWeights.Bold, sud.FontWeight);
        Assert.NotEqual(Color.FromRgb(0xCC, 0, 0), ColorOf(Formatted(grid, 0, 1).Foreground)); // 210
    });

    [Fact]
    public void A_matrix_formats_its_cells_and_leaves_the_totals_plain() => Sta.Run(() =>
    {
        var visual = new VisualDefinition
        {
            Model = "sales",
            Mark = MarkType.Matrix,
            Axis = [new("Region")],
            Legend = new("Channel"),
            Values = [new("Amount") { Format = new ValueFormat { Bar = new DataBar() } }],
        };
        var grid = Shown(new MatrixRenderer().Render(Request(visual)));

        // Nord: Shop 200 (the largest cell, not the total 210), Web 10
        Assert.Equal(1d, Formatted(grid, 0, 1).BarLength.Value);
        Assert.Equal(0.05, Formatted(grid, 0, 2).BarLength.Value, 10);
        Assert.IsType<TextBlock>(grid.Columns[3].GetCellContent((DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0))); // the total column
        Assert.Null(Formatted(grid, 2, 1).ValueStyle); // the total row
    });

    [Fact]
    public void A_kpi_takes_the_color_and_the_icon_of_its_format() => Sta.Run(() =>
    {
        var format = LowInRed with { Rules = [.. LowInRed.Rules], Icons = new IconSet { Kind = IconKind.Triangles, Low = 100, High = 1000 } };
        var visual = new VisualDefinition { Model = "sales", Mark = MarkType.Kpi, Values = [new("Amount") { Format = format with { Rules = [new FormatRule { Operator = FilterOperator.LessThan, Value = 1000, Color = ThemeColors.Error }] } }] };

        var card = (Viewbox)new KpiRenderer().Render(Request(visual));

        var value = ((StackPanel)((WrapPanel)card.Child).Children[0]).Children.OfType<TextBlock>().First();
        Assert.Equal(Color.FromRgb(0xCC, 0, 0), ColorOf(value.Foreground));
        Assert.StartsWith("▬", new System.Windows.Documents.TextRange(value.ContentStart, value.ContentEnd).Text); // 260: between the thresholds
    });

    [Fact]
    public void The_designer_edits_the_format_of_a_measure_and_saves_it_with_the_visual()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(Model()));
        designer.Load(new VisualDefinition { Model = "sales", Mark = MarkType.Table, Axis = [new("Region")], Values = [new("Amount")] });
        var amount = designer.Values.Single();
        designer.SelectedItem = amount;
        Assert.Contains(designer.Properties, s => s.Key == DesignerPropertySections.Formatting);
        Assert.Null(designer.Visual!.Values[0].Format); // nothing set yet

        var format = amount.Format;
        format.IsBold = true;
        format.AddRuleCommand.Execute(null);
        var rule = format.Rules.Single();
        rule.Condition = FormatRuleViewModel.Conditions.Single(c => c.Value == FilterOperator.Range);
        rule.Number = "10";
        rule.SecondNumber = "1,000";
        rule.Background.Selected = rule.Background.Options.Single(o => o.Value == ThemeColors.Warning);
        format.HasBar = true;

        var saved = designer.Visual!.Values[0].Format!;
        Assert.True(saved.IsBold);
        Assert.Equal((FilterOperator.Range, (double?)10, (double?)1000, ThemeColors.Warning),
            (saved.Rules[0].Operator, saved.Rules[0].Value, saved.Rules[0].Maximum, saved.Rules[0].Background));
        Assert.NotNull(saved.Bar);

        // Loaded again, the chip shows the same format
        designer.Load(designer.Visual);
        Assert.Equal("1000", designer.Values.Single().Format.Rules.Single().SecondNumber);
        Assert.True(designer.Values.Single().Format.HasBar);
    }

    [Fact]
    public void A_custom_color_counts_once_it_is_a_valid_hexadecimal_color()
    {
        var changes = 0;
        var color = new ColorChoiceViewModel(null, () => changes++);

        color.Selected = color.Options.Last(); // custom
        color.Hex = "#12";
        Assert.Null(color.Value);
        color.Hex = "#123456";

        Assert.Equal("#123456", color.Value);
        Assert.True(new ColorChoiceViewModel("#ABCDEF", () => { }).IsCustom);
    }

    [Fact]
    public void Only_measures_on_the_values_well_have_a_format() => Sta.Run(() =>
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(Model()));
        designer.Load(new VisualDefinition { Model = "sales", Mark = MarkType.Table, Axis = [new("Region")], Values = [new("Amount")] });

        designer.SelectedItem = designer.Axis.Single();

        Assert.DoesNotContain(designer.Properties, s => s.Key == DesignerPropertySections.Formatting);
    });

    [Fact]
    public void Charts_get_the_themes_success_warning_and_error_colors() => Sta.Run(() =>
    {
        var element = new Border();
        element.Resources.MergedDictionaries.Add(new EasyBIThemeDictionary { Mode = EasyBIThemeMode.Dark });

        var theme = EasyBITheme.ChartTheme(element);

        Assert.Equal("#FF4ADE80", theme.Success);
        Assert.Equal("#FFFBBF24", theme.Warning);
        Assert.Equal("#FFF87171", theme.Error);
    });
}
