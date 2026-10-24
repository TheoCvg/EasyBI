# Contributing

See [AGENTS.md](https://github.com/TheoCvg/EasyBI/blob/develop/AGENTS.md) for the code style, language rules and Git
workflow.

## Building and testing

```
dotnet build
dotnet test
dotnet run --project samples/EasyBI.Sample
```

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
