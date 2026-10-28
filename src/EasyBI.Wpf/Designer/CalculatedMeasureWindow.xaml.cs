using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Built-in dialog of a <see cref="CalculatedMeasureEditor"/>: the formula on the left (colored, its problem underlined,
/// suggestions of what can be written as it is typed, the function at the caret) with the operator keypad; the
/// functions, measures and dimensions to search and insert on the right, each in its tab.
/// Saves the measure, or the variable (<see cref="CalculatedMeasureEditor.ForVariable"/>), in its context on OK.
/// </summary>
internal partial class CalculatedMeasureWindow : Window
{
    private readonly CalculatedMeasureEditor _editor;
    // Set while the formula changes by an insertion, which does not ask for suggestions
    private bool _inserting;

    public CalculatedMeasureWindow(CalculatedMeasureEditor editor)
    {
        InitializeComponent();
        DataContext = _editor = editor;
        editor.PropertyChanged += OnEditorChanged;

        var dimensions = editor.InsertableDimensions.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var highlighter = new FormulaHighlighter(FormulaBox, caption => dimensions.Contains("[" + caption.Replace("]", "]]") + "]"));
        highlighter.SetBinding(FormulaHighlighter.ProblemProperty, new Binding(nameof(CalculatedMeasureEditor.Problem)));
        FormulaHost.Children.Insert(1, highlighter);

        FormulaBox.TextChanged += OnFormulaTyped;
        FormulaBox.SelectionChanged += (_, _) => _editor.CaretIndex = FormulaBox.CaretIndex;
        FormulaBox.PreviewKeyDown += OnFormulaKeyDown;
        FormulaBox.LostKeyboardFocus += (_, _) => CloseSuggestions();
        FormulaBox.PreviewMouseDown += (_, _) => CloseSuggestions();

        if (editor.IsVariable)
        {
            Title = Strings.VariableEditorTitle;
            CaptionLabel.Text = Strings.VariableName;
            FormulaHint.Text = Strings.VariableFormulaHint;
            FormulaPlaceholder.Text = Strings.VariableFormulaPlaceholder;
        }
        ResetButton.Visibility = editor.CanReset ? Visibility.Visible : Visibility.Collapsed;
        if (editor.DeclaredFormula is { } declared) RedefinitionHint.Text = Strings.Format(Strings.MeasureRedefinitionHint, declared);
        else RedefinitionBanner.Visibility = Visibility.Collapsed;
        ShowStatus();
        Loaded += (_, _) =>
        {
            if (editor.IsNew) CaptionBox.Focus();
            else
            {
                FormulaBox.Focus();
                FormulaBox.CaretIndex = FormulaBox.Text.Length;
            }
        };
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CalculatedMeasureEditor.Error) or nameof(CalculatedMeasureEditor.Preview):
                ShowStatus();
                break;
            case nameof(CalculatedMeasureEditor.Suggestions):
                ShowSuggestions();
                break;
        }
    }

    private void ShowStatus()
    {
        var problem = _editor.Error;
        StatusIcon.Text = problem is null ? "✓" : "!";
        Status.Text = problem ?? (_editor.Preview is { } total
            ? Strings.Format(_editor.IsVariable ? Strings.VariablePreview : Strings.MeasurePreview, total)
            : Strings.FormulaValid);
        var brush = problem is null ? EasyBITheme.SuccessBrushKey : EasyBITheme.ErrorBrushKey;
        StatusIcon.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Status.SetResourceReference(TextBlock.ForegroundProperty, problem is null ? EasyBITheme.ForegroundBrushKey : EasyBITheme.ErrorBrushKey);
    }

    // --- Inserting

    private void Insert(FormulaItem item)
    {
        _inserting = true;
        try
        {
            var caret = _editor.Insert(item, FormulaBox.SelectionStart, FormulaBox.SelectionLength);
            FormulaBox.Focus();
            FormulaBox.Select(caret, 0);
        }
        finally
        {
            _inserting = false;
        }
    }

    private void OnInsertClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FormulaItem item }) Insert(item);
    }

    private void OnCatalogDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: FormulaItem item }) Insert(item);
    }

    private void OnCatalogKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _editor.SelectedItem is not { } item) return;
        Insert(item);
        e.Handled = true;
    }

    // Down arrow from the search goes to the results of the open tab
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || Descendant<ListBox>(CatalogTabs) is not { Items.Count: > 0 } list) return;
        list.SelectedIndex = Math.Max(list.SelectedIndex, 0);
        (list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) as UIElement)?.Focus();
        e.Handled = true;
    }

    private static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (Descendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    // --- Suggestions

    private void OnFormulaTyped(object sender, TextChangedEventArgs e)
    {
        if (_inserting) return;
        _editor.Formula = FormulaBox.Text;
        _editor.Suggest(FormulaBox.CaretIndex);
    }

    private void OnFormulaKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _editor.Suggest(FormulaBox.CaretIndex, all: true);
            e.Handled = true;
            return;
        }
        if (!SuggestionPopup.IsOpen) return;
        switch (e.Key)
        {
            case Key.Down or Key.Up:
                var count = SuggestionList.Items.Count;
                SuggestionList.SelectedIndex = (SuggestionList.SelectedIndex + (e.Key == Key.Down ? 1 : count - 1)) % count;
                SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                break;
            case Key.Tab or Key.Enter when SuggestionList.SelectedItem is FormulaItem item:
                Accept(item);
                break;
            case Key.Escape:
                CloseSuggestions();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnSuggestionClicked(object sender, MouseButtonEventArgs e)
    {
        // Handled on the press, so that the formula keeps the keyboard
        if (e.OriginalSource is not FrameworkElement { DataContext: FormulaItem item }) return;
        Accept(item);
        e.Handled = true;
    }

    private void Accept(FormulaItem item)
    {
        _inserting = true;
        try
        {
            var caret = _editor.Accept(item);
            FormulaBox.Focus();
            FormulaBox.Select(caret, 0);
        }
        finally
        {
            _inserting = false;
        }
    }

    // Under what is being typed
    private void ShowSuggestions()
    {
        if (_editor.Suggestions.Count == 0 || _editor.Completion is not { } completion || !FormulaBox.IsKeyboardFocused)
        {
            SuggestionPopup.IsOpen = false;
            return;
        }
        var at = FormulaBox.GetRectFromCharacterIndex(Math.Min(completion.Start, FormulaBox.Text.Length));
        if (!at.IsEmpty)
        {
            SuggestionPopup.HorizontalOffset = at.Left;
            SuggestionPopup.VerticalOffset = at.Bottom + 2;
        }
        SuggestionList.SelectedIndex = 0;
        SuggestionPopup.IsOpen = true;
    }

    private void CloseSuggestions()
    {
        if (SuggestionPopup.IsOpen) _editor.CloseSuggestions();
    }

    // --- Saving

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _editor.Reset();
        DialogResult = true;
    }

    /// <summary>Opens the editor of a variable (a new one when <paramref name="variable"/> is null) and saves it on OK.</summary>
    /// <returns>The variable saved, or null when the user cancelled.</returns>
    public static ReportVariable? EditVariable(EasyBIContext context, ISemanticModel model, Window? owner, ReportVariable? variable = null)
    {
        var editor = CalculatedMeasureEditor.ForVariable(context, model, variable);
        return new CalculatedMeasureWindow(editor) { Owner = owner }.ShowDialog() == true ? editor.VariableResult : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!_editor.Save()) return;
        DialogResult = true;
    }
}
