using System.Linq.Expressions;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

public static class DateGrains
{
    /// <summary>Localized name of a granularity ("Quarter", "Trimestre"...).</summary>
    public static string Caption(DateGrain grain) => grain switch
    {
        DateGrain.Year => Strings.GrainYear,
        DateGrain.Quarter => Strings.GrainQuarter,
        DateGrain.Month => Strings.GrainMonth,
        DateGrain.Day => Strings.GrainDay,
        _ => Strings.GrainNone,
    };

    /// <summary>Caption of a field seen through a granularity: "Date (Quarter)".</summary>
    public static string Caption(string fieldCaption, DateGrain grain) =>
        grain == DateGrain.None ? fieldCaption : $"{fieldCaption} ({Caption(grain)})";

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

public static class Aggregations
{
    /// <summary>Localized name of an aggregation ("Sum", "Somme"...).</summary>
    public static string Caption(Aggregation aggregation) => aggregation switch
    {
        Aggregation.Sum => Strings.AggregationSum,
        Aggregation.Average => Strings.AggregationAverage,
        Aggregation.Min => Strings.AggregationMin,
        Aggregation.Max => Strings.AggregationMax,
        Aggregation.Count => Strings.AggregationCount,
        _ => aggregation.ToString(),
    };
}
