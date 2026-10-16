using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Core.Tests;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

[Collection(CultureCollection.Name)]
public class DesignerLocalizationTests
{
    private sealed record Sale(string Region, decimal Amount);

    private sealed class FrenchCaptions : ResourceManager
    {
        private static readonly Dictionary<string, string> French = new() { ["Sales"] = "Ventes", ["Region"] = "Région" };

        public override string? GetString(string name, CultureInfo? culture) =>
            culture?.TwoLetterISOLanguageName == "fr" && French.TryGetValue(name, out var text) ? text : null;
    }

    [Fact]
    public void French_resources_define_every_neutral_key()
    {
        var resources = new ResourceManager("EasyBI.Wpf.Resources.Strings", typeof(VisualView).Assembly);

        var neutral = Keys(resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!);
        var french = Keys(resources.GetResourceSet(CultureInfo.GetCultureInfo("fr"), true, false)!);

        Assert.NotEmpty(neutral);
        Assert.Equal(neutral.Order(), french.Order());
    }

    [Fact]
    public void Designer_follows_the_language_without_losing_the_visual()
    {
        var model = SemanticModel.For(new[] { new Sale("Nord", 1m) }, "sales", "Sales")
            .Dimension(s => s.Region, "Region")
            .Measure(s => s.Amount, "Amount")
            .Localize(new FrenchCaptions());
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(model));
        designer.Load(new VisualDefinition { Title = "T", Model = "sales", Axis = [new("Region")], Values = [new("Amount")] });
        var visualId = designer.Visual!.Id;
        var bars = designer.MarkOptions.Single(o => o.Mark == MarkType.Bar);
        var notified = new List<string?>();
        ((INotifyPropertyChanged)bars).PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        Assert.Equal("Bars", bars.Label);
        Assert.Equal("Region", designer.Axis.Single().Caption);

        using (TestCulture.Use("fr-FR"))
        {
            Assert.Equal("Barres", bars.Label);
            Assert.Contains(nameof(MarkOptionViewModel.Label), notified);
            Assert.Equal("Ventes", designer.Models.Single().Label);
            Assert.Equal("Région", designer.Axis.Single().Caption);
            Assert.Equal("Somme", ShelfItemViewModel.AggregationOptions[0].Label);
            Assert.Equal(visualId, designer.Visual!.Id);
            Assert.Equal("T", designer.Visual.Title);
        }
    }

    private static List<string> Keys(ResourceSet set) => set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToList();
}
