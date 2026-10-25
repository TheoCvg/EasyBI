using System.Text.Json;
using System.Text.Json.Nodes;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// Values of the <see cref="RendererOption"/>s of a visual. They are stored in <see cref="VisualDefinition.Extensions"/>:
/// one JSON object per renderer, under the renderer's name (<c>"LiveCharts2": "{\"scrollable\":true}"</c>), so that
/// the report format does not change and the settings of a renderer are kept while another one draws the visual.
/// </summary>
public static class RendererSettings
{
    /// <summary>The value of an option, or <paramref name="default"/> when it is not set or not a <typeparamref name="T"/>.</summary>
    public static T? GetRendererOption<T>(this VisualDefinition visual, string renderer, string key, T? @default = default)
    {
        if (Read(visual.Extensions, renderer)?[key] is not JsonValue value) return @default;
        try
        {
            return value.Deserialize<T>() is { } result ? result : @default;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            return @default;
        }
    }

    /// <summary>The value of <paramref name="option"/>, or its <see cref="RendererOption.Default"/> when it is not set.</summary>
    public static object? GetRendererOption(this VisualDefinition visual, string renderer, RendererOption option) => option.Kind switch
    {
        RendererOptionKind.Toggle => visual.GetRendererOption(renderer, option.Key, option.Default as bool? ?? false),
        RendererOptionKind.Number => visual.GetRendererOption(renderer, option.Key, Convert.ToDouble(option.Default ?? 0d)),
        _ => visual.GetRendererOption(renderer, option.Key, option.Default as string),
    };

    /// <summary>The value of a setting of the visual's type (<see cref="VisualTypeDefinition.Options"/>), saved under its id.</summary>
    public static T? GetTypeOption<T>(this VisualDefinition visual, string key, T? @default = default) =>
        visual.GetRendererOption(visual.Type.Id, key, @default);

    /// <summary>The visual with the option set; a null value removes it, and with it the renderer's entry once empty.</summary>
    public static VisualDefinition WithRendererOption(this VisualDefinition visual, string renderer, string key, object? value) =>
        visual with { Extensions = WithRendererOption(visual.Extensions, renderer, key, value) };

    /// <inheritdoc cref="WithRendererOption(VisualDefinition, string, string, object?)"/>
    public static IReadOnlyDictionary<string, string>? WithRendererOption(IReadOnlyDictionary<string, string>? extensions,
        string renderer, string key, object? value)
    {
        var settings = Read(extensions, renderer) ?? [];
        if (value is null) settings.Remove(key);
        else settings[key] = value switch
        {
            bool b => JsonValue.Create(b),
            string s => JsonValue.Create(s),
            IConvertible number => JsonValue.Create(number.ToDouble(System.Globalization.CultureInfo.InvariantCulture)),
            _ => throw new ArgumentException($"Unsupported option value type '{value.GetType()}': use a bool, a string or a number.", nameof(value)),
        };

        var result = extensions?.ToDictionary() ?? [];
        if (settings.Count == 0) result.Remove(renderer);
        else result[renderer] = settings.ToJsonString();
        return result.Count == 0 ? null : result;
    }

    // An entry that is not a JSON object (written by hand, or by an older connector) counts as no settings
    private static JsonObject? Read(IReadOnlyDictionary<string, string>? extensions, string renderer)
    {
        if (extensions is null || !extensions.TryGetValue(renderer, out var json)) return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
