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
        // Reports without calculated measures stay as they were before measures existed
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitEmptyMeasures } },
    };

    private static void OmitEmptyMeasures(JsonTypeInfo type)
    {
        if (type.Type != typeof(ReportDefinition)) return;
        foreach (var property in type.Properties.Where(p => p.Name == "measures"))
            property.ShouldSerialize = (_, value) => value is IReadOnlyCollection<CalculatedMeasure> { Count: > 0 };
    }

    public static string Serialize(ReportDefinition report) => JsonSerializer.Serialize(report, Options);

    public static ReportDefinition Deserialize(string json)
    {
        var report = JsonSerializer.Deserialize<ReportDefinition>(json, Options)
            ?? throw new JsonException("Empty report.");
        if (report.Schema != ReportDefinition.CurrentSchema)
            throw new NotSupportedException($"Unsupported schema '{report.Schema}' (expected: {ReportDefinition.CurrentSchema}).");
        return report;
    }
}
