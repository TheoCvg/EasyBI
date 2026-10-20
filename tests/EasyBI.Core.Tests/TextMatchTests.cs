using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class TextMatchTests
{
    [Theory]
    [InlineData("Île-de-France", "ile")]
    [InlineData("Île-de-France", "DE FRANCE")]
    [InlineData("Chiffre d'affaires", "aff chif")]
    [InlineData("Catégorie", "categ")]
    [InlineData("Région", "  ")]
    [InlineData("Région", null)]
    public void A_search_ignores_case_and_accents_and_takes_its_words_in_any_order(string text, string? search) =>
        Assert.True(TextMatch.Matches(text, search));

    [Theory]
    [InlineData("Chiffre d'affaires", "marge")]
    [InlineData("Chiffre d'affaires", "chiffre marge")]
    [InlineData(null, "nord")]
    public void A_search_needs_every_word(string? text, string search) =>
        Assert.False(TextMatch.Matches(text, search));
}
