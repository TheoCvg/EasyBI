using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>Report exchange format: readable JSON, independent of the renderer.</summary>
public static class ReportSerializer
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        // Reports without calculated measures or variables stay as they were before those existed
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitEmptyLists } },
    };

    private static void OmitEmptyLists(JsonTypeInfo type)
    {
        if (type.Type != typeof(ReportDefinition)) return;
        foreach (var property in type.Properties.Where(p => p.Name is "measures" or "variables"))
            property.ShouldSerialize = (_, value) => value is System.Collections.ICollection { Count: > 0 };
    }

    public static string Serialize(ReportDefinition report) => JsonSerializer.Serialize(report, Options);

    public static ReportDefinition Deserialize(string json)
    {
        var report = JsonSerializer.Deserialize<ReportDefinition>(json, Options)
            ?? throw new JsonException("Empty report.");
        if (report.Schema != ReportDefinition.CurrentSchema)
            throw new NotSupportedException($"Unsupported schema '{report.Schema}' (expected: {ReportDefinition.CurrentSchema}).");
        return report with { Visuals = [.. report.Visuals.Select(WithTimeSortKey)] };
    }

    // Keys of time columns written before time calculations were made of catalogs (Amount.PreviousYear): a sort on one
    // refers to the same column under its key of today
    private static readonly (string Old, MeasureTime Time)[] LegacyTimeKeys =
    [
        (".PreviousYear", MeasureTime.PreviousYear),
        (".ChangeFromPreviousYear", MeasureTime.ChangeFromPreviousYear),
        (".PercentChangeFromPreviousYear", MeasureTime.PercentChangeFromPreviousYear),
        (".YearToDate", MeasureTime.YearToDate),
    ];

    private static VisualDefinition WithTimeSortKey(VisualDefinition visual) =>
        visual.Sort is { } sort && LegacyTimeKeys.FirstOrDefault(k => sort.Key.EndsWith(k.Old, StringComparison.Ordinal)) is { Time: not null } legacy
            ? visual with { Sort = sort with { Key = sort.Key.Substring(0, sort.Key.Length - legacy.Old.Length) + "." + legacy.Time.Key } }
            : visual;
}
