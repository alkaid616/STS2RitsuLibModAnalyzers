using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

internal sealed class RitsuLibAdditionalFileIndex
{
    private const string I18NTable = "__ritsulib_i18n__";

    private static readonly string[] AssetExtensions =
    {
        ".png", ".jpg", ".jpeg", ".webp", ".svg", ".ogg", ".wav", ".mp3", ".bank", ".tscn", ".tres", ".res",
        ".theme", ".json", ".txt", ".gdshader", ".material",
    };

    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _tableKeysByLanguage;
    private readonly Dictionary<string, HashSet<string>> _i18NKeysByLanguage;
    private readonly Dictionary<string, Dictionary<string, string>> _tablePathsByLanguage;
    private readonly Dictionary<string, string> _i18NPathsByLanguage;
    private readonly HashSet<string> _assetRelativePaths;
    private readonly string[] _roots;

    private RitsuLibAdditionalFileIndex(
        ModManifestInfo manifest,
        Dictionary<string, Dictionary<string, HashSet<string>>> tableKeysByLanguage,
        Dictionary<string, HashSet<string>> i18NKeysByLanguage,
        Dictionary<string, Dictionary<string, string>> tablePathsByLanguage,
        Dictionary<string, string> i18NPathsByLanguage,
        HashSet<string> assetRelativePaths,
        List<string> roots,
        bool hasGodotTextResources)
    {
        Manifest = manifest;
        _tableKeysByLanguage = tableKeysByLanguage;
        _i18NKeysByLanguage = i18NKeysByLanguage;
        _tablePathsByLanguage = tablePathsByLanguage;
        _i18NPathsByLanguage = i18NPathsByLanguage;
        _assetRelativePaths = assetRelativePaths;
        _roots = roots.OrderBy(root => root, StringComparer.OrdinalIgnoreCase).ToArray();
        HasGodotTextResources = hasGodotTextResources;
        HasAssetIndex = assetRelativePaths.Count > 0;
        Languages = tableKeysByLanguage.Keys
            .Concat(i18NKeysByLanguage.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ModManifestInfo Manifest { get; }
    public string[] Languages { get; }
    public bool HasGodotTextResources { get; }
    public bool HasAssetIndex { get; }

    public static RitsuLibAdditionalFileIndex Create(CompilationStartAnalysisContext context)
    {
        Dictionary<string, Dictionary<string, HashSet<string>>> tableKeysByLanguage = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> i18NKeysByLanguage = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Dictionary<string, string>> tablePathsByLanguage = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> i18NPathsByLanguage = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> assetRelativePaths = new(StringComparer.OrdinalIgnoreCase);
        List<string> roots = new();
        ModManifestInfo manifest = ModManifestInfo.None;
        var hasGodotTextResources = false;
        var projectDirectory = GetBuildProperty(context.Options, "MSBuildProjectDirectory");

        foreach (var file in context.Options.AdditionalFiles)
        {
            var path = file.Path;
            if (IsIgnoredPath(path))
                continue;

            AddAssetPath(path, projectDirectory, assetRelativePaths);

            var extension = Path.GetExtension(path);
            if (extension.Equals(".tscn", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".tres", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".theme.json", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".guids.txt", StringComparison.OrdinalIgnoreCase))
            {
                hasGodotTextResources = true;
            }

            if (Path.GetFileName(path).Equals("mod_manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                var text = file.GetText(context.CancellationToken)?.ToString();
                manifest = ModManifestInfo.Parse(path, text);
            }

            if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryGetLocalizationPathParts(path, out var language, out var table, out var root, out var isI18NFile))
                continue;

            if (!string.IsNullOrWhiteSpace(root) && !roots.Contains(root!, StringComparer.OrdinalIgnoreCase))
                roots.Add(root!);

            var json = file.GetText(context.CancellationToken)?.ToString();
            var keys = string.IsNullOrWhiteSpace(json)
                ? new HashSet<string>(StringComparer.Ordinal)
                : JsonTopLevelKeyScanner.ReadKeys(json!);

            if (isI18NFile)
            {
                if (!i18NKeysByLanguage.TryGetValue(language, out var i18NKeys))
                {
                    i18NKeys = new(StringComparer.OrdinalIgnoreCase);
                    i18NKeysByLanguage[language] = i18NKeys;
                }

                foreach (var key in keys)
                    i18NKeys.Add(key);

                i18NPathsByLanguage[language] = path;
                continue;
            }

            if (!tableKeysByLanguage.TryGetValue(language, out var tables))
            {
                tables = new(StringComparer.OrdinalIgnoreCase);
                tableKeysByLanguage[language] = tables;
            }

            if (!tables.TryGetValue(table, out var tableKeys))
            {
                tableKeys = new(StringComparer.Ordinal);
                tables[table] = tableKeys;
            }

            foreach (var key in keys)
                tableKeys.Add(key);

            if (!tablePathsByLanguage.TryGetValue(language, out var tablePaths))
            {
                tablePaths = new(StringComparer.OrdinalIgnoreCase);
                tablePathsByLanguage[language] = tablePaths;
            }

            tablePaths[table] = path;
        }

        return new(
            manifest,
            tableKeysByLanguage,
            i18NKeysByLanguage,
            tablePathsByLanguage,
            i18NPathsByLanguage,
            assetRelativePaths,
            roots,
            hasGodotTextResources);
    }

    public bool ContainsTable(string language, string table, string key)
    {
        return _tableKeysByLanguage.TryGetValue(language, out var tables) &&
               tables.TryGetValue(table, out var keys) &&
               keys.Contains(key);
    }

    public bool ContainsI18N(string language, string key)
    {
        return _i18NKeysByLanguage.TryGetValue(language, out var keys) &&
               keys.Contains(key);
    }

    public string GetTargetPath(string language, string table, bool isI18N)
    {
        if (isI18N && _i18NPathsByLanguage.TryGetValue(language, out var i18NPath))
            return i18NPath;

        if (!isI18N &&
            _tablePathsByLanguage.TryGetValue(language, out var tablePaths) &&
            tablePaths.TryGetValue(table, out var tablePath))
            return tablePath;

        var root = _roots.Length == 0 ? "localization" : _roots[0];
        return isI18N
            ? CombinePath(root, $"{language}.json")
            : CombinePath(root, language, $"{table}.json");
    }

    public bool ResourceExists(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
            return false;

        var relative = NormalizeResourcePath(resourcePath);
        if (string.IsNullOrWhiteSpace(relative))
            return false;

        if (_assetRelativePaths.Contains(relative))
            return true;

        return false;
    }

    public static bool IsResourcePath(string value)
    {
        return value.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("user://", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeLanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "eng";

        var text = (language ?? string.Empty).Trim().Replace('-', '_').ToLowerInvariant();
        return text switch
        {
            "zh_cn" or "zh_hans" or "zh_sg" or "zh" => "zhs",
            "en_us" or "en_gb" or "en" or "eng" => "eng",
            "ja" or "ja_jp" or "jpn" => "jpn",
            "ko" or "ko_kr" or "kor" => "kor",
            "de" or "de_de" or "deu" => "deu",
            "es" or "es_es" or "esp" => "esp",
            "fr" or "fr_fr" or "fra" => "fra",
            "it" or "it_it" or "ita" => "ita",
            "pl" or "pl_pl" or "pol" => "pol",
            "pt" or "pt_br" or "ptb" => "ptb",
            "ru" or "ru_ru" or "rus" => "rus",
            "th" or "th_th" or "tha" => "tha",
            "tr" or "tr_tr" or "tur" => "tur",
            _ => text,
        };
    }

    private static bool TryGetLocalizationPathParts(
        string path,
        out string language,
        out string table,
        out string? root,
        out bool isI18NFile)
    {
        language = string.Empty;
        table = string.Empty;
        root = null;
        isI18NFile = false;

        var normalized = path.Replace('\\', '/');
        var marker = "/localization/";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return false;

        root = path.Substring(0, markerIndex + marker.Length - 1);
        var relative = normalized.Substring(markerIndex + marker.Length);
        var parts = relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            language = NormalizeLanguageCode(Path.GetFileNameWithoutExtension(parts[0]));
            table = I18NTable;
            isI18NFile = true;
            return !string.IsNullOrWhiteSpace(language);
        }

        if (parts.Length >= 2)
        {
            language = NormalizeLanguageCode(parts[0]);
            table = Path.GetFileNameWithoutExtension(parts[1]);
            return !string.IsNullOrWhiteSpace(language) && !string.IsNullOrWhiteSpace(table);
        }

        return false;
    }

    private static bool IsIgnoredPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/.git/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/.godot/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddAssetPath(string path, string? projectDirectory, HashSet<string> relativePaths)
    {
        var extension = Path.GetExtension(path);
        if (!AssetExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) &&
            !path.EndsWith(".guids.txt", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".theme.json", StringComparison.OrdinalIgnoreCase))
            return;

        if (!TryGetProjectRelativePath(path, projectDirectory, out var relativePath))
            return;

        relativePaths.Add(relativePath);
    }

    private static string NormalizeResourcePath(string resourcePath)
    {
        var text = resourcePath.Trim().Replace('\\', '/');
        if (text.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            text = text.Substring("res://".Length);
        else if (text.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
            text = text.Substring("user://".Length);

        return text.TrimStart('/');
    }

    private static string? GetBuildProperty(AnalyzerOptions options, string name)
    {
        return options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue($"build_property.{name}", out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static bool TryGetProjectRelativePath(string path, string? projectDirectory, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return false;

        var root = Path.GetFullPath(projectDirectory!);
        var fullPath = Path.GetFullPath(path);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                                root.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            return false;

        relativePath = fullPath.Substring(rootWithSeparator.Length).Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(relativePath) && !relativePath.StartsWith("../", StringComparison.Ordinal);
    }

    private static string CombinePath(params string[] parts)
    {
        return string.Join(Path.DirectorySeparatorChar.ToString(), parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    internal sealed class ModManifestInfo
    {
        private ModManifestInfo(string? path, string? modId, bool dependsOnRitsuLib)
        {
            Path = path;
            ModId = modId;
            DependsOnRitsuLib = dependsOnRitsuLib;
        }

        public string? Path { get; }
        public string? ModId { get; }
        public bool DependsOnRitsuLib { get; }
        public bool Exists => Path != null;

        public static ModManifestInfo None { get; } = new(null, null, false);

        public static ModManifestInfo Parse(string path, string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new(path, null, false);

            var modId = JsonTopLevelKeyScanner.ReadStringProperty(json!, "id");
            var dependencies = JsonTopLevelKeyScanner.ReadStringArrayProperty(json!, "dependencies");
            var depends = dependencies.Any(value => string.Equals(value, "STS2-RitsuLib", StringComparison.OrdinalIgnoreCase));
            return new(path, modId, depends);
        }
    }

    internal readonly struct RequiredLocalizationKey
    {
        public RequiredLocalizationKey(string table, string key)
        {
            Table = table;
            Key = key;
        }

        public string Table { get; }
        public string Key { get; }
    }

    internal readonly struct LocalizationTemplate
    {
        public LocalizationTemplate(string table, string keyTemplate)
        {
            Table = table;
            KeyTemplate = keyTemplate;
        }

        public string Table { get; }
        private string KeyTemplate { get; }

        public string Resolve(string id)
        {
            return KeyTemplate.Replace("{id}", id);
        }
    }

    internal static class JsonTopLevelKeyScanner
    {
        public static HashSet<string> ReadKeys(string json)
        {
            HashSet<string> keys = new(StringComparer.Ordinal);
            var properties = ReadTopLevelProperties(json);
            foreach (var property in properties)
                keys.Add(property.Key);
            return keys;
        }

        public static string? ReadStringProperty(string json, string propertyName)
        {
            foreach (var property in ReadTopLevelProperties(json))
            {
                if (!string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var index = property.ValueStart;
                SkipWhiteSpace(json, ref index);
                return index < json.Length && json[index] == '"' ? ReadString(json, ref index) : null;
            }

            return null;
        }

        public static ImmutableArray<string> ReadStringArrayProperty(string json, string propertyName)
        {
            foreach (var property in ReadTopLevelProperties(json))
            {
                if (!string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var index = property.ValueStart;
                SkipWhiteSpace(json, ref index);
                if (index >= json.Length || json[index] != '[')
                    return ImmutableArray<string>.Empty;

                index++;
                var builder = ImmutableArray.CreateBuilder<string>();
                while (index < json.Length)
                {
                    SkipWhiteSpaceAndCommas(json, ref index);
                    if (index >= json.Length || json[index] == ']')
                        break;

                    if (json[index] == '"')
                        builder.Add(ReadString(json, ref index));
                    else
                        SkipValue(json, ref index);
                }

                return builder.ToImmutable();
            }

            return ImmutableArray<string>.Empty;
        }

        private static List<JsonPropertySpan> ReadTopLevelProperties(string json)
        {
            List<JsonPropertySpan> properties = new();
            var index = 0;
            SkipWhiteSpace(json, ref index);
            if (index >= json.Length || json[index] != '{')
                return properties;

            index++;
            while (index < json.Length)
            {
                SkipWhiteSpaceAndCommas(json, ref index);
                if (index >= json.Length || json[index] == '}')
                    break;

                if (json[index] != '"')
                {
                    index++;
                    continue;
                }

                var key = ReadString(json, ref index);
                SkipWhiteSpace(json, ref index);
                if (index >= json.Length || json[index] != ':')
                    continue;

                index++;
                SkipWhiteSpace(json, ref index);
                properties.Add(new(key, index));
                SkipValue(json, ref index);
            }

            return properties;
        }

        private static void SkipValue(string json, ref int index)
        {
            SkipWhiteSpace(json, ref index);
            if (index >= json.Length)
                return;

            if (json[index] == '"')
            {
                _ = ReadString(json, ref index);
                return;
            }

            if (json[index] is '{' or '[')
            {
                var stack = new Stack<char>();
                stack.Push(json[index] == '{' ? '}' : ']');
                index++;
                while (index < json.Length && stack.Count > 0)
                {
                    if (json[index] == '"')
                    {
                        _ = ReadString(json, ref index);
                        continue;
                    }

                    if (json[index] is '{' or '[')
                        stack.Push(json[index] == '{' ? '}' : ']');
                    else if (json[index] == stack.Peek())
                        stack.Pop();

                    index++;
                }

                return;
            }

            while (index < json.Length && json[index] is not ',' and not '}' and not ']')
                index++;
        }

        private static string ReadString(string json, ref int index)
        {
            StringBuilder builder = new();
            if (index < json.Length && json[index] == '"')
                index++;

            while (index < json.Length)
            {
                var ch = json[index++];
                if (ch == '"')
                    break;

                if (ch != '\\' || index >= json.Length)
                {
                    builder.Append(ch);
                    continue;
                }

                var escaped = json[index++];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escaped);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u' when index + 4 <= json.Length:
                        var hex = json.Substring(index, 4);
                        if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
                            builder.Append((char)value);
                        index += 4;
                        break;
                    default:
                        builder.Append(escaped);
                        break;
                }
            }

            return builder.ToString();
        }

        private static void SkipWhiteSpaceAndCommas(string json, ref int index)
        {
            while (index < json.Length && (char.IsWhiteSpace(json[index]) || json[index] == ','))
                index++;
        }

        private static void SkipWhiteSpace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }

        private readonly struct JsonPropertySpan
        {
            public JsonPropertySpan(string key, int valueStart)
            {
                Key = key;
                ValueStart = valueStart;
            }

            public string Key { get; }
            public int ValueStart { get; }
        }
    }
}
