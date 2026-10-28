namespace EasyBI.Wpf;

/// <summary>
/// Names of the <see cref="Slot"/>s of a <see cref="FieldList"/>. Their content takes the data context of the field
/// list (the <see cref="Designer.DesignerViewModel"/> in a designer, the application's elsewhere). The sections are
/// vertical: before is above them, after below them.
/// </summary>
public static class FieldListSlots
{
    /// <summary>The "Model" header and the model picker.</summary>
    public const string Model = "FieldList.Model";

    /// <summary>The box searching the fields by caption.</summary>
    public const string Search = "FieldList.Search";

    /// <summary>The "Dimensions" header and the list of dimensions.</summary>
    public const string Dimensions = "FieldList.Dimensions";

    /// <summary>The "Measures" header and the list of measures.</summary>
    public const string Measures = "FieldList.Measures";

    /// <summary>The button adding a calculated measure.</summary>
    public const string AddMeasureButton = "FieldList.AddMeasureButton";

    /// <summary>The "Variables" header, the list of the report's variables and the buttons adding one.</summary>
    public const string Variables = "FieldList.Variables";
}
