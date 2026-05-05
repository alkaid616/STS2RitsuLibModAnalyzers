# Nothing.STS2RitsuLib.ModAnalyzers

[English](README.en.md)

面向 Slay the Spire 2 / RitsuLib mod 的本地化 Roslyn analyzer。

当前只暴露一个诊断：`RITSU001 MissingLocalization`。

## 安装

在 mod 项目中引用 analyzer 包：

```xml
<PackageReference Include="Nothing.STS2RitsuLib.ModAnalyzers" Version="0.1.0" PrivateAssets="all" />
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

`RITSU001` 会检查：

- owned keyword 注册，包括自定义 keyword table/key
- card pile 与 top-bar button 的 `static_hover_tips`
- RitsuLib 内容模型本地化键，例如 cards、powers、relics、characters、ancients
- 传给 `I18N.Get`、`I18N.TryGet`、`I18N.ContainsKey` 的常量 key
- Ancient 对话键

本包不包含 `RITSU002` 或 `RITSU003`。

## Rider 快速修复

Rider 可以加载 NuGet 包中的 Roslyn analyzer 和 code fix。启用 Roslyn analyzers
后，把光标放在 `RITSU001` 高亮位置并按 `Alt+Enter`。

可用修复：

- `添加缺失的本地化键到 ...`
- `插入本地化 JSON 片段`

JSON 修复会追加缺失 key，值为 `""`；如果目标 table 文件不存在，会在同一个
localization 文件夹下创建它。

snippet 修复会在诊断位置附近插入注释形式的 JSON 片段，方便手动复制。

## 本地开发

测试本地构建包时，可以注册本地 NuGet 源：

```powershell
dotnet nuget add source C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\local-packages --name local-ritsulib-analyzers
```

构建并打包：

```powershell
dotnet test C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzers.sln --no-restore
dotnet build C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzer\STS2RitsuLib.ModAnalyzers.csproj -c Release
```

手动发布到 nuget.org：

```powershell
dotnet nuget push C:\Users\Lenovo\Desktop\STS2RitsuLibModAnalyzers\RitsuLibModAnalyzer\bin\Release\Nothing.STS2RitsuLib.ModAnalyzers.0.1.0.nupkg --api-key <你的 NuGet API Key> --source https://api.nuget.org/v3/index.json
```

本包不会推断翻译文本。自动生成的值均为空字符串。
