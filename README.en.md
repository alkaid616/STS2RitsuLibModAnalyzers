# Nothing.STS2RitsuLib.ModAnalyzers

[中文](README.md)

RitsuLib mod localization analyzer for Slay the Spire 2 mods.

It currently exposes one diagnostic only: `RITSU001 MissingLocalization`.

## Installation

Install the analyzer package in your mod project:

```xml
<PackageReference Include="Nothing.STS2RitsuLib.ModAnalyzers" Version="0.1.0" PrivateAssets="all" />
```

Expose localization JSON files as analyzer additional files:

```xml
<AdditionalFiles Include="MyMod/localization/**/*.json" />
```

Supported layouts:

```text
localization/eng/cards.json
localization/zhs/cards.json
localization/eng/card_keywords.json
localization/eng.json
localization/zhs.json
```

`localization/{lang}/{table}.json` is treated as a game LocString table.
`localization/{lang}.json` is treated as a RitsuLib `I18N` file.

## Diagnostic

| ID | Severity | Description |
| --- | --- | --- |
| `RITSU001` | Error | Missing RitsuLib localization keys in discovered language JSON files. |

`RITSU001` checks:

- owned keyword registrations, including custom keyword tables and keys
- card pile and top-bar button `static_hover_tips`
- RitsuLib content model localization keys, such as cards, powers, relics, characters, and ancients
- constant keys passed to `I18N.Get`, `I18N.TryGet`, and `I18N.ContainsKey`
- Ancient dialogue keys

This package does not include `RITSU002` or `RITSU003`.

## Rider Quick Fix

Rider can load Roslyn analyzers and analyzer code fixes from NuGet packages.
After enabling Roslyn analyzers, place the caret on a `RITSU001` highlight and
press `Alt+Enter`.

Available fixes:

- `Add missing localization keys to ...`
- `Insert localization JSON snippet`

The JSON fix appends missing keys with empty string values and can create a
missing table file under the same localization folder.

The snippet fix inserts a comment-form JSON snippet near the diagnostic location
for manual copy/paste.

## Local Development

To test a locally built package, register a local package source:

```powershell
dotnet nuget add source C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\local-packages --name local-ritsulib-analyzers
```

Build and pack:

```powershell
dotnet test C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzers.sln --no-restore
dotnet build C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzer\STS2RitsuLib.ModAnalyzers.csproj -c Release
```

Manual publish command for nuget.org:

```powershell
dotnet nuget push C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzer\bin\Release\Nothing.STS2RitsuLib.ModAnalyzers.0.1.0.nupkg --api-key <your NuGet API key> --source https://api.nuget.org/v3/index.json
```

The package does not infer translated text. Generated values are empty strings.
