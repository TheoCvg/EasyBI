using System.Text.Json;
using System.Text.Json.Serialization;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>Format d'échange des rapports : du JSON lisible, indépendant du renderer.</summary>
public static class ReportSerializer
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ReportDefinition report) => JsonSerializer.Serialize(report, Options);

    public static ReportDefinition Deserialize(string json)
    {
        var report = JsonSerializer.Deserialize<ReportDefinition>(json, Options)
            ?? throw new JsonException("Rapport vide.");
        if (report.Schema != ReportDefinition.CurrentSchema)
            throw new NotSupportedException($"Schéma '{report.Schema}' non supporté (attendu : {ReportDefinition.CurrentSchema}).");
        return report;
    }
}
