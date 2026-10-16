using System.Resources;
using EasyBI.Core;

namespace EasyBI.Sample;

public sealed record Product(string Name, string Category, decimal UnitPrice);

public sealed record Sale(DateTime Date, string Region, string Channel, Product Product, int Quantity, decimal Amount, decimal Margin);

/// <summary>Fake dataset and the semantic model the developer exposes.</summary>
public static class SalesData
{
    private static readonly Product[] Products =
    [
        new("Vélo route", "Vélos", 1200m), new("VTT", "Vélos", 900m), new("Vélo cargo", "Vélos", 2400m),
        new("Casque", "Équipement", 70m), new("Antivol", "Équipement", 45m), new("Gants", "Équipement", 25m),
        new("Révision", "Services", 90m), new("Location", "Services", 35m),
    ];

    private static readonly string[] Regions = ["Île-de-France", "Nord", "Sud", "Est", "Ouest"];
    private static readonly string[] Channels = ["Magasin", "Web", "Partenaires"];

    public static IReadOnlyList<Sale> Generate(int count = 20_000)
    {
        var random = new Random(42);
        var start = new DateTime(2023, 1, 1);
        var days = (new DateTime(2025, 12, 31) - start).Days;
        var sales = new List<Sale>(count);
        for (var i = 0; i < count; i++)
        {
            var date = start.AddDays(random.Next(days));
            var region = Regions[(int)Math.Floor(Math.Pow(random.NextDouble(), 1.6) * Regions.Length)];
            var channel = Channels[random.Next(10) switch { < 5 => 0, < 8 => 1, _ => 2 }];
            var product = Products[random.Next(Products.Length)];
            var quantity = 1 + random.Next(product.Category == "Vélos" ? 2 : 5);
            // Yearly growth + summer seasonality
            var trend = 1 + (date.Year - 2023) * 0.12 + Math.Sin((date.Month - 3) / 12.0 * Math.PI * 2) * 0.15;
            var amount = Math.Round(product.UnitPrice * quantity * (decimal)trend, 2);
            var margin = Math.Round(amount * (product.Category == "Services" ? 0.55m : 0.28m), 2);
            sales.Add(new Sale(date, region, channel, product, quantity, amount, margin));
        }
        return sales;
    }

    /// <summary>Captions are resource keys, translated through <paramref name="captions"/>.</summary>
    public static QueryableSemanticModel<Sale> CreateModel(IEnumerable<Sale> sales, ResourceManager captions) =>
        SemanticModel.For(sales, "sales", "Sales")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Region, "Region")
            .Dimension(s => s.Channel, "Channel")
            .Dimension(s => s.Product.Category, "Category")
            .Dimension(s => s.Product.Name, "Product")
            .Measure(s => s.Amount, "Revenue", format: "#,##0 €")
            .Measure(s => s.Margin, "Margin", format: "#,##0 €")
            .Measure(s => s.Quantity, "Quantity", format: "N0")
            .Count("SalesCount")
            .Localize(captions);
}
