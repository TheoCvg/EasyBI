using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyBI.Wpf.Tests;

internal static class TestHost
{
    /// <summary>
    /// Gives an element a parent. A lookless control created in code gets its default template once initialized,
    /// which happens when it is added to a parent: measured on its own, it would stay empty.
    /// </summary>
    public static T Hosted<T>(this T element) where T : UIElement
    {
        if (VisualTreeHelper.GetParent(element) is null && LogicalTreeHelper.GetParent(element) is null)
            _ = new Border { Child = element };
        return element;
    }
}
