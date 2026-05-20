# Nothing.STS2RitsuLib.ModAnalyzers

[English](README.en.md)

面向 Slay the Spire 2 / RitsuLib mod 的 Roslyn analyzer。

## 安装

在 mod 项目中引用 analyzer 包：

```xml
<PackageReference Include="Nothing.STS2RitsuLib.ModAnalyzers" Version="0.7.0" PrivateAssets="all" />
```

包会通过 `buildTransitive` 自动收集常见文件。如果你关闭了自动收集，也可以手动把本地化 JSON 暴露给 analyzer：

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
语言集合会从 `localization/<language>` 目录识别；即使某个语言目录没有 JSON 文件，也会参与 RITSU001 检查。

## 诊断

| ID | 严重级别 | 说明 |
| --- | --- | --- |
| `RITSU001` | Error / Warning | 已发现语言的 JSON 文件中缺少 RitsuLib 本地化键；缺失 key 在其他语言已有翻译时为 Warning，否则为 Error。 |
| `RITSU013` | Warning | 资源路径形状或存在性问题。 |

`RITSU001` 会检查：

- owned keyword 注册，包括自定义 keyword table/key
- card pile 与 top-bar button 的 `static_hover_tips`
- RitsuLib 内容模型本地化键，例如 cards、powers、relics、characters、ancients
- 传给 `I18N.Get`、`I18N.TryGet`、`I18N.ContainsKey` 的常量 key
- Ancient 对话键
- ModSettings 属性中的 `*LocKey` / `*Key` 参数
- `WithSharedTooltip` / `WithTooltip` 动态变量提示

`RITSU001` 只会跟踪已识别的 RitsuLib API 调用；同名普通方法（例如 `ModelDb.Power<T>()` 或项目内自定义 helper）不会触发本地化诊断。

`RITSU001` 按 key 单独判断严重级别：`eng` 缺失但任一非 `eng` 语言已存在该 key 时为 Warning，否则为 Error；非 `eng` 语言缺失且 `eng` 也缺失为 Error，`eng` 已存在时为 Warning。同一 JSON 内混合 Error / Warning 时会拆成多条诊断。

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

Rider 可以加载 NuGet 包中的 Roslyn analyzer、completion provider 和 code fix。启用 Roslyn analyzers
后，把光标放在诊断高亮位置并按 `Alt+Enter`。

Rider 2025.2+ 中，在 RitsuLib 资源路径参数或 `AssetProfile` 路径里输入 `res://`，
会补全项目实际资源根、目录、文件，以及常用的 `images/relics`、`images/cards`、
`images/characters` 和 `images/keywords` 路径模板。若项目里存在可静态解析的
`ResPath` / `ResourceRoot` 等静态字符串，也会在插值字符串中提示该符号。

可用修复：

- `添加缺失的本地化到 <language>/<table>.json`（RITSU001）— 只追加当前诊断对应 JSON 的缺失 key，值为 `""`；目标文件不存在时自动创建。
- `修复所有本地化缺失问题`（RITSU001）— 收集当前项目所有 RITSU001，并一次性创建/更新所有语言、所有目标 JSON。
- `插入本地化 JSON 片段`（RITSU001）— 在诊断位置附近插入注释形式的 JSON，方便手动复制。
- `添加 res:// 前缀`（RITSU013）— 自动补全资源路径前缀；能唯一匹配项目资源时会补齐实际资源根，并优先复用项目内唯一的资源根符号。
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
