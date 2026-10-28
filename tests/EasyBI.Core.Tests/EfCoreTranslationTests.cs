using EasyBI.Abstractions;
using EasyBI.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyBI.Core.Tests;

/// <summary>
/// The engine's queries must stay translatable by EF Core: LINQ to objects runs any expression, a database provider
/// fails on what it cannot translate. SQLite in memory stands for the relational providers (it cannot sum decimals,
/// hence the double amounts).
/// </summary>
public sealed class EfCoreTranslationTests : IDisposable
{
    public sealed class Shipment
    {
        public int Id { get; set; }
        public DateTime? Shipped { get; set; }
        public DateOnly? Day { get; set; }
        public double Amount { get; set; }
    }

    private sealed class Shipments(SqliteConnection connection) : DbContext
    {
        public DbSet<Shipment> Rows => Set<Shipment>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
    }

    // An in-memory SQLite database lives as long as its connection
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public EfCoreTranslationTests()
    {
        _connection.Open();
        using var context = new Shipments(_connection);
        context.Database.EnsureCreated();
        context.Rows.AddRange(
            new Shipment { Shipped = new(2024, 2, 10), Day = new(2024, 2, 10), Amount = 1 },
            new Shipment { Shipped = new(2025, 5, 3), Day = new(2025, 5, 3), Amount = 4 },
            new Shipment { Amount = 2 });
        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private QueryableSemanticModel<Shipment> Model() =>
        SemanticModel.For(() => new Shipments(_connection), c => c.Rows, "m")
            .Dimension(s => s.Shipped, "Expédition")
            .Dimension(s => s.Day, "Jour")
            .Measure(s => s.Amount, "CA");

    [Theory]
    [InlineData("Shipped", DateGrain.Year)]
    [InlineData("Shipped", DateGrain.Quarter)]
    [InlineData("Shipped", DateGrain.Month)]
    [InlineData("Shipped", DateGrain.Day)]
    [InlineData("Day", DateGrain.Month)]
    [InlineData("Day", DateGrain.Day)]
    public async Task Nullable_dates_are_grouped_and_filtered_by_period_in_the_database(string field, DateGrain grain)
    {
        var model = Model();

        var grouped = await model.ExecuteAsync(new([new(field, grain)], [new("Amount")]));
        Assert.Null(grouped.Rows[0][0]);
        Assert.Equal([2d, 1d, 4d], grouped.Rows.Select(r => r[1]));

        var missing = await model.ExecuteAsync(new([], [new("Amount")],
            Filters: [new(field, FilterOperator.In, [InvariantValue.Format(null)], grain)]));
        Assert.Equal(2d, Assert.Single(missing.Rows)[0]);
    }

    [Fact]
    public async Task Time_calculations_on_a_nullable_date_run_in_the_database()
    {
        var result = await Model().ExecuteWithTimeAsync(new([new("Shipped", DateGrain.Month)],
            [new("Amount"), new("Amount") { Time = TimeCalculation.PreviousYear }]));

        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public async Task A_set_chosen_by_conditions_and_an_order_runs_in_the_database()
    {
        var model = Model();
        var variables = new VariableContext();
        variables.Load([new ReportVariable
        {
            Name = "s",
            Model = "m",
            Formula = "SELECT TOP 1 [Day] WHERE [Day] >= \"2024-01-01\" AND NOT [Day] = \"2024-03-01\" AND [Amount] > 0 ORDER BY [Amount] / 2 DESC",
        }]);

        var members = await variables.MembersAsync("s", new SetSource(_ => model));

        Assert.Equal(["2025-05-03"], members);
    }

    [Fact]
    public async Task A_field_grouped_by_a_set_runs_in_the_database()
    {
        var result = await Model().ExecuteAsync(new([new("Day") { Set = "s", SetMembers = ["2024-02-10"] }], [new("Amount")]));

        Assert.Equal([true, false], result.Rows.Select(r => r[0]));
        Assert.Equal([1d, 6d], result.Rows.Select(r => r[1]));
    }
}
