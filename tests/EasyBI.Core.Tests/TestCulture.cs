using System.Globalization;
using System.Runtime.CompilerServices;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>
/// Tests must not depend on the machine's language: EasyBI runs in the invariant culture
/// (English texts, invariant formats) unless a test opts into another one with <see cref="Use"/>.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Initialize() => EasyBILocalization.Culture = CultureInfo.InvariantCulture;

    /// <summary>Switches EasyBI to <paramref name="name"/> until disposed. Only for tests in <see cref="CultureCollection"/>.</summary>
    public static IDisposable Use(string name)
    {
        EasyBILocalization.Culture = CultureInfo.GetCultureInfo(name);
        return new Restore();
    }

    private sealed class Restore : IDisposable
    {
        public void Dispose() => EasyBILocalization.Culture = CultureInfo.InvariantCulture;
    }
}

/// <summary>Tests that change the global EasyBI culture: they run alone, after the parallel tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CultureCollection
{
    public const string Name = "Culture";
}
