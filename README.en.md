# Nothing.STS2RitsuLib.ModAnalyzers

[中文](README.md)

RitsuLib mod analyzer for Slay the Spire 2 mods.

## Installation

Install the analyzer package in your mod project:

```xml
<PackageReference Include="Nothing.STS2RitsuLib.ModAnalyzers" Version="0.3.0" PrivateAssets="all" />
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
| `RITSU001` | Error / Warning | Missing RitsuLib localization keys; missing `eng` fallback keys are errors, while other languages are warnings when `eng` already has that key. |
| `RITSU002` | Warning | `mod_manifest.json` is missing the `STS2-RitsuLib` dependency. |
| `RITSU003` | Error | Code uses a mod id that does not match the manifest. |
| `RITSU004` | Error | Auto-registration attributes are used without `ModTypeDiscoveryHub.RegisterModAssembly`. |
| `RITSU005` | Warning | Godot scripts or text resources were found without `EnsureGodotScriptsRegistered`. |
| `RITSU006` | Error | A `CreateContentPack(...)` chain does not call `.Apply()`. |
| `RITSU007` | Error | Duplicate fixed public entry. |
| `RITSU008` | Info | Discouraged id / stem shape. |
| `RITSU009` | Warning | Settings page / section / entry / callback contract issue. |
| `RITSU010` | Warning | `ModDataStore` contract issue. |
| `RITSU011` | Error | Missing `IPatchMethod` / `IModPatches` static members. |
| `RITSU012` | Error | Patch target method or property was not found. |
| `RITSU013` | Warning | Resource path shape or existence issue. |
| `RITSU014` | Warning | FMOD string shape issue. |
| `RITSU015` | Warning | Runtime helper literal issue. |
| `RITSU016` | Info | Legacy pool hook override. |
| `RITSU017` | Warning | Disposable handle not disposed (PlayLoop / PlayMusic / CreateManualScope / SubscribeLifecycle). |
| `RITSU018` | Error | `ModContentPackBuilder.For()` chain does not call `.Apply()`. |
| `RITSU019` | Warning | `AudioSource.Event` / `Snapshot` / `Guid` path prefix is incorrect. |
| `RITSU020` | Warning | `[ModInterop]` target mod id is empty or has a discouraged format. |
| `RITSU021` | Info | Legacy `StartingDeckTypes` / `StartingRelicTypes` / `StartingPotionTypes` override. |
| `RITSU022` | Warning | `AddSubpage` references a non-existent settings page. |
| `RITSU023` | Warning | `[InteropTarget]` used outside a `[ModInterop]` class. |
| `RITSU024` | Warning | Duplicate subpage reference in the same section. |
| `RITSU025` | Warning | `SubscribeLifecycleOnce` event type is not a sealed class or struct. |

`RITSU001` checks:

- owned keyword registrations, including custom keyword tables and keys
- card pile and top-bar button `static_hover_tips`
- RitsuLib content model localization keys, such as cards, powers, relics, characters, and ancients
- constant keys passed to `I18N.Get`, `I18N.TryGet`, and `I18N.ContainsKey`
- Ancient dialogue keys

`RITSU001` computes severity per key: missing `eng` keys are errors; missing non-`eng` keys are errors when `eng` is also missing the key, and warnings when `eng` already has it. Mixed errors and warnings in the same JSON are reported as separate diagnostics.

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

Rider can load Roslyn analyzers and analyzer code fixes from NuGet packages.
After enabling Roslyn analyzers, place the caret on a `RITSU001` highlight and
press `Alt+Enter`.

Available fixes:

- `Add missing localization to <language>/<table>.json` (RITSU001) — appends only the current diagnostic's missing keys to the current target JSON; creates the file if it does not exist.
- `Add missing localization to */<table>.json` (RITSU001) — appends the current diagnostic's keys to the same table for every language.
- `Fix all missing localization issues` (RITSU001) — collects every RITSU001 in the current project and creates or updates every target JSON.
- `Insert localization JSON snippet` (RITSU001) — inserts a comment-form JSON near the diagnostic location for manual copy/paste.
- `Insert RegisterModAssembly boilerplate` (RITSU004)
- `Insert EnsureGodotScriptsRegistered boilerplate` (RITSU005)
- `Add .Apply() to content pack` (RITSU006 / RITSU018)
- `Generate settings callback/provider stub` (RITSU009)
- `Generate required patch members stub` (RITSU011 / RITSU012)
- `Wrap in using statement` (RITSU017) — wraps `PlayLoop` / `SubscribeLifecycle` return values in `using var`.
- `Add event:/ prefix` (RITSU019) — prepends the missing FMOD path prefix to `AudioSource.Event` / `Snapshot` calls.
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

Manual publish command for nuget.org:

```powershell
dotnet nuget push C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzer\bin\Release\Nothing.STS2RitsuLib.ModAnalyzers.0.3.0.nupkg --api-key <your NuGet API key> --source https://api.nuget.org/v3/index.json
```

The package does not infer translated text. Generated values are empty strings.
