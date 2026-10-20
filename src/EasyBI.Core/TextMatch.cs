using System.Globalization;

namespace EasyBI.Core;

/// <summary>
/// Search of a text typed by the end user in captions and values: case and accents are ignored ("ile" finds
/// "Île-de-France"), and every word of the search must appear, in any order ("aff chif" finds "Chiffre d'affaires").
/// </summary>
public static class TextMatch
{
    private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    /// <summary>Whether <paramref name="text"/> matches <paramref name="search"/>; an empty search matches everything.</summary>
    public static bool Matches(string? text, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        if (string.IsNullOrEmpty(text)) return false;
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        return search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => compare.IndexOf(text, word, Options) >= 0);
    }
}
