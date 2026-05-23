using System;
using System.Globalization;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

internal static class RitsuLibUiText
{
    private static bool IsChinese =>
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    // RITSU001: Missing localization
    public static string MissingLocalizationTitle =>
        IsChinese ? "缺少 RitsuLib 本地化" : "Missing RitsuLib localization";

    public static string MissingLocalizationMessageFormat =>
        IsChinese
            ? "缺少 RitsuLib 本地化键: {3}: {4}"
            : "Missing RitsuLib localization keys: {3}: {4}";

    public static string MissingLocalizationDescription =>
        IsChinese
            ? "RitsuLib 引用的本地化键应存在于对应语言 JSON 中。"
            : "RitsuLib localization keys should exist in the matching language JSON.";

    // RITSU002: Ancient dialogue r-mixed within sequence
    public static string AncientDialogueRepeatMixedTitle =>
        IsChinese ? "古老对话同段 r 变体混用" : "Ancient dialogue mixes 'r' variants within a sequence";

    public static string AncientDialogueRepeatMixedMessageFormat =>
        IsChinese
            ? "对话段 '{0}' 在 {1} 中混用带 r 与不带 r 的行: {2}"
            : "Dialogue sequence '{0}' in {1} mixes 'r' and non-'r' lines: {2}";

    public static string AncientDialogueRepeatMixedDescription =>
        IsChinese
            ? "AncientDialogueLocalization.ExistingLine 优先匹配 r 变体；同一段 dialogue 的所有行必须统一使用 r 后缀或全部不用，否则某些行可能被运行时静默忽略。"
            : "AncientDialogueLocalization.ExistingLine prefers the 'r' variant; every line within a single dialogue sequence must uniformly use the 'r' suffix or omit it, otherwise some lines may be silently skipped at runtime.";

    // RITSU003: Unknown localization table name
    public static string UnknownLocalizationTableTitle =>
        IsChinese ? "未识别的本地化表名" : "Unknown localization table";

    public static string UnknownLocalizationTableMessageFormat =>
        IsChinese
            ? "本地化文件 '{0}' 使用未识别的表名 '{1}'，游戏 LocManager 不会加载此表"
            : "Localization file '{0}' uses unknown table '{1}'; the game LocManager will not load this table";

    public static string UnknownLocalizationTableDescription =>
        IsChinese
            ? "RitsuLib 与游戏原生 LocManager 只识别一组已知表名（cards、relics、powers 等）。可能的拼写错误或非约定表名会被静默忽略。"
            : "RitsuLib and the vanilla LocManager only recognize a fixed set of game LocTable names (cards, relics, powers, ...). Typos or non-conventional names are silently ignored.";

    // RITSU013: Resource path
    public static string ResourcePathTitle =>
        IsChinese ? "RitsuLib 资源路径问题" : "RitsuLib resource path issue";

    public static string ResourcePathMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string ResourcePathDescription =>
        IsChinese
            ? "检查 Godot 资源路径前缀、常见资源是否存在以及资源路径形状。"
            : "Checks Godot resource path prefixes, common resource existence, and resource path shape.";

    public static string ResourcePathMissingPrefix(string value) =>
        IsChinese
            ? $"资源路径 '{value}' 应使用 res:// 或 user:// 前缀。"
            : $"Resource path '{value}' should use res:// or user://.";

    public static string ResourcePathNotFound(string value) =>
        IsChinese
            ? $"资源路径 '{value}' 在项目资源索引中未找到。"
            : $"'{value}' was not found in the project resource index.";

    // Code fix titles
    public static string AddMissingKeysToTargetTitle(string targetLabel)
    {
        return IsChinese
            ? $"添加缺失的本地化到 {targetLabel}"
            : $"Add missing localization to {targetLabel}";
    }

    public static string FixAllMissingLocalizationTitle =>
        IsChinese ? "修复所有本地化缺失问题" : "Fix all missing localization issues";

    public static string InsertSnippetTitle =>
        IsChinese ? "插入本地化 JSON 片段" : "Insert localization JSON snippet";

    public static string MissingLocalizationSnippetHeader =>
        IsChinese ? "缺失的 RitsuLib 本地化:" : "Missing RitsuLib localization:";

    public static string InsertTodoFixTitle =>
        IsChinese ? "插入 RitsuLib TODO 修复片段" : "Insert RitsuLib TODO fix snippet";

    public static string InsertCurrentFileResourcePathTodosTitle =>
        IsChinese ? "为当前文件所有缺失资源路径插入 RitsuLib TODO" : "Insert RitsuLib TODOs for all missing resource paths in current file";

    public static string AddPrefixTitle(string prefix)
    {
        return IsChinese ? $"添加 {prefix} 前缀" : $"Add {prefix} prefix";
    }

    public static string ObsoleteApiMigrationTitle(string newMethod)
    {
        return IsChinese ? $"迁移到 {newMethod}" : $"Migrate to {newMethod}";
    }
}
