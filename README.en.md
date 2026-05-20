# Nothing.STS2RitsuLib.ModAnalyzers

[中文](README.md)

RitsuLib mod analyzer for Slay the Spire 2 mods.

## Installation

Install the analyzer package in your mod project:

```xml
<PackageReference Include="Nothing.STS2RitsuLib.ModAnalyzers" Version="0.6.0" PrivateAssets="all" />
```

The package collects common files automatically through `buildTransitive`. If automatic collection is disabled, expose localization JSON files as analyzer additional files:

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
Languages are discovered from `localization/<language>` directories; an empty language directory still participates in RITSU001 checks.

## Diagnostics

| ID | Severity | Description |
| --- | --- | --- |
| `RITSU001` | Error / Warning | Missing RitsuLib localization keys; missing keys are warnings when another language already has that key, otherwise errors. |
| `RITSU013` | Warning | Resource path shape or existence issue. |

`RITSU001` checks:

- owned keyword registrations, including custom keyword tables and keys
- card pile and top-bar button `static_hover_tips`
- RitsuLib content model localization keys, such as cards, powers, relics, characters, and ancients
- constant keys passed to `I18N.Get`, `I18N.TryGet`, and `I18N.ContainsKey`
- Ancient dialogue keys
- `*LocKey` / `*Key` named parameters on ModSettings attributes
- `WithSharedTooltip` / `WithTooltip` dynamic var tooltips

`RITSU001` only tracks recognized RitsuLib API calls; same-named ordinary methods such as `ModelDb.Power<T>()` or local helpers do not create localization diagnostics.

`RITSU001` computes severity per key: missing `eng` keys are warnings when any non-`eng` language already has the key, otherwise errors; missing non-`eng` keys are errors when `eng` is also missing the key, and warnings when `eng` already has it. Mixed errors and warnings in the same JSON are reported as separate diagnostics.

## Automatic AdditionalFiles

The package uses `buildTransitive` to feed these files to the analyzer by default:

- `mod_manifest.json`
- `**/localization/**/*.json`
- `*.tscn`, `*.tres`, `*.theme.json`, `*.guids.txt`
- common resource extensions such as `png`, `ogg`, `bank`, and `gdshader`

Opt out in the consuming project:

```xml
<PropertyGroup>
  <RitsuLibAnalyzersAutoAdditionalFiles>false</RitsuLibAnalyzersAutoAdditionalFiles>
  <RitsuLibAnalyzersIncludeAssets>false</RitsuLibAnalyzersIncludeAssets>
  <RitsuLibAnalyzersIncludeGodotTextResources>false</RitsuLibAnalyzersIncludeGodotTextResources>
</PropertyGroup>
```

## Rider Quick Fix

Rider can load Roslyn analyzers, completion providers, and analyzer code fixes from NuGet packages.
After enabling Roslyn analyzers, place the caret on a diagnostic highlight and
press `Alt+Enter`.

In Rider 2025.2+, typing `res://` inside RitsuLib resource-path arguments or
`AssetProfile` paths suggests project resource roots, directories, files, and common
`images/relics`, `images/cards`, `images/characters`, and `images/keywords` templates.
If the project has statically resolvable `ResPath` / `ResourceRoot`-style static strings,
interpolated strings can also suggest those symbols.

Available fixes:

- `Add missing localization to <language>/<table>.json` (RITSU001) — appends only the current diagnostic's missing keys to the current target JSON; creates the file if it does not exist.
- `Fix all missing localization issues` (RITSU001) — collects every RITSU001 in the current project and creates or updates every target JSON.
- `Insert localization JSON snippet` (RITSU001) — inserts a comment-form JSON near the diagnostic location for manual copy/paste.
- `Add res:// prefix` (RITSU013) — completes missing resource prefixes; when one project asset matches, it completes the real resource root and prefers the project's unique root symbol.
- `Insert RitsuLib TODO fix snippet` (fallback)

Additionally, calls to RitsuLib APIs marked `[Obsolete]` (compiler CS0618) get migration fixes:

| Deprecated API | Migration target |
| --- | --- |
| `ModKeywordRegistry.Register()` | `RegisterOwned()` |
| `ModKeywordRegistry.RegisterCardKeyword()` | `RegisterCardKeywordOwnedByLocNamespace()` |
| `ModContentPackBuilder.CardKeyword()` | `CardKeywordOwnedByLocNamespace()` |
| `ModContentPackBuilder.Keyword()` | `KeywordOwned()` |
| `AddSlider(..., float, ...)` | `AddSlider(..., double, ...)` |

## Local Development

When testing local builds, `Pack` overwrites the same package version in the local NuGet global package cache by default.

Build, pack, and overwrite-install:

```powershell
dotnet test C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzers.sln --no-restore
dotnet msbuild C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzer\STS2RitsuLib.ModAnalyzers.csproj /t:Pack /p:Configuration=Release
```

The default install path is `%USERPROFILE%\.nuget\packages`, and `NUGET_PACKAGES` is respected. Pass `/p:NuGetGlobalPackagesFolder=...` to override the destination, or `/p:InstallAnalyzerOnPack=false` to pack without installing.
