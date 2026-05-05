using System;
using System.Globalization;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

internal static class RitsuLibUiText
{
    private static bool IsChinese =>
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

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

    public static string AddMissingKeysTitle(string targetLabel)
    {
        return IsChinese
            ? $"添加缺失的本地化键到 {targetLabel}"
            : $"Add missing localization keys to {targetLabel}";
    }

    public static string InsertSnippetTitle =>
        IsChinese ? "插入本地化 JSON 片段" : "Insert localization JSON snippet";

    public static string MissingLocalizationSnippetHeader =>
        IsChinese ? "缺失的 RitsuLib 本地化:" : "Missing RitsuLib localization:";
}
