using EasyBI.Core;
using Microsoft.EntityFrameworkCore;

namespace EasyBI.Sample.Sales;

public sealed class ProductRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public double UnitPrice { get; set; }
}

/// <summary>A sale stored in the database (SQLite cannot sum decimals: amounts are doubles).</summary>
public sealed class SaleRow
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Region { get; set; } = "";
    public string Channel { get; set; } = "";
    public int ProductId { get; set; }
    public ProductRow Product { get; set; } = null!;
    public int Quantity { get; set; }
    public double Amount { get; set; }
    public double Margin { get; set; }
}

public sealed class SalesDbContext(DbContextOptions<SalesDbContext> options) : DbContext(options)
{
    public DbSet<SaleRow> Sales => Set<SaleRow>();
    public DbSet<ProductRow> Products => Set<ProductRow>();
}

/// <summary>
/// The same sales as <see cref="SalesData"/>, in a SQLite database of millions of rows: EF Core translates every
/// visual into one SQL query, so only aggregated rows reach the application, whatever the volume.
/// </summary>
public static class SalesDatabase
{
    private static string PathFor(int rows) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyBI.Sample", $"sales-{rows}.db");

    private static DbContextOptions<SalesDbContext> Options(int rows) =>
        new DbContextOptionsBuilder<SalesDbContext>().UseSqlite($"Data Source={PathFor(rows)}").Options;

    public static bool Exists(int rows) => File.Exists(PathFor(rows));

    /// <summary>Generates the database once (in SQL, a few seconds per million rows) and reuses it afterwards.</summary>
    public static async Task EnsureCreatedAsync(int rows, CancellationToken cancellationToken = default)
    {
        if (Exists(rows)) return;
        var path = PathFor(rows);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var building = path + ".tmp";
        File.Delete(building);
        var options = new DbContextOptionsBuilder<SalesDbContext>().UseSqlite($"Data Source={building};Pooling=False").Options;
        await using (var db = new SalesDbContext(options))
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Products (Name, Category, UnitPrice) VALUES
                ('Vélo route', 'Vélos', 1200), ('VTT', 'Vélos', 900), ('Vélo cargo', 'Vélos', 2400),
                ('Casque', 'Équipement', 70), ('Antivol', 'Équipement', 45), ('Gants', 'Équipement', 25),
                ('Révision', 'Services', 90), ('Location', 'Services', 35)
                """, cancellationToken);
            // Same shape as the in-memory data: regions and channels unevenly spread, yearly growth, a summer peak
            await db.Database.ExecuteSqlAsync($"""
                WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < {rows}),
                draws AS (
                    SELECT date('2023-01-01', '+' || (abs(random()) % 1095) || ' days') AS d,
                           abs(random()) % 100 AS r, abs(random()) % 10 AS c, 1 + abs(random()) % 8 AS p, abs(random()) % 5 AS q
                    FROM n)
                INSERT INTO Sales (Date, Region, Channel, ProductId, Quantity, Amount, Margin)
                SELECT d || ' 00:00:00',
                       CASE WHEN r < 35 THEN 'Île-de-France' WHEN r < 55 THEN 'Nord' WHEN r < 72 THEN 'Sud' WHEN r < 87 THEN 'Est' ELSE 'Ouest' END,
                       CASE WHEN c < 5 THEN 'Magasin' WHEN c < 8 THEN 'Web' ELSE 'Partenaires' END,
                       p, quantity, round(amount, 2), round(amount * CASE WHEN category = 'Services' THEN 0.55 ELSE 0.28 END, 2)
                FROM (
                    SELECT d, r, c, p, Category AS category,
                           1 + q % CASE WHEN Category = 'Vélos' THEN 2 ELSE 5 END AS quantity,
                           UnitPrice * (1 + q % CASE WHEN Category = 'Vélos' THEN 2 ELSE 5 END)
                               * (1 + (CAST(strftime('%Y', d) AS INTEGER) - 2023) * 0.12
                                    + CASE WHEN CAST(strftime('%m', d) AS INTEGER) BETWEEN 5 AND 8 THEN 0.15 ELSE 0 END) AS amount
                    FROM draws JOIN Products ON Products.Id = draws.p)
                """, cancellationToken);
        }
        File.Move(building, path);
    }

    /// <summary>
    /// The model over the database, with the same fields as <see cref="SalesData.CreateModel"/>. One context per
    /// query: the visuals of a dashboard run in parallel.
    /// </summary>
    public static QueryableSemanticModel<SaleRow> CreateModel(int rows)
    {
        var options = Options(rows);
        return SemanticModel.For(() => new SalesDbContext(options), db => db.Sales.AsNoTracking(), "sales", "Sales")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Region, "Region")
            .Dimension(s => s.Channel, "Channel")
            .Dimension(s => s.Product.Category, "Category")
            .Dimension(s => s.Product.Name, "Product")
            .Measure(s => s.Amount, "Revenue", format: "#,##0 €")
            .Measure(s => s.Margin, "Margin", format: "#,##0 €")
            .Measure(s => s.Quantity, "Quantity", format: "N0")
            .Count("SalesCount")
            // Ratios of sums per group: the margin rate of a region is not the average of its sales' rates
            .Ratio(s => s.Margin, s => s.Amount, "MarginRate", "MarginRate", format: "P1")
            .Ratio(s => s.Amount, s => 1, "AverageBasket", "AverageBasket", format: "#,##0 €")
            // A formula, as end users write them: they can rewrite it in the application
            .Formula("SUM([Amount]; [Channel] = \"Web\") / [Amount]", "WebShare", "WebShare", format: "P1")
            .Localize(SalesData.Captions);
    }
}
