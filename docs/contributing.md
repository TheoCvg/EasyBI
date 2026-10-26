# Contributing

See [AGENTS.md](https://github.com/TheoCvg/EasyBI/blob/develop/AGENTS.md) for the code style, language rules and Git
workflow.

## Building and testing

```
dotnet build
dotnet test
dotnet run --project samples/EasyBI.Sample
```

## Target frameworks

The libraries run on .NET 8 and on older .NET:

| Projects | Targets |
|---|---|
| `EasyBI.Abstractions`, `EasyBI.Core` | `netstandard2.0`, `net8.0` |
| `EasyBI.Wpf`, `EasyBI.Renderers.*`, `EasyBI.Themes.*` | `net462`, `net8.0-windows` |
| Tests | `net8.0` (`net8.0-windows`) and `net48`, which runs the older builds |

The code is written once, in the C# of the repository, for every target:

- **Compiler features** (records, `init`, `required`, nullable attributes, ranges) are generated for the older targets
  by [PolySharp](https://github.com/Sergio0694/PolySharp), a source generator: it adds no dependency to the packages.
- **Missing instance and LINQ methods** (`ToHashSet`, `DistinctBy`, `GetValueOrDefault`, `Chunk`...) are added for the
  older targets only by `src/Shared/Legacy`, under the framework's names, so the code calls them as on .NET 8.
- **Missing static methods** (`Math.Clamp`, `char.IsAsciiDigit`, `Enum.GetValues<T>`...) cannot be added that way:
  call `Compat` (`src/Shared/Compat.cs`) instead, on every target.
- **Missing types** come from packages referenced for the older targets only: Microsoft's own (`System.Text.Json`,
  `Microsoft.Bcl.AsyncInterfaces`, `Microsoft.Bcl.HashCode`, `System.ValueTuple`) and
  [Portable.System.DateTimeOnly](https://github.com/OlegRa/System.DateTimeOnly) for `DateOnly`. `DateOnly` stays in
  the public API rather than becoming a `DateTime`: a date without time says what it is, and the .NET 8 build does
  not change.
- The public API avoids types older targets lack (`IReadOnlySet<T>`: use `IReadOnlyCollection<T>`) and default
  interface implementations.
- Nullable warnings come from the .NET 8 build only: older frameworks are not annotated, their warnings would be false
  positives.

`dotnet build` builds every target; `dotnet test` runs the tests on .NET 8 and on .NET Framework 4.8 (Windows).

## Writing this documentation

This site is built from the Markdown files of the [`docs`](https://github.com/TheoCvg/EasyBI/tree/develop/docs) folder
with [Zensical](https://zensical.org), and published on GitHub Pages:

- Every page has an **edit** button that opens its Markdown file on GitHub. A change merged into `develop` updates the
  **dev** version of the site within minutes; nothing has to be uploaded by hand.
- Each release tag (`vX.Y.Z`) publishes its own version, which becomes **latest**; older versions stay available
  in the version selector.
- A change to the public API or to the report format updates the page that documents it, and adds an entry to the
  [changelog](changelog.md) under *Unreleased* (with *Breaking changes* when there are).

To preview the site locally:

```
pip install zensical
zensical serve
```
