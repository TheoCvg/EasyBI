# AGENTS.md

Guidelines for anyone (human or AI agent) contributing to EasyBI.

## Project at a glance

EasyBI is a self-service BI library for WPF (.NET 8). Developers declare a semantic model
(dimensions and measures over an `IQueryable`), end users build visuals by drag-and-drop, and
rendering is delegated to interchangeable connectors (LiveCharts2, OxyPlot, ...).

```
Source (IQueryable) ─▶ Semantic model ─▶ AnalyticQuery ─▶ ResultSet ─▶ ChartData ─▶ IVisualRenderer
```

| Project | Role | May depend on |
|---|---|---|
| `src/EasyBI.Abstractions` | The contract: model, query, result, visual and report definitions | nothing |
| `src/EasyBI.Core` | Query engine (expression trees), pivot, filtering, JSON, validation | Abstractions |
| `src/EasyBI.Wpf` | `VisualView`, `ReportDesigner`, `FilterBar`, built-in KPI/Table renderers | Core |
| `src/EasyBI.Renderers.*` | One connector per charting library | Wpf + that library |
| `src/EasyBI.Themes.*` | One theme mapping per control library (`EasyBIThemeMapping`) | Wpf + that library |
| `samples/EasyBI.Sample` | Demo application | everything |
| `tests/EasyBI.Core.Tests` | Engine, filtering, serialization tests (`net8.0`) | Core |
| `tests/EasyBI.Wpf.Tests` | Designer view-model tests (`net8.0-windows`) | Wpf |

Architecture rules:

- `Abstractions` has **no dependencies** and no WPF types. `Core` has no WPF types either: anything
  that can be tested without a UI belongs in `Core`.
- Renderers only draw. They receive already queried and pivoted data (`RenderRequest`), never query
  a model themselves, and report clicks through `request.Select`.
- The report JSON format (`easybi/v1`) is a public contract: additions must stay backward compatible
  (optional properties with defaults). A breaking change requires a new schema version.
- The query engine must keep everything inside the `IQueryable` (filters, grouping, aggregates, sort,
  top) so that providers such as EF Core can translate it to SQL.

## Build and test

```
dotnet build
dotnet test
dotnet run --project samples/EasyBI.Sample
```

Both commands must succeed with no errors before a commit is pushed. Existing NU1701 warnings
(SkiaSharp on .NET Framework assets) are known; do not introduce new compiler warnings.

## Language

- **Everything in the code is written in English**: identifiers, comments, XML docs, test names,
  exception messages meant for developers.
- **User-facing text** (designer labels, validation messages, values shown in the UI) is never
  hard-coded: it lives in `Resources/Strings.resx` (English, neutral) and `Resources/Strings.fr.resx`
  (French) of the project that displays it.
  - Add a key to **both** files; the `French_resources_define_every_neutral_key` tests fail otherwise.
  - In C#, expose it as a property of the project's internal `Resources.Strings` class and format
    with `Strings.Format(...)`. In XAML, use `{bi:Localize Key}`.
  - Texts are resolved in `EasyBILocalization.UICulture` and values formatted in
    `EasyBILocalization.FormatCulture`, never in `CultureInfo.CurrentCulture` directly, so that the
    language can change at runtime.
  - Tests run in the invariant culture; tests that switch language use `TestCulture.Use(...)` and
    belong to the `Culture` collection.
- Commit messages, branch names and PR titles are in English.

## Code style

Follow the style already in the codebase:

- C# latest with `Nullable` and `ImplicitUsings` enabled; file-scoped namespaces.
- `sealed` by default for classes and records; records for immutable data (`init` properties or
  positional parameters).
- Primary constructors, collection expressions (`[]`, `[.. a, .. b]`), pattern matching and switch
  expressions where they make the code shorter and clearer.
- Naming: `PascalCase` for types and members, `_camelCase` for private fields, `camelCase` for locals
  and parameters. Async methods end with `Async` and take a `CancellationToken` when they do I/O.
- XML `<summary>` docs on public types and on members whose intent is not obvious. Comments explain
  *why*, not *what*; keep them short.
