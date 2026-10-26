// .NET 8 members missing from .NET Standard 2.0 and .NET Framework, compiled for those targets only. They keep the
// framework's names and namespaces so the code calls them as on .NET 8.
namespace System.Collections.Generic
{
    internal static class CollectionPolyfills
    {
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
            where TKey : notnull
        {
            if (dictionary.ContainsKey(key)) return false;
            dictionary.Add(key, value);
            return true;
        }

        public static TValue? GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key) =>
            dictionary.TryGetValue(key, out var value) ? value : default;

        public static TValue GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue) =>
            dictionary.TryGetValue(key, out var value) ? value : defaultValue;

        public static bool Remove<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, out TValue value)
            where TKey : notnull
        {
            if (!dictionary.TryGetValue(key, out value!)) return false;
            dictionary.Remove(key);
            return true;
        }

        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }
    }
}

namespace System.Linq
{
    internal static class EnumerablePolyfills
    {
        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source, IEqualityComparer<T>? comparer = null) =>
            new(source, comparer);

        public static IEnumerable<T> DistinctBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
        {
            var seen = new HashSet<TKey>();
            foreach (var item in source)
                if (seen.Add(keySelector(item)))
                    yield return item;
        }

        public static IOrderedEnumerable<T> Order<T>(this IEnumerable<T> source) => source.OrderBy(x => x);

        public static T FirstOrDefault<T>(this IEnumerable<T> source, Func<T, bool> predicate, T defaultValue)
        {
            foreach (var item in source)
                if (predicate(item))
                    return item;
            return defaultValue;
        }

        public static IEnumerable<T[]> Chunk<T>(this IEnumerable<T> source, int size)
        {
            var chunk = new List<T>(size);
            foreach (var item in source)
            {
                chunk.Add(item);
                if (chunk.Count < size) continue;
                yield return chunk.ToArray();
                chunk.Clear();
            }
            if (chunk.Count > 0) yield return chunk.ToArray();
        }

        public static IEnumerable<(TFirst First, TSecond Second)> Zip<TFirst, TSecond>(this IEnumerable<TFirst> first, IEnumerable<TSecond> second) =>
            first.Zip(second, (a, b) => (a, b));

#if NETFRAMEWORK
        // .NET Standard 2.0 has it, .NET Framework 4.6.2 does not
        public static IEnumerable<T> Append<T>(this IEnumerable<T> source, T element)
        {
            foreach (var item in source)
                yield return item;
            yield return element;
        }
#endif

        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source)
            where TKey : notnull =>
            source.ToDictionary(p => p.Key, p => p.Value);
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class ConditionalWeakTablePolyfills
    {
        public static void AddOrUpdate<TKey, TValue>(this ConditionalWeakTable<TKey, TValue> table, TKey key, TValue value)
            where TKey : class
            where TValue : class
        {
            table.Remove(key);
            table.Add(key, value);
        }
    }
}

namespace System
{
    internal static class StringPolyfills
    {
        public static bool StartsWith(this string text, char value) => text.Length > 0 && text[0] == value;

        public static bool EndsWith(this string text, char value) => text.Length > 0 && text[text.Length - 1] == value;

        public static bool Contains(this string text, char value) => text.IndexOf(value) >= 0;
    }
}
