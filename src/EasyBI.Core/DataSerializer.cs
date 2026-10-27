using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// Transport-neutral JSON of what travels between an application and a data source it reaches remotely: the
/// <see cref="VisualDataRequest"/> of a visual, its <see cref="VisualData"/>, a model's <see cref="ModelMetadata"/> and
/// the distinct values of a dimension. How the JSON travels (HTTP, gRPC, a queue...) is the application's business.
/// Values keep their type through the trip: dates, date periods, numbers, texts and booleans.
/// </summary>
public static class DataSerializer
{
    /// <summary>
    /// The options of this format, to read and write it with <see cref="JsonSerializer"/> directly (a stream, an
    /// <c>HttpContent</c>...): <see cref="VisualDataRequest"/>, <see cref="VisualData"/> and <see cref="ModelMetadata"/>.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // An empty ratio can be NaN: it travels as "NaN" instead of failing
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(), new ResultSetConverter(), new ValueConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { WriteTodayAsDate } },
    };

    // The user's today is a day, not an instant: written with its offset, it could become yesterday in another time zone
    private static void WriteTodayAsDate(JsonTypeInfo type)
    {
        if (type.Type != typeof(VisualDataRequest)) return;
        foreach (var property in type.Properties.Where(p => p.Name == "today"))
            property.CustomConverter = new DayConverter();
    }

    public static string Serialize(VisualDataRequest request) => JsonSerializer.Serialize(request, Options);

    public static VisualDataRequest DeserializeRequest(string json) =>
        JsonSerializer.Deserialize<VisualDataRequest>(json, Options) ?? throw new JsonException("Empty request.");

    public static string Serialize(VisualData data) => JsonSerializer.Serialize(data, Options);

    public static VisualData DeserializeData(string json) =>
        JsonSerializer.Deserialize<VisualData>(json, Options) ?? throw new JsonException("Empty data.");

    public static string Serialize(ModelMetadata metadata) => JsonSerializer.Serialize(metadata, Options);

    public static ModelMetadata DeserializeMetadata(string json) =>
        JsonSerializer.Deserialize<ModelMetadata>(json, Options) ?? throw new JsonException("Empty metadata.");

    /// <summary>What a filter asks of a dimension (<see cref="ISemanticModel.GetDistinctValuesAsync"/>).</summary>
    public static string Serialize(DistinctValuesRequest request) => JsonSerializer.Serialize(request, Options);

    public static DistinctValuesRequest DeserializeValuesRequest(string json) =>
        JsonSerializer.Deserialize<DistinctValuesRequest>(json, Options) ?? throw new JsonException("Empty request.");

    /// <summary>Distinct values of a dimension, as <c>{"values":[...],"isTruncated":false}</c>.</summary>
    public static string Serialize(DistinctValues values) => JsonSerializer.Serialize(values, Options);

    /// <summary>Reads distinct values written by <see cref="Serialize(DistinctValues)"/>, of a dimension of type <paramref name="dataType"/>.</summary>
    public static DistinctValues DeserializeValues(string json, DataType dataType)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array)
            throw new JsonException("Distinct values are written as {\"values\":[...]}.");
        var truncated = document.RootElement.TryGetProperty("isTruncated", out var flag) && flag.ValueKind == JsonValueKind.True;
        return new DistinctValues([.. values.EnumerateArray().Select(v => ReadValue(v, dataType))], truncated);
    }

    /// <summary>
    /// Writes a value of a result: a date as yyyy-MM-dd (with its time in ISO 8601 when it has one), a
    /// <see cref="DatePeriod"/> as <c>{"grain":"Month","key":202403}</c>, an enum as its name.
    /// </summary>
    private static void WriteValue(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text: writer.WriteStringValue(text); break;
            case bool flag: writer.WriteBooleanValue(flag); break;
            case DateOnly or DateTime or DateTimeOffset: writer.WriteStringValue(InvariantValue.Format(value)); break;
            case DatePeriod period:
                writer.WriteStartObject();
                writer.WriteString("grain", period.Grain.ToString());
                writer.WriteNumber("key", period.Key);
                writer.WriteEndObject();
                break;
            case Enum: writer.WriteStringValue(value.ToString()); break;
            case int or short or byte or sbyte or ushort: writer.WriteNumberValue(Convert.ToInt32(value, CultureInfo.InvariantCulture)); break;
            case long or uint: writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture)); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case double or float:
                var real = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(real) || double.IsInfinity(real)) writer.WriteStringValue(real.ToString(CultureInfo.InvariantCulture));
                else writer.WriteNumberValue(real);
                break;
            // Anything else serialized as an object (a list of values, a result): as its own type
            default:
                if (value.GetType() == typeof(object)) writer.WriteStringValue(InvariantValue.Format(value));
                else JsonSerializer.Serialize(writer, value, value.GetType(), options);
                break;
        }
    }

    /// <summary>
    /// Reads a value written by <see cref="WriteValue"/>. The column's type tells a date from a text; without it (the
    /// members of a matrix, which come with their labels) a date stays its invariant text, which filters the same way.
    /// </summary>
    private static object? ReadValue(JsonElement element, DataType? dataType) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.TryGetInt32(out var integer) ? (object)integer
            : element.TryGetInt64(out var large) ? (object)large
            : element.GetDouble(),
        JsonValueKind.String => ReadText(element.GetString()!, dataType),
        JsonValueKind.Object => new DatePeriod(
            (DateGrain)Enum.Parse(typeof(DateGrain), element.GetProperty("grain").GetString()!, ignoreCase: true),
            element.GetProperty("key").GetInt32()),
        _ => throw new JsonException($"Unexpected value: {element.GetRawText()}."),
    };

    private static object ReadText(string text, DataType? dataType) => dataType switch
    {
        DataType.Date when text.Length == 10 => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        DataType.Date => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DataType.Number => double.Parse(text, CultureInfo.InvariantCulture), // NaN, Infinity
        _ => text,
    };

    /// <summary>A result as its columns and its rows of values: <c>{"columns":[...],"rows":[["North",1250.5],...]}</c>.</summary>
    private sealed class ResultSetConverter : JsonConverter<ResultSet>
    {
        public override ResultSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var columns = root.TryGetProperty("columns", out var c)
                ? c.Deserialize<ResultColumn[]>(options) ?? []
                : [];
            var rows = new List<object?[]>();
            if (root.TryGetProperty("rows", out var r))
            {
                foreach (var row in r.EnumerateArray())
                {
                    var values = new object?[columns.Length];
                    var i = 0;
                    foreach (var value in row.EnumerateArray())
                    {
                        if (i == columns.Length) throw new JsonException("A row has more values than the result has columns.");
                        values[i] = ReadValue(value, columns[i].DataType);
                        i++;
                    }
                    rows.Add(values);
                }
            }
            return new ResultSet(columns, rows);
        }

        public override void Write(Utf8JsonWriter writer, ResultSet value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("columns");
            JsonSerializer.Serialize(writer, value.Columns, options);
            writer.WriteStartArray("rows");
            foreach (var row in value.Rows)
            {
                writer.WriteStartArray();
                foreach (var cell in row) WriteValue(writer, cell, options);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// The values typed <see cref="object"/> outside a result: the members of a matrix, distinct values. A web framework
    /// may also hand it a whole response typed object, written as its own type.
    /// </summary>
    private sealed class ValueConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return ReadValue(document.RootElement, null);
        }

        public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options) => WriteValue(writer, value, options);
    }

    private sealed class DayConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString() ?? throw new JsonException("A day is written as yyyy-MM-dd.");
            // Only the date counts: a full date and time from another writer keeps its own day
            return DateTime.ParseExact(text.Length > 10 ? text.Substring(0, 10) : text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