- One main type per file; small closely related types (records, enums) may share a file.
- No new third-party dependency in `Abstractions` or `Core` without discussing it first.
- WPF: keep logic in view-models or `Core`, not in code-behind; update UI only on the dispatcher.
- WPF: no hard-coded colors. Draw with the `EasyBITheme` keys, as `DynamicResource` (`SetResourceReference` in code)
  so that theme changes apply at runtime; add a key to `EasyBITheme`, `Light.xaml` and `Dark.xaml` when none fits
  (a test checks that both themes define every key). Do not give standard controls an explicit style without
  `BasedOn` their implicit one, so that the application's control library still applies.

## Tests

- xUnit. Test names are sentences in snake case describing the behavior:
  `Clicking_a_bar_filters_other_visuals_on_axis_and_legend`.
- Every bug fix comes with a test that fails without the fix; every feature comes with tests.
- Tests must be deterministic: in-memory data, no network, no real database, no sleeping beyond what
  a concurrency test strictly needs.

## Git workflow

Branches:

- `main`: stable, released code only. It is updated exclusively by merging `develop` (release) or a
  `hotfix/*` branch. Each merge into `main` is tagged with a version (`v0.2.0`, ...).
- `develop`: integration branch; every feature lands here first. It must always build and pass tests.
- Work branches are short-lived and named `<type>/<short-description>`:
  - `feature/*`, `fix/*`, `refactor/*`, `docs/*`, `test/*`, `build/*`: created from `develop`,
    merged back into `develop`.
  - `hotfix/*`: urgent fix of a released version, created from `main`, merged into `main` **and**
    back into `develop`.
- Never commit directly to `main` or `develop`; everything goes through a pull request.
- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/):
  `<type>(<scope>): <description>` in English, imperative mood, no trailing period.
  - Types: `feat`, `fix`, `refactor`, `perf`, `test`, `docs`, `build`, `ci`, `chore`.
  - Scopes: `abstractions`, `core`, `wpf`, `designer`, `livecharts2`, `oxyplot`, `sample`, `tests`.
  - Breaking changes: add `!` after the scope and a `BREAKING CHANGE:` footer.
  - Example: `feat(core): add cross-filtering between visuals`.

## Packages and releases

- Every project under `src/` is a NuGet package. Shared package metadata lives in
  `src/Directory.Build.props`; repository-wide settings in `Directory.Build.props`.
- Public WPF types are exposed under the XAML namespace `https://github.com/TheoCvg/EasyBI`
  (`src/EasyBI.Wpf/Properties/AssemblyInfo.cs`): register any new public namespace there.
- CI (`.github/workflows/ci.yml`) builds, tests and packs every pull request and every push to
  `develop` and `main`.
- Documentation: the site (https://theocvg.github.io/EasyBI/) is built by Zensical from `docs/` and `zensical.toml`.
  The `Docs` workflow (`.github/workflows/docs.yml`) publishes it to the `gh-pages` branch with mike: every push to
  `develop` updates the **dev** version, every release tag publishes its own version and moves **latest** to it.
  `README.md` stays short (presentation, get started, links): it is also the README of the NuGet packages, so its
  links and images are absolute URLs.
- Releasing: in `docs/changelog.md`, turn *Unreleased* into the version and its date, merge `develop` into `main`, then
  tag `main` with `vX.Y.Z` (or `vX.Y.Z-beta.N`).
  The `Release` workflow builds, tests, publishes the packages to nuget.org and creates the GitHub
  release. Publishing uses nuget.org **Trusted Publishing** (OIDC, no stored API key): it needs the
  nuget.org policy for `TheoCvg/EasyBI` / `release.yml` and the `NUGET_USER` secret (nuget.org
  profile name). Never add a long-lived NuGet API key to the repository. Versions follow [SemVer](https://semver.org/); while in `0.x`,
  breaking changes bump the minor version.

## Pull requests

- Work branches target `develop` (hotfixes target `main`).
- One topic per PR. The PR title follows the commit convention, since work-branch PRs are
  **squash-merged** and the title becomes the commit message on `develop`.
- Release PRs (`develop` → `main`) and hotfix PRs use a **merge commit** (no squash), so that
  `develop` and `main` keep a shared history.
- Before opening a PR: `dotnet build` and `dotnet test` are green.
- Bug fixes and features include tests (see above).
- Any change to the public API or to the JSON report format updates the page of `docs/` that documents it, and adds
  an entry under *Unreleased* in `docs/changelog.md` (in *Breaking changes* when it breaks), in the same PR.
- The description explains what changed, why, and how it was verified (tests, manual check in the
  sample app); mention anything left unverified.
