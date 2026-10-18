# Contributing to OrbitConvert

Thanks for helping. Bug reports, translations, documentation, tests and new converters are all welcome.

## Before you start

- Search existing issues. For larger changes (a new engine, a new UI page), open an issue first so we can
  agree on the approach.
- By contributing you agree that your contribution is licensed under the MIT License of this project,
  and you confirm you have the right to submit it. Do not submit code, icons or text copied from
  proprietary products.

## Development setup

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). In short: Windows 10/11, .NET 10 SDK,
`dotnet build OrbitConvert.sln`, `dotnet test OrbitConvert.sln`.

## Pull requests

1. Create a branch from `main`.
2. Keep changes focused; one topic per pull request.
3. Add or update tests. Bug fixes should come with a test that fails without the fix.
4. Run `dotnet build -c Release` (no new warnings) and `dotnet test`.
5. Update user documentation (README, docs/) when behavior changes.
6. Fill in the pull request template, including how you tested and what you could **not** test.

## Ground rules for conversions

- Never fake a conversion (for example by renaming a file). If an engine cannot do something reliably,
  do not offer it, or mark it `Experimental` with honest `Limitations`.
- Report failures with a message a non-technical user can act on (`ConversionException`), and keep tool
  output in the technical detail.
- Start processes only through `IProcessRunner` with argument lists.
- Write only inside `ConversionContext.WorkDirectory`.

## Adding a dependency

Prefer permissively licensed libraries (MIT, BSD, Apache-2.0). For anything that will be redistributed,
add it to [NOTICE.md](NOTICE.md) with its version and license, and make sure its notice file is copied to
the `licenses` folder. GPL/AGPL components must not be bundled; they may be supported as optional,
separately installed tools.

## Translations

UI strings are currently in XAML and view models. Moving them to `.resx` resources is on the roadmap;
if you want to help translate, comment on the localization issue so we can coordinate.

## Reporting bugs

Use the bug report template. Helpful details: Windows version, OrbitConvert version, the conversion
(input format → target), what happened, and the relevant part of the log from
`%LOCALAPPDATA%\OrbitConvert\logs`. Please do not attach private files; a small sample that reproduces
the problem is ideal.
