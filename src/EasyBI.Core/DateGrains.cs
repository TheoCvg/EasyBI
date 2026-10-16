using System.Linq.Expressions;
using EasyBI.Abstractions;

namespace EasyBI.Core;

public static class DateGrains
{
    public static string Caption(DateGrain grain) => grain switch
    {
        DateGrain.Year => "Année",
        DateGrain.Quarter => "Trimestre",
        DateGrain.Month => "Mois",
        DateGrain.Day => "Jour",
        _ => "Valeur exacte",
    };

    /// <summary>
    /// Ramène une date à sa granularité sous forme d'entier triable (2024, 20241, 202403),
    /// une expression simple que les providers LINQ savent traduire.
    /// </summary>
    internal static Expression Apply(Expression date, DateGrain grain)
    {
        if (date.Type == typeof(DateTime?))
            date = Expression.Call(date, nameof(Nullable<DateTime>.GetValueOrDefault), Type.EmptyTypes);

        var year = Expression.Property(date, nameof(DateTime.Year));
        var month = Expression.Property(date, nameof(DateTime.Month));
        return grain switch
        {
            DateGrain.Year => year,
            DateGrain.Quarter => Expression.Add(
                Expression.Multiply(year, Expression.Constant(10)),
                Expression.Divide(Expression.Add(month, Expression.Constant(2)), Expression.Constant(3))),
            DateGrain.Month => Expression.Add(Expression.Multiply(year, Expression.Constant(100)), month),
            DateGrain.Day => Expression.Property(date, nameof(DateTime.Date)),
            _ => throw new ArgumentOutOfRangeException(nameof(grain)),
        };
    }

    internal static string Label(int value, DateGrain grain) => grain switch
    {
        DateGrain.Year => value.ToString(),
        DateGrain.Quarter => $"{value / 10} T{value % 10}",
        DateGrain.Month => $"{value / 100}-{value % 100:00}",
        _ => value.ToString(),
    };
}
