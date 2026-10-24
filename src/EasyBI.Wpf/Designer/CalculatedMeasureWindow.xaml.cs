using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

/// <summary>Built-in dialog of a <see cref="CalculatedMeasureEditor"/>: saves the measure in its context on OK.</summary>
internal partial class CalculatedMeasureWindow : Window
{
    private readonly CalculatedMeasureEditor _editor;

    public CalculatedMeasureWindow(CalculatedMeasureEditor editor)
    {
        InitializeComponent();
        DataContext = _editor = editor;
        editor.PropertyChanged += OnEditorChanged;
        ShowStatus();
        Loaded += (_, _) => (editor.IsNew ? CaptionBox : FormulaBox).Focus();
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CalculatedMeasureEditor.Error) or nameof(CalculatedMeasureEditor.Preview)) ShowStatus();
    }

    private void ShowStatus()
    {
        var problem = _editor.Error;
        Status.Text = problem ?? (_editor.Preview is { } total ? Strings.Format(Strings.MeasurePreview, total) : "");
        Status.SetResourceReference(TextBlock.ForegroundProperty, problem is null ? EasyBITheme.MutedForegroundBrushKey : EasyBITheme.ErrorBrushKey);
    }

    // Inserts at the caret, replacing the selection; parentheses (and a function's) go around it
    private void OnInsert(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text }) return;
        var selected = FormulaBox.SelectedText;
        var start = FormulaBox.SelectionStart;
        var wraps = text.EndsWith("()", StringComparison.Ordinal);
        var inserted = wraps ? $"{text[..^1]}{selected})" : text;
        FormulaBox.SelectedText = inserted;
        FormulaBox.CaretIndex = wraps && selected.Length == 0 ? start + inserted.Length - 1 : start + inserted.Length;
        FormulaBox.SelectionLength = 0;
        FormulaBox.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!_editor.Save()) return;
        DialogResult = true;
    }
}
