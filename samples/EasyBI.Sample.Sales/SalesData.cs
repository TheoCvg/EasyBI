using System.Resources;
using EasyBI.Core;

namespace EasyBI.Sample.Sales;

public sealed record Product(string Name, string Category, decimal UnitPrice);

public sealed record Sale(DateTime Date, string Region, string Channel, Product Product, int Quantity, decimal Amount, decimal Margin)
{
    public string Customer { get; init; } = "";
}

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

    private static readonly string[] FirstNames =
    [
        "Alice", "Antoine", "Camille", "Chloé", "Clément", "Emma", "Étienne", "Fanny", "Gabriel", "Hugo",
        "Inès", "Jade", "Jules", "Julie", "Léa", "Léo", "Lina", "Louis", "Lucas", "Manon",
        "Marie", "Mathis", "Maxime", "Mila", "Nathan", "Noé", "Océane", "Paul", "Pierre", "Rose",
        "Sacha", "Sarah", "Simon", "Sophie", "Théo", "Thomas", "Tom", "Valentin", "Victor", "Zoé",
        "Adam", "Anna", "Arthur", "Clara", "Eva", "Lola", "Louise", "Malo", "Nina", "Raphaël",
    ];

    private static readonly string[] LastNames =
    [
        "Martin", "Bernard", "Thomas", "Petit", "Robert", "Richard", "Durand", "Dubois", "Moreau", "Laurent",
        "Simon", "Michel", "Lefebvre", "Leroy", "Roux", "David", "Bertrand", "Morel", "Fournier", "Girard",
        "Bonnet", "Dupont", "Lambert", "Fontaine", "Rousseau", "Vincent", "Muller", "Lefèvre", "Faure", "André",
        "Mercier", "Blanc", "Guérin", "Boyer", "Garnier", "Chevalier", "François", "Legrand", "Gauthier", "Garcia",
        "Perrin", "Robin", "Clément", "Morin", "Nicolas", "Henry", "Roussel", "Mathieu", "Gautier", "Masson",
    ];

    /// <summary>The demo's customers, thousands of them: a dimension with a long list of values to filter.</summary>
    public static IReadOnlyList<string> Customers { get; } =
        [.. LastNames.SelectMany(last => FirstNames.Select(first => $"{first} {last}"))];

    public static IReadOnlyList<Sale> Generate(int count = 20_000)
    {
        var random = new Random(42);
        var start = new DateTime(2023, 1, 1);
        var days = (new DateTime(2025, 12, 31) - start).Days;
        var sales = new List<Sale>(count);
        // Drawn apart, so that the other values stay those of the demo before customers
        var customers = new Random(7);
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
            sales.Add(new Sale(date, region, channel, product, quantity, amount, margin) { Customer = Customers[customers.Next(Customers.Count)] });
        }
        return sales;
    }

    /// <summary>Captions of the demo's fields in its languages: the captions given to the models are their keys.</summary>
    public static ResourceManager Captions { get; } = new("EasyBI.Sample.Sales.Resources.Captions", typeof(SalesData).Assembly);

    public static QueryableSemanticModel<Sale> CreateModel(IEnumerable<Sale> sales) =>
        SemanticModel.For(sales, "sales", "Sales")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Region, "Region")
            .Dimension(s => s.Channel, "Channel")
            .Dimension(s => s.Product.Category, "Category")
            .Dimension(s => s.Product.Name, "Product")
            .Dimension(s => s.Customer, "Customer", "Customer")
            .Measure(s => s.Amount, "Revenue", format: "#,##0 €")
            .Measure(s => s.Margin, "Margin", format: "#,##0 €")
            .Measure(s => s.Quantity, "Quantity", format: "N0")
            .Count("SalesCount")
            // Ratios of sums per group: the margin rate of a region is not the average of its sales' rates
            .Ratio(s => s.Margin, s => s.Amount, "MarginRate", "MarginRate", format: "P1")
            .Ratio(s => s.Amount, s => 1, "AverageBasket", "AverageBasket", format: "#,##0 €")
            // A formula, as end users write them: they can rewrite it in the application
            .Formula("SUM([Amount]; [Channel] = \"Web\") / [Amount]", "WebShare", "WebShare", format: "P1")
            .Localize(Captions);
}
