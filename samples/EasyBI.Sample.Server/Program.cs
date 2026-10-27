using System.Text.Json;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Sample.Sales;

// The demo's sales behind HTTP: the data stays here, applications receive the results of their visuals. EasyBI only
// gives the JSON (DataSerializer); the routes, the transport and the security are the application's choice.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRequestLocalization(options => options
    .SetDefaultCulture("en-US")
    .AddSupportedCultures("en-US", "fr-FR")
    .AddSupportedUICultures("en-US", "fr-FR"));
var app = builder.Build();
// Captions of the metadata follow the Accept-Language of the request
app.UseRequestLocalization();

var sales = SalesData.CreateModel(SalesData.Generate());

app.MapGet("/sales/metadata", () => Results.Json(sales.Metadata, DataSerializer.Options));

// A visual's request holds the visual, the user's calculated measures, their day and their languages
app.MapPost("/sales/data", async (HttpRequest http, CancellationToken cancellationToken) =>
{
    VisualDataRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<VisualDataRequest>(http.Body, DataSerializer.Options, cancellationToken);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    if (request is null) return Results.BadRequest();
    return Results.Json(await sales.GetDataAsync(request, cancellationToken), DataSerializer.Options);
});

// The values of a dimension listed by a filter, or those containing what the user searches
app.MapPost("/sales/values", async (HttpRequest http, CancellationToken cancellationToken) =>
{
    DistinctValuesRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<DistinctValuesRequest>(http.Body, DataSerializer.Options, cancellationToken);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    if (request is null) return Results.BadRequest();
    return Results.Json(await sales.GetDistinctValuesAsync(request, cancellationToken), DataSerializer.Options);
});

app.Run(args.Length > 0 ? args[0] : "http://localhost:5180");
