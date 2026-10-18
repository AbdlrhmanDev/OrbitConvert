## What does this change?

<!-- A short description and the motivation. Link related issues: "Fixes #123". -->

## How was it tested?

- [ ] `dotnet build OrbitConvert.sln -c Release` (no new warnings)
- [ ] `dotnet test OrbitConvert.sln`
- [ ] New or updated tests cover the change
- [ ] Manually tested in the app (describe below)

<!-- Windows version, display setup, tools installed. -->

## What could not be tested?

<!-- Be explicit, e.g. "not tested on Windows 10" or "LibreOffice route untested". -->

## Checklist

- [ ] No fake conversions; unsupported cases fail with a clear message
- [ ] External processes use `IProcessRunner` with argument lists
- [ ] Documentation updated (README / docs) if behavior changed
- [ ] New dependencies are listed in NOTICE.md with version and license
