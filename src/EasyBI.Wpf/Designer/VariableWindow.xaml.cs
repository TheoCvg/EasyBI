using System.Globalization;
using System.Windows;
using System.Windows.Data;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Built-in dialog of a <see cref="VariableEditor"/>: creates or edits a parameter or a set of values of the report,
/// saved in the context's <see cref="EasyBIContext.Variables"/> on OK.
/// </summary>
public partial class VariableWindow : Window
{
    /// <summary>Negates a bool (a read-only name once the variable exists).</summary>
    public static IValueConverter Not { get; } = new NotConverter();

    public VariableWindow(VariableEditor editor)
    {
        InitializeComponent();
        DataContext = Editor = editor;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public VariableEditor Editor { get; }

    /// <summary>The variable saved on OK, or null.</summary>
    public ReportVariable? Saved { get; private set; }

    /// <summary>Creates a variable with the dialog; returns it, or null when cancelled.</summary>
    public static ReportVariable? Create(EasyBIContext context, Window? owner, VariableKind kind = VariableKind.Parameter, ISemanticModel? model = null) =>
        Show(new VariableEditor(context, null, kind, model), owner);

    /// <summary>Edits a variable with the dialog; returns it as saved, or null when cancelled or missing.</summary>
    public static ReportVariable? Edit(EasyBIContext context, string name, Window? owner) =>
        context.Variables.Find(name) is { } variable ? Show(new VariableEditor(context, variable), owner) : null;

    private static ReportVariable? Show(VariableEditor editor, Window? owner)
    {
        var window = new VariableWindow(editor) { Owner = owner };
        return window.ShowDialog() == true ? window.Saved : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Editor.Save() is not { } saved) return;
        Saved = saved;
        DialogResult = true;
    }

    private sealed class NotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    }
}
