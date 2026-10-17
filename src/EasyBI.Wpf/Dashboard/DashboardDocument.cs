using System.Text.Json;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// What a <see cref="DashboardView"/> saves: the report (the visuals and filters, unchanged <c>easybi/v1</c> format)
/// next to the WPF arrangement of its visuals.
/// </summary>
public sealed record DashboardDocument
{
    public const string CurrentSchema = "easybi-dashboard/v1";

    public string Schema { get; init; } = CurrentSchema;

    public ReportDefinition Report { get; init; } = new();

    /// <summary>Arrangement of the visuals; null lets the dashboard place them.</summary>
    public DashboardLayoutDefinition? Layout { get; init; }
}

/// <summary>JSON of a <see cref="DashboardDocument"/>, with the same conventions as <see cref="ReportSerializer"/>.</summary>
public static class DashboardSerializer
{
    public static string Serialize(DashboardDocument document) => JsonSerializer.Serialize(document, ReportSerializer.Options);

    /// <summary>Reads a dashboard document, or a plain report (which then gets a default layout).</summary>
    public static DashboardDocument Deserialize(string json)
    {
        using (var parsed = JsonDocument.Parse(json))
        {
            if (parsed.RootElement.ValueKind == JsonValueKind.Object
                && parsed.RootElement.TryGetProperty("schema", out var schema)
                && schema.GetString() == ReportDefinition.CurrentSchema)
                return new DashboardDocument { Report = ReportSerializer.Deserialize(json) };
        }

        var document = JsonSerializer.Deserialize<DashboardDocument>(json, ReportSerializer.Options)
            ?? throw new JsonException("Empty dashboard.");
        if (document.Schema != DashboardDocument.CurrentSchema)
            throw new NotSupportedException($"Unsupported schema '{document.Schema}' (expected: {DashboardDocument.CurrentSchema}).");
        // The report keeps its own versioning
        return document with { Report = ReportSerializer.Deserialize(ReportSerializer.Serialize(document.Report)) };
    }
}
