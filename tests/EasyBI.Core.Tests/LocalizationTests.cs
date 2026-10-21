using System.Collections;
using System.Globalization;
using System.Resources;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

[Collection(CultureCollection.Name)]
public class LocalizationTests
{
    private sealed record Sale(string Region, DateTime Date, decimal Amount);

    private static readonly Sale[] Sales = [new("Nord", new(2024, 2, 1), 1234.5m), new("Sud", new(2024, 5, 1), 10m)];

    /// <summary>Resource manager backed by a dictionary per language, standing in for an application's .resx.</summary>
    private sealed class FakeResources(Dictionary<string, Dictionary<string, string>> byLanguage) : ResourceManager
    {
        public override string? GetString(string name, CultureInfo? culture) =>
            byLanguage.TryGetValue((culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName, out var texts)
            && texts.TryGetValue(name, out var text) ? text : null;
    }

    private static QueryableSemanticModel<Sale> LocalizedModel() =>
        SemanticModel.For(Sales, "sales", "Sales")
            .Dimension(s => s.Region, "Region")
            .Dimension(s => s.Date, "Date")
            .Measure(s => s.Amount, "Revenue")
            .Localize(new FakeResources(new()
            {
                ["en"] = new() { ["Sales"] = "Sales", ["Region"] = "Region", ["Revenue"] = "Revenue" },
                ["fr"] = new() { ["Sales"] = "Ventes", ["Region"] = "Région", ["Revenue"] = "Chiffre d'affaires" },
            }));

    [Fact]
    public void French_resources_define_every_neutral_key()
    {
        var resources = new ResourceManager("EasyBI.Core.Resources.Strings", typeof(SemanticModel).Assembly);

        var neutral = Keys(resources, CultureInfo.InvariantCulture);
        var french = Keys(resources.GetResourceSet(CultureInfo.GetCultureInfo("fr"), true, false)!);

        Assert.NotEmpty(neutral);
        Assert.Equal(neutral.Order(), french.Order());
    }

    [Fact]
    public void Texts_follow_the_EasyBI_culture()
    {
        Assert.Equal("Quarter", DateGrains.Caption(DateGrain.Quarter));
        Assert.Equal("2024 Q1", ValueFormatter.Display(new DatePeriod(DateGrain.Quarter, 20241)));
        Assert.Equal("(blank)", ValueFormatter.Display(null));

        using (TestCulture.Use("fr-FR"))
        {
            Assert.Equal("Trimestre", DateGrains.Caption(DateGrain.Quarter));
            Assert.Equal("Somme", Aggregations.Caption(Aggregation.Sum));
            Assert.Equal("2024 T1", ValueFormatter.Display(new DatePeriod(DateGrain.Quarter, 20241)));
            Assert.Equal("(vide)", ValueFormatter.Display(null));
            Assert.Equal("Oui", ValueFormatter.Display(true));
            Assert.Equal("1 234,5", ValueFormatter.Format(1234.5, null));
            Assert.Equal("Déposez au moins une mesure dans « Valeurs ».",
                VisualValidator.Validate(new VisualDefinition { Model = "m", Mark = MarkType.Bar }));
        }
    }

    [Fact]
    public void Changing_the_culture_raises_one_event()
    {
        var raised = 0;
        void OnChanged(object? sender, EventArgs e) => raised++;
        EasyBILocalization.CultureChanged += OnChanged;
        try
        {
            using (TestCulture.Use("fr-FR"))
                EasyBILocalization.Culture = CultureInfo.GetCultureInfo("fr-FR");
        }
        finally
        {
            EasyBILocalization.CultureChanged -= OnChanged;
        }

        Assert.Equal(2, raised); // fr-FR, then back to invariant
    }

    [Fact]
    public async Task Model_captions_are_translated_and_follow_the_culture()
    {
        var model = LocalizedModel();
        Assert.Equal(["Region", "Date", "Revenue"], model.Metadata.Fields.Select(f => f.Caption));

        using (TestCulture.Use("fr-FR"))
        {
            Assert.Equal("Ventes", model.Metadata.Caption);
            // "Date" has no translation: the caption itself is kept
            Assert.Equal(["Région", "Date", "Chiffre d'affaires"], model.Metadata.Fields.Select(f => f.Caption));

            var result = await model.ExecuteAsync(new([new("Date", DateGrain.Quarter)], [new("Amount", Aggregation.Max)]));
            Assert.Equal(["Date (Trimestre)", "Chiffre d'affaires (Max)"], result.Columns.Select(c => c.Caption));
        }

        Assert.Equal("Sales", model.Metadata.Caption);
    }

    [Fact]
    public async Task Selection_is_described_in_the_current_language()
    {
        var model = LocalizedModel();
        var filters = new FilterContext();
        var visual = new VisualDefinition { Model = "sales", Mark = MarkType.Line, Axis = [new("Date", DateGrain.Quarter)], Values = [new("Amount")] };
        filters.Select(await VisualPipeline.PrepareAsync(model, visual), new CategorySelection(0));

        Assert.Equal("Date (Quarter) = 2024 Q1", Assert.Single(filters.Selections).Describe(model.Metadata));
        using (TestCulture.Use("fr-FR"))
            Assert.Equal("Date (Trimestre) = 2024 T1", filters.Selections[0].Describe(model.Metadata));
    }

    private static List<string> Keys(ResourceManager resources, CultureInfo culture) =>
        Keys(resources.GetResourceSet(culture, true, false)!);

    private static List<string> Keys(ResourceSet set) =>
        set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToList();
}
