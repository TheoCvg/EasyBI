using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf;
using EasyBI.Wpf.Renderers;

namespace EasyBI.Sample;

/// <summary>
/// A visual type of the application, which no charting library offers: the items of a dimension ranked by a measure,
/// each with a bar relative to the leader. The renderer brings its type (<see cref="RendererCapabilities.VisualTypes"/>):
/// plugging it in is enough for the designer to offer it, with its wells, its setting and its icon (App.xaml).
/// </summary>
public sealed class RankingRenderer : IVisualRenderer
{
    public static readonly VisualType Ranking = new("sample.ranking");

    /// <summary>A setting of the type: saved with the visual under the type's id, whatever renderer draws it.</summary>
    public const string ShareOption = "share";

    public static VisualTypeDefinition Definition { get; } = new(Ranking, () => App.Texts["Ranking"])
    {
        Group = VisualTypeGroup.Comparison,
        Axis = new() { MinFields = 1, MaxFields = 1, Caption = () => App.Texts["RankingItems"] },
        Legend = VisualWell.Unused,
        Values = new() { MinFields = 1, MaxFields = 1, Caption = () => App.Texts["RankingBy"] },
        HasTooltips = true,
        Options = [RendererOption.Toggle(ShareOption, () => App.Texts["RankingShare"])],
    };

    public string Name => "Ranking";

    public RendererCapabilities Capabilities { get; } = new(new HashSet<VisualType> { Ranking }) { VisualTypes = [Definition] };

    public FrameworkElement Render(RenderRequest request) => new RankingView(request);

    private sealed class RankingView : ScrollViewer
    {
        public RankingView(RenderRequest request)
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            if (request.Chart.Series.FirstOrDefault() is not { } series) return;

            var tooltip = new ChartTooltipHost(this);
            var share = request.Visual.GetTypeOption<bool>(ShareOption);
            var ranked = series.Values.Select((value, category) => (Category: category, Value: value ?? 0)).OrderByDescending(r => r.Value).ToList();
            var leader = Math.Max(ranked.FirstOrDefault().Value, double.Epsilon);
            var total = ranked.Sum(r => r.Value);
            var bar = new SolidColorBrush((Color)ColorConverter.ConvertFromString(request.Theme.ColorAt(0)));

            var rows = new StackPanel { Margin = new Thickness(4, 2, 8, 2) };
            foreach (var (rank, (category, value)) in ranked.Select((r, rank) => (rank + 1, r)))
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(Text($"{rank}", EasyBITheme.MutedForegroundBrushKey));

                // The bar is as long as the value, relative to the leader's
                var length = new Border { Height = 5, CornerRadius = new CornerRadius(2), Background = bar, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
                var label = new StackPanel();
                label.Children.Add(Text(request.Chart.Categories[category], EasyBITheme.ForegroundBrushKey));
                label.Children.Add(length);
                label.SizeChanged += (_, _) => length.Width = label.ActualWidth * value / leader;
                Grid.SetColumn(label, 1);
                row.Children.Add(label);

                var amount = ValueFormatter.Format(value, series.Format) + (share && total > 0 ? $"  {ValueFormatter.Format(value / total, "P0")}" : "");
                var shown = Text(amount, EasyBITheme.ForegroundBrushKey);
                shown.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(shown, 2);
                row.Children.Add(shown);

                // Like a point of a chart: a click cross-filters the other visuals, the pointer shows EasyBI's tooltip
                row.Opacity = request.IsEmphasized(category, 0) ? 1 : SelectionFilters.DimmedOpacity;
                row.MouseMove += (_, _) => tooltip.Show(request.TooltipFor(category, [0]));
                if (request.Select is { } select)
                {
                    row.Cursor = Cursors.Hand;
                    row.MouseLeftButtonUp += (_, e) =>
                    {
                        select(new CategorySelection(category));
                        e.Handled = true;
                    };
                }
                rows.Children.Add(row);
            }
            Content = rows;
        }

        private static TextBlock Text(string text, ComponentResourceKey brush)
        {
            var block = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            block.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return block;
        }
    }
}
