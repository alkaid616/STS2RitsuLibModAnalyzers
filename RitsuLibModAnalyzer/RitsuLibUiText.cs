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
