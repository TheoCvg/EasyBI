using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>The coloring drawn over the formula's text box, whose own text is transparent.</summary>
public class FormulaHighlighterTests
{
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    // Pixels the highlighter painted (its text): it draws nothing else
    private static int PaintedPixels(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] != 0);
    }

    [Fact]
    public void The_formula_stays_visible_when_it_wraps_onto_more_lines() => Sta.Run(() =>
    {
        var box = new TextBox { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 15, Width = 300, Height = 120 };
        var host = new Grid();
        host.Children.Add(box);
        var highlighter = new FormulaHighlighter(box, _ => false);
        host.Children.Add(highlighter);
        var window = new Window { Content = host, Width = 400, Height = 200, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();

        box.Text = "SUM([Amount])";
        Pump();
        var oneLine = PaintedPixels(highlighter);
        Assert.True(oneLine > 0);

        // Typed one character at a time, as the user does, until the formula takes three lines
        foreach (var c in " + SUM([Amount]; [Region] = \"North\" AND [Channel] = \"Web\")")
        {
            box.AppendText(c.ToString());
            Pump();
        }

        Assert.True(box.GetLineIndexFromCharacterIndex(box.Text.Length - 1) >= 2);
        var painted = PaintedPixels(highlighter);
        Assert.True(painted > oneLine * 3, $"{painted} pixels after wrapping, {oneLine} on one line");
        window.Close();
    });
}
