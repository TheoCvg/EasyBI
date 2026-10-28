using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Sample;

/// <summary>
/// The sales of the demo's server (samples/EasyBI.Sample.Server), reached over HTTP: each visual sends its request and
/// draws the result it receives, the sales never leave the server. EasyBI gives the JSON (<see cref="DataSerializer"/>);
/// the routes and the transport are the application's.
/// </summary>
public sealed class RemoteModel : ISemanticModel
{
    public static readonly Uri DefaultServer = new("http://localhost:5180/");

    private readonly HttpClient _http;
    private readonly IReadOnlyDictionary<string, ModelMetadata> _metadata;

    private RemoteModel(HttpClient http, IReadOnlyDictionary<string, ModelMetadata> metadata) =>
        (_http, _metadata) = (http, metadata);

    /// <summary>
    /// Reads the model's metadata in each language of the application up front: <see cref="Metadata"/> is read
    /// synchronously, and its captions follow the language when the user switches it.
    /// </summary>
    public static async Task<RemoteModel> ConnectAsync(Uri server, IEnumerable<CultureInfo> languages, CancellationToken cancellationToken = default)
    {
        var http = new HttpClient { BaseAddress = server, Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            var metadata = new Dictionary<string, ModelMetadata>();
            foreach (var language in languages)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "sales/metadata");
                request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language.Name));
                using var response = await http.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                metadata[language.Name] = DataSerializer.DeserializeMetadata(await response.Content.ReadAsStringAsync(cancellationToken));
            }
            return new RemoteModel(http, metadata);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    public ModelMetadata Metadata =>
        _metadata.TryGetValue(EasyBILocalization.UICulture.Name, out var metadata) ? metadata : _metadata.Values.First();

    public async Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(DataSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("sales/data", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return DataSerializer.DeserializeData(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task<DistinctValues> GetDistinctValuesAsync(DistinctValuesRequest request, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(DataSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("sales/values", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        // The JSON tells a number from a text, the field's type a date from a text
        return DataSerializer.DeserializeValues(await response.Content.ReadAsStringAsync(cancellationToken), Metadata.GetField(request.Field).DataType);
    }
}
