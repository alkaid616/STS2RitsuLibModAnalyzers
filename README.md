# Nothing.STS2RitsuLib.ModAnalyzers

[English](README.en.md)

面向 Slay the Spire 2 / RitsuLib mod 的 Roslyn analyzer。

## 安装

在 mod 项目中引用 analyzer 包：

```xml
<PackageReference Include="Nothing.STS2RitsuLib.ModAnalyzers" Version="0.2.0" PrivateAssets="all" />
```

把本地化 JSON 暴露给 analyzer：

```xml
<AdditionalFiles Include="MyMod/localization/**/*.json" />
```

支持的本地化文件结构：

```text
localization/eng/cards.json
localization/zhs/cards.json
localization/eng/card_keywords.json
localization/eng.json
localization/zhs.json
```

`localization/{lang}/{table}.json` 会被当作游戏 LocString 表。
`localization/{lang}.json` 会被当作 RitsuLib `I18N` 文件。

## 诊断

| ID | 严重级别 | 说明 |
| --- | --- | --- |
| `RITSU001` | Error | 已发现语言的 JSON 文件中缺少 RitsuLib 本地化键。 |
| `RITSU002` | Warning | `mod_manifest.json` 缺少 `STS2-RitsuLib` 依赖。 |
| `RITSU003` | Error | 代码里使用的 mod id 与 manifest 不一致。 |
| `RITSU004` | Error | 自动注册属性存在，但没有调用 `ModTypeDiscoveryHub.RegisterModAssembly`。 |
| `RITSU005` | Warning | 检测到 Godot 脚本或文本资源，但没有调用 `EnsureGodotScriptsRegistered`。 |
| `RITSU006` | Error | `CreateContentPack(...)` 链没有调用 `.Apply()`。 |
| `RITSU007` | Error | 固定 public entry 重复。 |
| `RITSU008` | Info | id / stem 形状不推荐。 |
| `RITSU009` | Warning | settings page / section / entry / callback 契约问题。 |
| `RITSU010` | Warning | `ModDataStore` 契约问题。 |
| `RITSU011` | Error | `IPatchMethod` / `IModPatches` 静态成员缺失。 |
| `RITSU012` | Error | patch target 方法或属性不存在。 |
| `RITSU013` | Warning | 资源路径形状或存在性问题。 |
| `RITSU014` | Warning | FMOD 字符串形状问题。 |
| `RITSU015` | Warning | runtime helper 字面量问题。 |
| `RITSU016` | Info | 旧式 pool hook 覆写。 |
| `RITSU017` | Warning | 可释放句柄未释放（PlayLoop / PlayMusic / CreateManualScope / SubscribeLifecycle）。 |
| `RITSU018` | Error | `ModContentPackBuilder.For()` 链没有调用 `.Apply()`。 |
| `RITSU019` | Warning | `AudioSource.Event` / `Snapshot` / `Guid` 路径前缀不正确。 |
| `RITSU020` | Warning | `[ModInterop]` 目标 mod id 为空或格式不推荐。 |
| `RITSU021` | Info | 旧式 `StartingDeckTypes` / `StartingRelicTypes` / `StartingPotionTypes` 覆写。 |
| `RITSU022` | Warning | `AddSubpage` 引用了不存在的 settings page。 |
| `RITSU023` | Warning | `[InteropTarget]` 在 `[ModInterop]` 类之外使用。 |
| `RITSU024` | Warning | 同一 section 中重复引用 subpage。 |
| `RITSU025` | Warning | `SubscribeLifecycleOnce` 的事件类型不是 sealed class 或 struct。 |

`RITSU001` 会检查：

- owned keyword 注册，包括自定义 keyword table/key
- card pile 与 top-bar button 的 `static_hover_tips`
- RitsuLib 内容模型本地化键，例如 cards、powers、relics、characters、ancients
- 传给 `I18N.Get`、`I18N.TryGet`、`I18N.ContainsKey` 的常量 key
- Ancient 对话键

## 自动 AdditionalFiles

这个包会通过 `buildTransitive` 默认把以下文件送进 analyzer：

- `mod_manifest.json`
- `**/localization/**/*.json`
- `*.tscn`、`*.tres`、`*.theme.json`、`*.guids.txt`
- 常见资源扩展名，如 `png`、`ogg`、`bank`、`gdshader`

可以在项目里关闭：

```xml
<PropertyGroup>
  <RitsuLibAnalyzersAutoAdditionalFiles>false</RitsuLibAnalyzersAutoAdditionalFiles>
  <RitsuLibAnalyzersIncludeAssets>false</RitsuLibAnalyzersIncludeAssets>
  <RitsuLibAnalyzersIncludeGodotTextResources>false</RitsuLibAnalyzersIncludeGodotTextResources>
</PropertyGroup>
```

## Rider 快速修复

Rider 可以加载 NuGet 包中的 Roslyn analyzer 和 code fix。启用 Roslyn analyzers
后，把光标放在 `RITSU001` 高亮位置并按 `Alt+Enter`。

可用修复：

- `添加缺失的本地化键到 ...`（RITSU001）— 追加缺失 key，值为 `""`；目标文件不存在时自动创建。
- `插入本地化 JSON 片段`（RITSU001）— 在诊断位置附近插入注释形式的 JSON，方便手动复制。
- `插入 RegisterModAssembly 样板`（RITSU004）
- `插入 EnsureGodotScriptsRegistered 样板`（RITSU005）
- `为 content pack 添加 .Apply()`（RITSU006 / RITSU018）
- `生成 settings callback/provider stub`（RITSU009）
- `生成 patch 必要成员 stub`（RITSU011 / RITSU012）
- `用 using 语句包装`（RITSU017）— 将 `PlayLoop` / `SubscribeLifecycle` 等返回值包装为 `using var`。
- `添加 event:/ 前缀`（RITSU019）— 自动补全 `AudioSource.Event` / `Snapshot` 缺失的 FMOD 路径前缀。
- `插入 RitsuLib TODO 修复片段`（兜底）

此外，对 RitsuLib 已标记 `[Obsolete]` 的 API 调用（编译器 CS0618），Rider 会额外提供迁移修复：

| 旧 API | 迁移目标 |
| --- | --- |
| `ModKeywordRegistry.Register()` | `RegisterOwned()` |
| `ModKeywordRegistry.RegisterCardKeyword()` | `RegisterCardKeywordOwnedByLocNamespace()` |
| `ModContentPackBuilder.CardKeyword()` | `CardKeywordOwnedByLocNamespace()` |
| `ModContentPackBuilder.Keyword()` | `KeywordOwned()` |
| `AddSlider(..., float, ...)` | `AddSlider(..., double, ...)` |

## 本地开发

测试本地构建包时，`Pack` 会默认覆盖安装同版本包到本机 NuGet 全局包缓存。

构建、打包并覆盖安装：

```powershell
dotnet test C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzers.sln --no-restore
dotnet msbuild C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzer\STS2RitsuLib.ModAnalyzers.csproj /t:Pack /p:Configuration=Release
```

默认安装路径为 `%USERPROFILE%\.nuget\packages`，也会尊重 `NUGET_PACKAGES` 环境变量。可以用 `/p:NuGetGlobalPackagesFolder=...` 覆盖目标目录；用 `/p:InstallAnalyzerOnPack=false` 只打包不安装。

手动发布到 nuget.org：

```powershell
dotnet nuget push C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzer\bin\Release\Nothing.STS2RitsuLib.ModAnalyzers.0.2.0.nupkg --api-key <你的 NuGet API Key> --source https://api.nuget.org/v3/index.json
```

本包不会推断翻译文本。自动生成的值均为空字符串。
