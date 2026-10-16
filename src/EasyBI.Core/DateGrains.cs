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
    /// Reduces a date (DateTime, DateOnly, DateTimeOffset) to its granularity: a sortable integer
    /// (2024, 20241, 202403) or the day, a simple expression that LINQ providers can translate.
    /// </summary>
    internal static Expression Apply(Expression date, DateGrain grain)
    {
        if (Nullable.GetUnderlyingType(date.Type) is not null)
            date = Expression.Call(date, nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes);

        if (grain == DateGrain.Day)
            return date.Type == typeof(DateOnly) ? date : Expression.Property(date, nameof(DateTime.Date));

        var year = Expression.Property(date, nameof(DateTime.Year));
        var month = Expression.Property(date, nameof(DateTime.Month));
        return grain switch
        {
            DateGrain.Year => year,
            DateGrain.Quarter => Expression.Add(
                Expression.Multiply(year, Expression.Constant(10)),
                Expression.Divide(Expression.Add(month, Expression.Constant(2)), Expression.Constant(3))),
            DateGrain.Month => Expression.Add(Expression.Multiply(year, Expression.Constant(100)), month),
            _ => throw new ArgumentOutOfRangeException(nameof(grain)),
        };
    }
}
