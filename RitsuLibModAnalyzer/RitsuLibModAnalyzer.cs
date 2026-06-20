using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class RitsuLibModAnalyzer : DiagnosticAnalyzer
{
    public const string MissingLocalizationId = RitsuLibDiagnostics.MissingLocalizationId;

    private const string I18NTable = "__ritsulib_i18n__";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        RitsuLibDiagnostics.CreateSupported();

    private static DiagnosticDescriptor CreateMissingLocalizationRule()
    {
        return RitsuLibDiagnostics.MissingLocalizationRule;
    }

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        var additionalFiles = RitsuLibAdditionalFileIndex.Create(context);
        var state = new CompilationState(
            ReadLocalization(context),
            additionalFiles,
            CompilationState.ReadFallbackOwner(context));

        context.RegisterSyntaxNodeAction(state.AnalyzeAttribute, SyntaxKind.Attribute);
        context.RegisterSyntaxNodeAction(state.AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(state.AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
        context.RegisterSyntaxNodeAction(state.AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);
        context.RegisterCompilationEndAction(state.ReportCompilationEnd);
    }

    private static LocalizationData ReadLocalization(CompilationStartAnalysisContext context)
    {
        Dictionary<string, Dictionary<string, HashSet<string>>> tableKeysByLanguage = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> i18NKeysByLanguage = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Dictionary<string, string>> tablePathsByLanguage = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> i18NPathsByLanguage = new(StringComparer.OrdinalIgnoreCase);
        List<string> roots = new();
        HashSet<string> directoryLanguages = new(StringComparer.OrdinalIgnoreCase);
        AddProjectLocalizationRoots(context, roots);

        foreach (var file in context.Options.AdditionalFiles)
        {
            var path = file.Path;
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryGetLocalizationPathParts(path, out var language, out var table, out var root, out var isI18NFile))
                continue;

            if (!string.IsNullOrWhiteSpace(root) && !roots.Contains(root!, StringComparer.OrdinalIgnoreCase))
                roots.Add(root!);

            var text = file.GetText(context.CancellationToken)?.ToString();
            var keys = string.IsNullOrWhiteSpace(text)
                ? new HashSet<string>(StringComparer.Ordinal)
                : JsonTopLevelKeyScanner.ReadKeys(text!);

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

        AddLocalizationDirectoryLanguages(roots, directoryLanguages);

        return new LocalizationData(tableKeysByLanguage, i18NKeysByLanguage, tablePathsByLanguage, i18NPathsByLanguage, roots, directoryLanguages);
    }

    private static void AddProjectLocalizationRoots(
        CompilationStartAnalysisContext context,
        List<string> roots)
    {
        foreach (var projectDirectory in GetProjectDirectories(context.Options))
        {
            if (!Directory.Exists(projectDirectory))
                continue;

            foreach (var localizationRoot in EnumerateLocalizationRoots(projectDirectory))
                AddLocalizationRoot(roots, localizationRoot);
        }

        foreach (var localizationRoot in EnumerateSourceAncestorLocalizationRoots(context.Compilation))
            AddLocalizationRoot(roots, localizationRoot);
    }

    private static void AddLocalizationDirectoryLanguages(
        List<string> roots,
        HashSet<string> languages)
    {
        foreach (var localizationRoot in roots.ToArray())
        {
            if (!Directory.Exists(localizationRoot))
                continue;

            foreach (var languageDirectory in Directory.EnumerateDirectories(localizationRoot))
            {
                var language = NormalizeLanguageCode(Path.GetFileName(languageDirectory));
                if (!string.IsNullOrWhiteSpace(language))
                    languages.Add(language);
            }
        }
    }

    private static IEnumerable<string> GetProjectDirectories(AnalyzerOptions options)
    {
        foreach (var propertyName in new[] { "MSBuildProjectDirectory", "ProjectDir" })
        {
            var directory = NormalizeDirectory(GetBuildProperty(options, propertyName));
            if (directory != null)
                yield return directory;
        }

        var projectFullPath = GetBuildProperty(options, "MSBuildProjectFullPath");
        if (!string.IsNullOrWhiteSpace(projectFullPath))
        {
            string? projectDirectory = null;
            try
            {
                projectDirectory = Path.GetDirectoryName(projectFullPath!);
            }
            catch
            {
                projectDirectory = null;
            }

            projectDirectory = NormalizeDirectory(projectDirectory);
            if (projectDirectory != null)
                yield return projectDirectory;
        }
    }

    private static IEnumerable<string> EnumerateLocalizationRoots(string projectDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(projectDirectory, "localization", SearchOption.AllDirectories))
        {
            if (IsIgnoredLocalizationRoot(directory))
                continue;

            yield return directory;
        }
    }

    private static IEnumerable<string> EnumerateSourceAncestorLocalizationRoots(Compilation compilation)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var directory = NormalizeDirectory(Path.GetDirectoryName(syntaxTree.FilePath));
            while (directory != null && visited.Add(directory))
            {
                var localizationRoot = Path.Combine(directory, "localization");
                if (Directory.Exists(localizationRoot) && !IsIgnoredLocalizationRoot(localizationRoot))
                    yield return localizationRoot;

                try
                {
                    directory = Directory.GetParent(directory)?.FullName;
                }
                catch
                {
                    directory = null;
                }
            }
        }
    }

    private static bool IsIgnoredLocalizationRoot(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/.git/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/.godot/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/.idea/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/.vs/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddLocalizationRoot(List<string> roots, string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || IsIgnoredLocalizationRoot(root!))
            return;

        if (!roots.Contains(root!, StringComparer.OrdinalIgnoreCase))
            roots.Add(root!);
    }

    private static string? NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        try
        {
            return Path.GetFullPath(directory!);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetBuildProperty(AnalyzerOptions options, string name)
    {
        return options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue($"build_property.{name}", out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
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

        // Direct I18N layout: localization/<lang>.json  (single segment, basename is a known language code).
        if (parts.Length == 1)
        {
            var basename = Path.GetFileNameWithoutExtension(parts[0]);
            if (!IsKnownLanguageSegment(basename))
                return false;

            language = NormalizeLanguageCode(basename);
            table = I18NTable;
            isI18NFile = true;
            return !string.IsNullOrWhiteSpace(language);
        }

        if (parts.Length < 2)
            return false;

        // Game LocTable layout: localization/<lang>/<table>.json  (first segment is a known language code).
        if (IsKnownLanguageSegment(parts[0]))
        {
            language = NormalizeLanguageCode(parts[0]);
            table = Path.GetFileNameWithoutExtension(parts[1]);
            return !string.IsNullOrWhiteSpace(language) && !string.IsNullOrWhiteSpace(table);
        }

        // I18N bridge feature layout: localization/<feature>/.../<lang>.json
        // (first segment is NOT a language code; file basename IS). RitsuLib I18N.pckFolders maps to any path.
        var i18NLanguage = Path.GetFileNameWithoutExtension(parts[parts.Length - 1]);
        if (!IsKnownLanguageSegment(i18NLanguage))
            return false;

        language = NormalizeLanguageCode(i18NLanguage);
        table = I18NTable;
        isI18NFile = true;
        return !string.IsNullOrWhiteSpace(language);
    }

    /// <summary>
    ///     Returns true when <paramref name="segment" /> normalizes to a canonical language code recognized by
    ///     <see cref="STS2RitsuLib.Utils.I18N.NormalizeLanguageCode" /> (eng, zhs, jpn, kor, deu, esp, fra, ita, pol,
    ///     ptb, rus, tha, tur). Used to disambiguate game LocTable directories (where the first path segment after
    ///     <c>/localization/</c> is a language) from I18N bridge folders (where it is a feature name).
    /// </summary>
    private static bool IsKnownLanguageSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return false;

        var normalized = (segment ?? string.Empty).Trim().Replace('-', '_').ToLowerInvariant();
        return normalized switch
        {
            "zh_cn" or "zh_hans" or "zh_sg" or "zh" or "zhs" => true,
            "en_us" or "en_gb" or "en" or "eng" => true,
            "ja" or "ja_jp" or "jpn" => true,
            "ko" or "ko_kr" or "kor" => true,
            "de" or "de_de" or "deu" => true,
            "es" or "es_es" or "esp" => true,
            "fr" or "fr_fr" or "fra" => true,
            "it" or "it_it" or "ita" => true,
            "pl" or "pl_pl" or "pol" => true,
            "pt" or "pt_br" or "ptb" => true,
            "ru" or "ru_ru" or "rus" => true,
            "th" or "th_th" or "tha" => true,
            "tr" or "tr_tr" or "tur" => true,
            _ => false,
        };
    }

    private static string GetCompoundId(string modId, string typeStem, string localStem)
    {
        return RitsuLibSyntaxFacts.GetCompoundId(modId, typeStem, localStem);
    }

    private static string GetModelEntry(string modId, string categoryStem, string typeName, PublicEntryOverride publicEntryOverride)
    {
        if (publicEntryOverride.Kind == PublicEntryOverrideKind.FullEntry)
            return NormalizeFullPublicEntry(publicEntryOverride.Value!);

        var localStem = publicEntryOverride.Kind == PublicEntryOverrideKind.Stem
            ? publicEntryOverride.Value!
            : typeName;

        return GetCompoundId(modId, categoryStem, localStem);
    }

    private static string NormalizePublicStem(string value)
    {
        return RitsuLibSyntaxFacts.NormalizePublicStem(value);
    }

    private static string NormalizeFullPublicEntry(string value)
    {
        return RitsuLibSyntaxFacts.NormalizeFullPublicEntry(value);
    }

    private static string NormalizeLanguageCode(string? language)
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

    private sealed partial class CompilationState
    {
        private readonly LocalizationData _localization;
        private readonly RitsuLibAdditionalFileIndex _additionalFiles;
        private readonly string? _fallbackOwner;
        private readonly object _gate = new();
        private readonly List<LocalizationRequirement> _requirements = new();
        private readonly List<OwnedModel> _characters = new();
        private readonly List<OwnedModel> _ancients = new();
        private readonly HashSet<string> _assemblyModIds = new(StringComparer.OrdinalIgnoreCase);

        public CompilationState(
            LocalizationData localization,
            RitsuLibAdditionalFileIndex additionalFiles,
            string? fallbackOwner)
        {
            _localization = localization;
            _additionalFiles = additionalFiles;
            _fallbackOwner = fallbackOwner;
            if (!string.IsNullOrWhiteSpace(fallbackOwner))
                _assemblyModIds.Add(fallbackOwner!);
        }

        public static string? ReadFallbackOwner(CompilationStartAnalysisContext context)
        {
            HashSet<string> modIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (var tree in context.Compilation.SyntaxTrees)
            {
                var root = tree.GetRoot(context.CancellationToken);
                var semanticModel = context.Compilation.GetSemanticModel(tree);
                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var method = semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
                    var methodName = method?.Name ?? GetInvokedMemberName(invocation);
                    if (methodName == null)
                        continue;

                    if (!IsRegisterModAssembly(methodName, method))
                        continue;

                    var modId = GetInvocationStringArgument(invocation, method, "modId", 0, semanticModel, context.CancellationToken);
                    if (!string.IsNullOrWhiteSpace(modId))
                        modIds.Add(modId!);
                }
            }

            return modIds.Count == 1 ? modIds.Single() : null;
        }

        public void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
        {
            var attribute = (AttributeSyntax)context.Node;
            var constructor = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol as IMethodSymbol;
            var attributeType = constructor?.ContainingType;
            var attributeName = attributeType?.Name ?? GetAttributeShortName(attribute);
            if (attributeName == null)
                return;

            AnalyzeContractAttribute(attributeName, attribute, context);
            AnalyzeOwnedRegistrationAttribute(attributeName, attribute, context);
            AnalyzeContentRegistrationAttribute(attributeName, attribute, context);
        }

        public void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            var method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
            var methodName = method?.Name ?? GetInvokedMemberName(invocation);
            if (methodName == null)
                return;

            if (IsRegisterModAssembly(methodName, method))
            {
                var modId = GetInvocationStringArgument(invocation, method, "modId", 0, context.SemanticModel, context.CancellationToken);
                if (!string.IsNullOrWhiteSpace(modId))
                    AddAssemblyModId(modId!);
                return;
            }

            AnalyzeContractInvocation(invocation, method, methodName, context);
            AnalyzeOwnedRegistrationInvocation(invocation, method, methodName, context);
            AnalyzeContentRegistrationInvocation(invocation, method, methodName, context);
            AnalyzeI18NInvocation(invocation, method, methodName, context);
            AnalyzeSettingsLocalizationInvocation(invocation, method, methodName, context);
            AnalyzeAncientDialogueInvocation(invocation, method, methodName, context);
        }

        public void ReportCompilationEnd(CompilationAnalysisContext context)
        {
            LocalizationRequirement[] requirements;
            OwnedModel[] characters;
            OwnedModel[] ancients;
            string[] assemblyModIds;

            lock (_gate)
            {
                requirements = _requirements.ToArray();
                characters = _characters.ToArray();
                ancients = _ancients.ToArray();
                assemblyModIds = _assemblyModIds.ToArray();
            }

            var fallbackOwner = _fallbackOwner ?? (assemblyModIds.Length == 1 ? assemblyModIds[0] : null);
            foreach (var requirement in requirements)
                ReportMissingLocalization(context, requirement.ResolveOwner(fallbackOwner));

            foreach (var ancient in ancients)
            {
                var ancientModel = ancient.ResolveOwner(fallbackOwner);
                if (ancientModel == null)
                    continue;

                foreach (var character in characters)
                {
                    var characterModel = character.ResolveOwner(fallbackOwner);
                    if (characterModel == null)
                        continue;

                    var baseKey = $"{ancientModel.Entry}.talk.{characterModel.Entry}.";
                    var requirement = LocalizationRequirement.TableAnyOf(
                        "Ancient dialogue",
                        $"{ancientModel.Entry} -> {characterModel.Entry}",
                        ancient.Location,
                        "ancients",
                        "dialogue:" + baseKey,
                        ImmutableArray.Create(
                            $"{baseKey}0-0.ancient",
                            $"{baseKey}0-0r.ancient",
                            $"{baseKey}0-0.char",
                            $"{baseKey}0-0r.char"));
                    ReportMissingLocalization(context, requirement);
                }
            }

            ReportAncientDialogueRepeatMixedDiagnostics(context);
            ReportUnknownLocalizationTableDiagnostics(context);
        }

        /// <summary>
        ///     RITSU002: Each ancient dialogue sequence (<c>base{idx}-{line}[r].(ancient|char)</c>) must keep its
        ///     line keys uniformly using or omitting the trailing 'r'. The vanilla resolver
        ///     (<see cref="STS2RitsuLib.Localization.AncientDialogueLocalization.ExistingLine" />) prefers the 'r'
        ///     variant first, so a half-and-half segment causes some lines to be silently skipped.
        /// </summary>
        private void ReportAncientDialogueRepeatMixedDiagnostics(CompilationAnalysisContext context)
        {
            foreach (var language in _localization.Languages)
            {
                var keys = _localization.GetTableKeys(language, "ancients");
                if (keys.Count == 0)
                    continue;

                var groups = new Dictionary<string, AncientDialogueRGroup>(StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    if (!TryParseAncientDialogueLineKey(key, out var sequencePrefix, out var hasRepeatSuffix))
                        continue;

                    if (!groups.TryGetValue(sequencePrefix, out var group))
                    {
                        group = new AncientDialogueRGroup();
                        groups[sequencePrefix] = group;
                    }

                    if (hasRepeatSuffix)
                        group.WithRepeat.Add(key);
                    else
                        group.WithoutRepeat.Add(key);
                }

                foreach (var pair in groups)
                {
                    var group = pair.Value;
                    if (group.WithRepeat.Count == 0 || group.WithoutRepeat.Count == 0)
                        continue;

                    var path = _localization.GetTargetPath(language, "ancients", isI18N: false);
                    var location = TryCreateFileLocation(path) ?? Location.None;
                    var mixedKeys = group.WithRepeat
                        .Concat(group.WithoutRepeat)
                        .OrderBy(key => key, StringComparer.Ordinal);
                    context.ReportDiagnostic(Diagnostic.Create(
                        RitsuLibDiagnostics.AncientDialogueRepeatMixedRule,
                        location,
                        pair.Key,
                        path,
                        string.Join(", ", mixedKeys)));
                }
            }
        }

        /// <summary>
        ///     RITSU003: Game LocManager only loads JSON tables whose names match its known set. Any
        ///     <c>localization/&lt;lang&gt;/&lt;X&gt;.json</c> file whose <c>&lt;X&gt;</c> is not in
        ///     <see cref="KnownLocalizationTableNames" /> is silently ignored at runtime — flag it as Info so users
        ///     catch typos like <c>cardz.json</c>.
        /// </summary>
        private void ReportUnknownLocalizationTableDiagnostics(CompilationAnalysisContext context)
        {
            foreach (var (language, table, path) in _localization.EnumerateTableFiles())
            {
                if (string.IsNullOrWhiteSpace(table))
                    continue;

                if (KnownLocalizationTableNames.Contains(table))
                    continue;

                var location = TryCreateFileLocation(path) ?? Location.None;
                var displayPath = !string.IsNullOrWhiteSpace(path) ? path : $"{language}/{table}.json";
                context.ReportDiagnostic(Diagnostic.Create(
                    RitsuLibDiagnostics.UnknownLocalizationTableRule,
                    location,
                    displayPath,
                    table));
            }
        }

        /// <summary>
        ///     Whitelist of game LocTable names recognized by the vanilla LocManager and RitsuLib content registry.
        ///     Sourced from the docs and verified against shipping mod JSON layouts.
        /// </summary>
        private static readonly HashSet<string> KnownLocalizationTableNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "cards",
            "relics",
            "potions",
            "powers",
            "characters",
            "events",
            "ancients",
            "encounters",
            "acts",
            "monsters",
            "orbs",
            "enchantments",
            "afflictions",
            "card_keywords",
            "static_hover_tips",
            "epochs",
            "achievements",
            "stories",
        };

        private static bool TryParseAncientDialogueLineKey(string key, out string sequencePrefix, out bool hasRepeatSuffix)
        {
            sequencePrefix = string.Empty;
            hasRepeatSuffix = false;

            // Expected shape: "<base>.<idx>-<line>[r].(ancient|char)"
            // Where <base> ends with a literal '.', and <idx>/<line> are non-empty digits.
            int speakerStart;
            if (key.EndsWith(".ancient", StringComparison.Ordinal))
                speakerStart = key.Length - ".ancient".Length;
            else if (key.EndsWith(".char", StringComparison.Ordinal))
                speakerStart = key.Length - ".char".Length;
            else
                return false;

            var beforeSpeaker = key.Substring(0, speakerStart);
            var hasR = beforeSpeaker.EndsWith("r", StringComparison.Ordinal);
            var lineEnd = hasR ? beforeSpeaker.Length - 1 : beforeSpeaker.Length;
            if (lineEnd <= 0)
                return false;

            // Walk back over the line digits.
            var lineStart = lineEnd;
            while (lineStart > 0 && IsAsciiDigit(beforeSpeaker[lineStart - 1]))
                lineStart--;
            if (lineStart == lineEnd || lineStart == 0 || beforeSpeaker[lineStart - 1] != '-')
                return false;

            // Walk back over the index digits.
            var dashIndex = lineStart - 1;
            var idxEnd = dashIndex;
            var idxStart = idxEnd;
            while (idxStart > 0 && IsAsciiDigit(beforeSpeaker[idxStart - 1]))
                idxStart--;
            if (idxStart == idxEnd || idxStart == 0 || beforeSpeaker[idxStart - 1] != '.')
                return false;

            sequencePrefix = beforeSpeaker.Substring(0, dashIndex + 1);
            hasRepeatSuffix = hasR;
            return true;
        }

        private static bool IsAsciiDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static Location? TryCreateFileLocation(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                return Location.Create(path!, new Microsoft.CodeAnalysis.Text.TextSpan(0, 0),
                    new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                        new Microsoft.CodeAnalysis.Text.LinePosition(0, 0),
                        new Microsoft.CodeAnalysis.Text.LinePosition(0, 0)));
            }
            catch
            {
                return null;
            }
        }

        private sealed class AncientDialogueRGroup
        {
            public List<string> WithRepeat { get; } = new();
            public List<string> WithoutRepeat { get; } = new();
        }

        private void AnalyzeOwnedRegistrationAttribute(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            if (!IsOwnedLocalizationAttribute(attributeName))
                return;

            var localStem = GetAttributeStringArgument(attribute, context.SemanticModel, 0, context.CancellationToken);
            if (string.IsNullOrWhiteSpace(localStem))
                return;

            var ownerModId = ResolveAttributeOwnerModId(attribute, context.SemanticModel, context.CancellationToken);
            var templates = CreateOwnedAttributeTemplates(attributeName, attribute, context.SemanticModel, context.CancellationToken);
            if (templates.Length == 0)
                return;

            AddRequirement(context, new CompoundLocalizationRequirement(
                GetRegistrationDisplayName(attributeName),
                localStem!,
                GetOwnedRegistrationKind(attributeName),
                templates,
                attribute.GetLocation(),
                ownerModId));
        }

        private void AnalyzeContentRegistrationAttribute(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            if (attributeName == "RegisterEpochAttribute")
            {
                var epochTypeDeclaration = attribute.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                var typeSymbol = epochTypeDeclaration == null
                    ? null
                    : context.SemanticModel.GetDeclaredSymbol(epochTypeDeclaration, context.CancellationToken) as INamedTypeSymbol;
                AddEpochRequirement(context, typeSymbol, attribute.GetLocation());
                return;
            }

            if (attributeName == "RegisterStoryEpochAttribute")
            {
                var epochTypeDeclaration = attribute.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                var typeSymbol = epochTypeDeclaration == null
                    ? null
                    : context.SemanticModel.GetDeclaredSymbol(epochTypeDeclaration, context.CancellationToken) as INamedTypeSymbol;
                AddEpochRequirement(context, typeSymbol, attribute.GetLocation());
                return;
            }

            if (attributeName == "RegisterStoryAttribute")
                return;

            if (!TryGetAttributeContentInfo(attributeName, out var info))
                return;

            var typeDeclaration = attribute.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (typeDeclaration == null)
                return;

            if (typeDeclaration.Modifiers.Any(SyntaxKind.AbstractKeyword))
                return;

            var ownerModId = ResolveAttributeOwnerModId(attribute, context.SemanticModel, context.CancellationToken);
            var publicEntry = ResolveAttributePublicEntry(attribute, context.SemanticModel, context.CancellationToken);
            AddModelRequirement(context, info, typeDeclaration.Identifier.ValueText, publicEntry, ownerModId, attribute.GetLocation());
        }

        private void AnalyzeOwnedRegistrationInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            var semanticModel = context.SemanticModel;
            var cancellationToken = context.CancellationToken;

            if (methodName == "RegisterCardKeywordOwnedByLocNamespace")
            {
                if (!IsRitsuLibOwnedRegistrationMethod(method, methodName, invocation, semanticModel, cancellationToken))
                    return;

                var ownerModId = ResolveReceiverModId(invocation, semanticModel, cancellationToken);
                var localStem = GetInvocationStringArgument(invocation, method, "localKeywordStem", 0, semanticModel, cancellationToken);
                AddCompoundRequirement(context, localStem, "KEYWORD", ownerModId, invocation.GetLocation(),
                    "card keyword", KeywordCardTemplates());
                return;
            }

            if (methodName == "CardKeywordOwnedByLocNamespace")
            {
                if (!IsRitsuLibOwnedRegistrationMethod(method, methodName, invocation, semanticModel, cancellationToken))
                    return;

                var ownerModId = ResolveReceiverModId(invocation, semanticModel, cancellationToken);
                var localStem = GetInvocationStringArgument(invocation, method, "localKeywordStem", 0, semanticModel, cancellationToken);
                AddCompoundRequirement(context, localStem, "KEYWORD", ownerModId, invocation.GetLocation(),
                    "card keyword", KeywordCardTemplates());
                return;
            }

            if (methodName == "KeywordOwned")
            {
                if (!IsRitsuLibOwnedRegistrationMethod(method, methodName, invocation, semanticModel, cancellationToken))
                    return;

                var ownerModId = ResolveReceiverModId(invocation, semanticModel, cancellationToken);
                var localStem = GetInvocationStringArgument(invocation, method, "localKeywordStem", 0, semanticModel, cancellationToken);
                AddCompoundRequirement(context, localStem, "KEYWORD", ownerModId, invocation.GetLocation(),
                    "keyword", CreateInvocationOwnedKeywordTemplates(invocation, method, semanticModel, cancellationToken));
                return;
            }

            if (methodName != "RegisterOwned")
                return;

            if (!IsRitsuLibOwnedRegistrationMethod(method, methodName, invocation, semanticModel, cancellationToken))
                return;

            var containingType = ResolveInvocationContainingType(invocation, method, semanticModel, cancellationToken);
            var receiverOwner = ResolveReceiverModId(invocation, semanticModel, cancellationToken);
            var stem = GetInvocationStringArgument(invocation, method, "localKeywordStem", 0, semanticModel, cancellationToken)
                       ?? GetInvocationStringArgument(invocation, method, "localStem", 0, semanticModel, cancellationToken)
                       ?? GetInvocationStringArgument(invocation, method, "localButtonStem", 0, semanticModel, cancellationToken)
                       ?? GetInvocationStringArgument(invocation, method, "localPileStem", 0, semanticModel, cancellationToken);

            switch (containingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            {
                case "STS2RitsuLib.Keywords.ModKeywordRegistry":
                    AddCompoundRequirement(context, stem, "KEYWORD", receiverOwner, invocation.GetLocation(),
                        "keyword", CreateInvocationOwnedKeywordTemplates(invocation, method, semanticModel, cancellationToken));
                    break;
                case "STS2RitsuLib.CardPiles.ModCardPileRegistry":
                    AddCompoundRequirement(context, stem, "CARDPILE", receiverOwner, invocation.GetLocation(),
                        "card pile", CardPileTemplates());
                    break;
                case "STS2RitsuLib.TopBar.ModTopBarButtonRegistry":
                    AddCompoundRequirement(context, stem, "TOPBARBUTTON", receiverOwner, invocation.GetLocation(),
                        "top-bar button", TopBarButtonTemplates());
                    break;
            }
        }

        private void AnalyzeContentRegistrationInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName is "RegisterStory" or "Story" &&
                (IsRitsuLibTimelineRegistrationInvocation(invocation, method, methodName, context.SemanticModel, context.CancellationToken) ||
                 IsRitsuLibContentRegistrationInvocation(invocation, method, methodName, context.SemanticModel, context.CancellationToken)))
            {
                return;
            }

            if (methodName == "RegisterStoryEpoch")
            {
                if (!IsRitsuLibTimelineRegistrationInvocation(invocation, method, methodName, context.SemanticModel, context.CancellationToken))
                    return;

                var typeSymbol = ResolveModelTypeSymbol(invocation, method, ContentRegistrationInfo.Epoch(modelTypeArgumentIndex: 1), context.SemanticModel, context.CancellationToken);
                AddEpochRequirement(context, typeSymbol, invocation.GetLocation());
                return;
            }

            if (methodName == "StoryEpoch")
            {
                if (!IsRitsuLibContentRegistrationInvocation(invocation, method, methodName, context.SemanticModel, context.CancellationToken))
                    return;

                var typeSymbol = ResolveModelTypeSymbol(invocation, method, ContentRegistrationInfo.Epoch(modelTypeArgumentIndex: 1), context.SemanticModel, context.CancellationToken);
                AddEpochRequirement(context, typeSymbol, invocation.GetLocation());
                return;
            }

            if (methodName is "RegisterEpoch" or "Epoch")
            {
                if (!IsRitsuLibTimelineRegistrationInvocation(invocation, method, methodName, context.SemanticModel, context.CancellationToken) &&
                    !IsRitsuLibContentRegistrationInvocation(invocation, method, methodName, context.SemanticModel, context.CancellationToken) &&
                    !IsRitsuLibTimelineColumnEpochInvocation(invocation, method, methodName, context.SemanticModel, context.CancellationToken))
                {
                    return;
                }

                var typeSymbol = ResolveModelTypeSymbol(invocation, method, ContentRegistrationInfo.Epoch(), context.SemanticModel, context.CancellationToken);
                AddEpochRequirement(context, typeSymbol, invocation.GetLocation());
                return;
            }

            if (!TryGetInvocationContentInfo(methodName, out var info))
                return;

            if (!IsRitsuLibContentRegistrationInvocation(
                    invocation,
                    method,
                    methodName,
                    context.SemanticModel,
                    context.CancellationToken))
                return;

            var ownerModId = ResolveReceiverModId(invocation, context.SemanticModel, context.CancellationToken);
            var typeName = ResolveModelTypeName(invocation, method, info, context.SemanticModel, context.CancellationToken);
            if (string.IsNullOrWhiteSpace(typeName))
                return;

            var publicEntry = ResolveInvocationPublicEntry(invocation, method, context.SemanticModel, context.CancellationToken);
            AddModelRequirement(context, info, typeName!, publicEntry, ownerModId, invocation.GetLocation());
        }

        private void AnalyzeI18NInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName is not ("Get" or "TryGet" or "ContainsKey"))
                return;

            if (!IsRitsuLibI18NMethod(method, invocation, context.SemanticModel, context.CancellationToken))
                return;

            var key = GetInvocationStringArgument(invocation, method, "key", 0, context.SemanticModel, context.CancellationToken);
            if (string.IsNullOrWhiteSpace(key))
                return;

            AddRequirement(context, LocalizationRequirement.I18N(
                "I18N",
                key!,
                invocation.GetLocation(),
                ImmutableArray.Create(key!)));
        }

        private void AnalyzeSettingsLocalizationInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName == "I18N" && IsRitsuLibModSettingsTextMethod(method, invocation, context.SemanticModel, context.CancellationToken))
            {
                var key = GetInvocationStringArgument(invocation, method, "key", 1, context.SemanticModel, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(key))
                    return;

                AddRequirement(context, LocalizationRequirement.I18N(
                    "ModSettings text",
                    key!,
                    invocation.GetLocation(),
                    ImmutableArray.Create(key!)));
                return;
            }

            if (methodName != "LocString" ||
                !IsRitsuLibModSettingsTextMethod(method, invocation, context.SemanticModel, context.CancellationToken))
                return;

            var table = GetInvocationStringArgument(invocation, method, "table", 0, context.SemanticModel, context.CancellationToken);
            var key2 = GetInvocationStringArgument(invocation, method, "key", 1, context.SemanticModel, context.CancellationToken);
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(key2))
                return;

            AddRequirement(context, LocalizationRequirement.Table(
                "ModSettings text",
                key2!,
                invocation.GetLocation(),
                table!,
                ImmutableArray.Create(key2!)));
        }

        private void AnalyzeAncientDialogueInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName == "GetDialoguesForKey")
            {
                if (!IsRitsuLibAncientDialogueMethod(method, invocation, context.SemanticModel, context.CancellationToken))
                    return;

                var table = GetInvocationStringArgument(invocation, method, "locTable", 0, context.SemanticModel, context.CancellationToken);
                var baseKey = GetInvocationStringArgument(invocation, method, "baseKey", 1, context.SemanticModel, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(baseKey))
                    return;

                AddRequirement(context, LocalizationRequirement.TableAnyOf(
                    "Ancient dialogue",
                    baseKey!,
                    invocation.GetLocation(),
                    table!,
                    "dialogue:" + baseKey,
                    ImmutableArray.Create(
                        $"{baseKey}0-0.ancient",
                        $"{baseKey}0-0r.ancient",
                        $"{baseKey}0-0.char",
                        $"{baseKey}0-0r.char")));
                return;
            }

            // BuildDialogueSetForModAncient: firstVisitEver and ANY dialogues are optional per
            // AncientDialogueLocalization (firstVisitSequences.Count > 0 ? ... : null; agnostic list may be empty).
            // No hard requirement to emit here.
        }

        private void AddCompoundRequirement(
            SyntaxNodeAnalysisContext context,
            string? localStem,
            string kind,
            string? ownerModId,
            Location location,
            string displayName,
            ImmutableArray<LocalizationTemplate> templates)
        {
            if (string.IsNullOrWhiteSpace(localStem) || templates.Length == 0)
                return;

            AddRequirement(context, new CompoundLocalizationRequirement(
                displayName,
                localStem!,
                kind,
                templates,
                location,
                ownerModId));
        }

        private void AddModelRequirement(
            SyntaxNodeAnalysisContext context,
            ContentRegistrationInfo info,
            string typeName,
            PublicEntryOverride publicEntryOverride,
            string? ownerModId,
            Location location)
        {
            if (info.DisplayName == null || info.Templates.Length == 0)
                return;

            var model = new OwnedModel(info.DisplayName, info.CategoryStem, typeName, publicEntryOverride, ownerModId, location);
            AddRequirement(context, new ModelLocalizationRequirement(model, info.Templates));

            if (info.CategoryStem == "CHARACTER")
                AddCharacter(model);
            else if (info.CategoryStem == "ANCIENT")
                AddAncient(model);
        }

        private void AddEpochRequirement(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol? epochType,
            Location location)
        {
            var epochId = ResolveConstantStringProperty(epochType, "Id", context.Compilation, context.CancellationToken);
            if (string.IsNullOrWhiteSpace(epochId))
                return;

            var id = epochId!.Trim();
            AddRequirement(context, LocalizationRequirement.Table(
                "epoch",
                id,
                location,
                "epochs",
                ImmutableArray.Create(
                    $"{id}.title",
                    $"{id}.description",
                    $"{id}.unlockInfo",
                    $"{id}.unlockText",
                    $"{id}.unlock")));
        }

        private void ReportMissingLocalization(SyntaxNodeAnalysisContext context, LocalizationRequirement? requirement)
        {
            foreach (var diagnostic in CreateMissingLocalizationDiagnostics(requirement))
                context.ReportDiagnostic(diagnostic);
        }

        private void ReportMissingLocalization(CompilationAnalysisContext context, LocalizationRequirement? requirement)
        {
            foreach (var diagnostic in CreateMissingLocalizationDiagnostics(requirement))
                context.ReportDiagnostic(diagnostic);
        }

        private IEnumerable<Diagnostic> CreateMissingLocalizationDiagnostics(LocalizationRequirement? requirement)
        {
            if (requirement == null || requirement.Keys.Length == 0 || _localization.Languages.Length == 0)
                yield break;

            var records = new List<MissingLocalizationRecord>();
            var highestSeverity = DiagnosticSeverity.Hidden;
            foreach (var language in _localization.Languages)
            {
                foreach (var group in requirement.Keys.GroupBy(key => key.Table, StringComparer.OrdinalIgnoreCase))
                {
                    var isI18N = string.Equals(group.Key, I18NTable, StringComparison.OrdinalIgnoreCase);
                    var missing = ResolveMissingKeys(language, isI18N, group)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(key => key, StringComparer.Ordinal)
                        .ToArray();

                    if (missing.Length == 0)
                        continue;

                    var table = isI18N ? string.Empty : group.Key;
                    var targetPath = _localization.GetTargetPath(language, table, isI18N);
                    var displayPath = isI18N ? $"{language}.json" : $"{language}/{table}.json";
                    var severity = missing
                        .Select(key => GetMissingLocalizationSeverity(language, isI18N, table, key))
                        .OrderByDescending(GetSeverityRank)
                        .First();
                    highestSeverity = GetSeverityRank(severity) > GetSeverityRank(highestSeverity) ? severity : highestSeverity;
                    records.Add(new MissingLocalizationRecord(language, table, isI18N, targetPath, displayPath, missing));
                }
            }

            if (records.Count == 0)
                yield break;

            var properties = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
            var first = records[0];
            properties[RitsuLibDiagnosticProperties.Language] = first.Language;
            properties[RitsuLibDiagnosticProperties.Table] = first.Table;
            properties[RitsuLibDiagnosticProperties.IsI18N] = first.IsI18N ? "true" : "false";
            properties[RitsuLibDiagnosticProperties.TargetPath] = first.TargetPath;
            properties[RitsuLibDiagnosticProperties.Keys] = JoinList(first.Keys);
            properties[RitsuLibDiagnosticProperties.Values] = JoinList(first.Keys.Select(_ => string.Empty));
            properties[RitsuLibDiagnosticProperties.Languages] = JoinRecords(records.Select(record => record.Language));
            properties[RitsuLibDiagnosticProperties.Tables] = JoinRecords(records.Select(record => record.Table));
            properties[RitsuLibDiagnosticProperties.IsI18NValues] = JoinRecords(records.Select(record => record.IsI18N ? "true" : "false"));
            properties[RitsuLibDiagnosticProperties.TargetPaths] = JoinRecords(records.Select(record => record.TargetPath));
            properties[RitsuLibDiagnosticProperties.KeyGroups] = JoinRecords(records.Select(record => JoinList(record.Keys)));
            properties[RitsuLibDiagnosticProperties.ValueGroups] = JoinRecords(records.Select(record => JoinList(record.Keys.Select(_ => string.Empty))));
            properties[RitsuLibDiagnosticProperties.PrimaryFixSource] = "true";

            yield return Diagnostic.Create(
                CreateMissingLocalizationRule(),
                requirement.Location,
                highestSeverity,
                additionalLocations: null,
                properties: properties.ToImmutable(),
                requirement.DisplayName,
                requirement.Subject,
                records.Sum(record => record.Keys.Length),
                string.Join(", ", records.Select(record => record.DisplayPath).Distinct(StringComparer.OrdinalIgnoreCase)),
                string.Join(", ", records.SelectMany(record => record.Keys).Distinct(StringComparer.Ordinal)));
        }

        private static int GetSeverityRank(DiagnosticSeverity severity)
        {
            return severity switch
            {
                DiagnosticSeverity.Error => 4,
                DiagnosticSeverity.Warning => 3,
                DiagnosticSeverity.Info => 2,
                DiagnosticSeverity.Hidden => 1,
                _ => 0,
            };
        }

        private static string JoinList(IEnumerable<string> values)
        {
            return string.Join(RitsuLibDiagnosticProperties.ListSeparator, values);
        }

        private static string JoinRecords(IEnumerable<string> values)
        {
            return string.Join(RitsuLibDiagnosticProperties.RecordSeparator, values);
        }

        private bool IsMissingLocalizationKey(string language, bool isI18N, RequiredLocalizationKey key)
        {
            return isI18N
                ? !_localization.ContainsI18N(language, key.Key)
                : !_localization.ContainsTable(language, key.Table, key.Key);
        }

        /// <summary>
        ///     Filters a single-table group of required keys down to those actually missing in <paramref name="language" />,
        ///     honoring the <see cref="RequiredLocalizationKey.AlternativeGroup" /> "any-of" relation: keys sharing the
        ///     same non-null group are collectively satisfied if any one member is present, and report only the canonical
        ///     (first) member when all are missing.
        /// </summary>
        private IEnumerable<string> ResolveMissingKeys(
            string language,
            bool isI18N,
            IEnumerable<RequiredLocalizationKey> tableGroup)
        {
            var ungrouped = new List<RequiredLocalizationKey>();
            var grouped = new Dictionary<string, List<RequiredLocalizationKey>>(StringComparer.Ordinal);

            foreach (var key in tableGroup)
            {
                if (string.IsNullOrEmpty(key.AlternativeGroup))
                {
                    ungrouped.Add(key);
                    continue;
                }

                if (!grouped.TryGetValue(key.AlternativeGroup!, out var members))
                {
                    members = new List<RequiredLocalizationKey>();
                    grouped[key.AlternativeGroup!] = members;
                }

                members.Add(key);
            }

            foreach (var key in ungrouped)
                if (IsMissingLocalizationKey(language, isI18N, key))
                    yield return key.Key;

            foreach (var members in grouped.Values)
                if (members.All(member => IsMissingLocalizationKey(language, isI18N, member)))
                    yield return members[0].Key;
        }

        private DiagnosticSeverity GetMissingLocalizationSeverity(string language, bool isI18N, string table, string key)
        {
            if (string.Equals(RitsuLibAdditionalFileIndex.NormalizeLanguageCode(language), "eng", StringComparison.OrdinalIgnoreCase))
                return AnyNonEnglishLanguageHasKey(isI18N, table, key)
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Error;

            var fallbackHasKey = isI18N
                ? _localization.ContainsI18N("eng", key)
                : _localization.ContainsTable("eng", table, key);

            return fallbackHasKey ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error;
        }

        private bool AnyNonEnglishLanguageHasKey(bool isI18N, string table, string key)
        {
            foreach (var language in _localization.Languages)
            {
                if (string.Equals(RitsuLibAdditionalFileIndex.NormalizeLanguageCode(language), "eng", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (isI18N
                    ? _localization.ContainsI18N(language, key)
                    : _localization.ContainsTable(language, table, key))
                {
                    return true;
                }
            }

            return false;
        }

        private static ImmutableArray<LocalizationTemplate> CreateOwnedAttributeTemplates(
            string attributeName,
            AttributeSyntax attribute,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return attributeName switch
            {
                "RegisterOwnedCardKeywordAttribute" => KeywordCardTemplates(),
                "RegisterOwnedKeywordAttribute" => CreateAttributeOwnedKeywordTemplates(attribute, semanticModel, cancellationToken),
                "RegisterOwnedCardPileAttribute" => CardPileTemplates(),
                "RegisterOwnedTopBarButtonAttribute" => TopBarButtonTemplates(),
                _ => ImmutableArray<LocalizationTemplate>.Empty,
            };
        }

        private static ImmutableArray<LocalizationTemplate> CreateAttributeOwnedKeywordTemplates(
            AttributeSyntax attribute,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var titleTable = GetAttributeNamedString(attribute, semanticModel, "TitleTable", cancellationToken) ?? "card_keywords";
            var titleKey = GetAttributeNamedString(attribute, semanticModel, "TitleKey", cancellationToken) ?? "{id}.title";
            var descriptionTable = GetAttributeNamedString(attribute, semanticModel, "DescriptionTable", cancellationToken) ?? titleTable;
            var descriptionKey = GetAttributeNamedString(attribute, semanticModel, "DescriptionKey", cancellationToken) ?? "{id}.description";
            return ImmutableArray.Create(
                new LocalizationTemplate(titleTable, titleKey),
                new LocalizationTemplate(descriptionTable, descriptionKey));
        }

        private static ImmutableArray<LocalizationTemplate> CreateInvocationOwnedKeywordTemplates(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var titleTable = GetInvocationStringArgument(invocation, method, "titleTable", 1, semanticModel, cancellationToken) ?? "card_keywords";
            var titleKey = GetInvocationStringArgument(invocation, method, "titleKey", 2, semanticModel, cancellationToken) ?? "{id}.title";
            var descriptionTable = GetInvocationStringArgument(invocation, method, "descriptionTable", 3, semanticModel, cancellationToken) ?? titleTable;
            var descriptionKey = GetInvocationStringArgument(invocation, method, "descriptionKey", 4, semanticModel, cancellationToken) ?? "{id}.description";
            return ImmutableArray.Create(
                new LocalizationTemplate(titleTable, titleKey),
                new LocalizationTemplate(descriptionTable, descriptionKey));
        }

        private static ImmutableArray<LocalizationTemplate> KeywordCardTemplates()
        {
            return ImmutableArray.Create(
                new LocalizationTemplate("card_keywords", "{id}.title"),
                new LocalizationTemplate("card_keywords", "{id}.description"));
        }

        private static ImmutableArray<LocalizationTemplate> CardPileTemplates()
        {
            return ImmutableArray.Create(
                new LocalizationTemplate("static_hover_tips", "{id}.title"),
                new LocalizationTemplate("static_hover_tips", "{id}.description"),
                new LocalizationTemplate("static_hover_tips", "{id}.empty"));
        }

        private static ImmutableArray<LocalizationTemplate> TopBarButtonTemplates()
        {
            return ImmutableArray.Create(
                new LocalizationTemplate("static_hover_tips", "{id}.title"),
                new LocalizationTemplate("static_hover_tips", "{id}.description"));
        }

        private static bool IsOwnedLocalizationAttribute(string attributeName)
        {
            return attributeName is
                "RegisterOwnedCardKeywordAttribute" or
                "RegisterOwnedKeywordAttribute" or
                "RegisterOwnedCardPileAttribute" or
                "RegisterOwnedTopBarButtonAttribute";
        }

        private static string GetOwnedRegistrationKind(string attributeName)
        {
            return attributeName switch
            {
                "RegisterOwnedCardKeywordAttribute" => "KEYWORD",
                "RegisterOwnedKeywordAttribute" => "KEYWORD",
                "RegisterOwnedCardPileAttribute" => "CARDPILE",
                "RegisterOwnedTopBarButtonAttribute" => "TOPBARBUTTON",
                _ => string.Empty,
            };
        }

        private static string GetRegistrationDisplayName(string attributeName)
        {
            return attributeName switch
            {
                "RegisterOwnedCardKeywordAttribute" => "card keyword",
                "RegisterOwnedKeywordAttribute" => "keyword",
                "RegisterOwnedCardPileAttribute" => "card pile",
                "RegisterOwnedTopBarButtonAttribute" => "top-bar button",
                _ => "registration",
            };
        }

        private static bool TryGetAttributeContentInfo(string attributeName, out ContentRegistrationInfo info)
        {
            info = attributeName switch
            {
                "RegisterCardAttribute" => ContentRegistrationInfo.Card(),
                "RegisterRelicAttribute" => ContentRegistrationInfo.Relic(),
                "RegisterPotionAttribute" => ContentRegistrationInfo.Potion(),
                "RegisterCharacterAttribute" => ContentRegistrationInfo.Character(),
                "RegisterActAttribute" => ContentRegistrationInfo.Act(),
                "RegisterMonsterAttribute" => ContentRegistrationInfo.Monster(),
                "RegisterPowerAttribute" => ContentRegistrationInfo.Power(),
                "RegisterOrbAttribute" => ContentRegistrationInfo.Orb(),
                "RegisterEnchantmentAttribute" => ContentRegistrationInfo.Enchantment(),
                "RegisterAfflictionAttribute" => ContentRegistrationInfo.Affliction(),
                "RegisterAchievementAttribute" => ContentRegistrationInfo.Achievement(),
                "RegisterSharedEventAttribute" => ContentRegistrationInfo.Event(),
                "RegisterGlobalEncounterAttribute" => ContentRegistrationInfo.Encounter(),
                "RegisterSharedAncientAttribute" => ContentRegistrationInfo.Ancient(),
                "RegisterActAncientAttribute" => ContentRegistrationInfo.Ancient(),
                "RegisterActEventAttribute" => ContentRegistrationInfo.Event(),
                "RegisterActEncounterAttribute" => ContentRegistrationInfo.Encounter(),
                _ => default,
            };
            return info.DisplayName != null;
        }

        private static bool TryGetInvocationContentInfo(string methodName, out ContentRegistrationInfo info)
        {
            info = methodName switch
            {
                "RegisterCard" or "Card" => ContentRegistrationInfo.Card(),
                "RegisterRelic" or "Relic" => ContentRegistrationInfo.Relic(),
                "RegisterPotion" or "Potion" => ContentRegistrationInfo.Potion(),
                "RegisterCharacter" or "Character" => ContentRegistrationInfo.Character(),
                "RegisterAct" or "Act" => ContentRegistrationInfo.Act(),
                "RegisterMonster" or "Monster" => ContentRegistrationInfo.Monster(),
                "RegisterPower" or "Power" => ContentRegistrationInfo.Power(),
                "RegisterOrb" or "Orb" => ContentRegistrationInfo.Orb(),
                "RegisterEnchantment" or "Enchantment" => ContentRegistrationInfo.Enchantment(),
                "RegisterAffliction" or "Affliction" => ContentRegistrationInfo.Affliction(),
                "RegisterAchievement" or "Achievement" => ContentRegistrationInfo.Achievement(),
                "RegisterSharedEvent" or "SharedEvent" => ContentRegistrationInfo.Event(),
                "RegisterGlobalEncounter" or "GlobalEncounter" => ContentRegistrationInfo.Encounter(),
                "RegisterActEncounter" or "ActEncounter" => ContentRegistrationInfo.Encounter(modelTypeArgumentIndex: 1),
                "RegisterActEvent" or "ActEvent" => ContentRegistrationInfo.Event(modelTypeArgumentIndex: 1),
                "RegisterSharedAncient" or "SharedAncient" => ContentRegistrationInfo.Ancient(),
                "RegisterActAncient" or "ActAncient" => ContentRegistrationInfo.Ancient(modelTypeArgumentIndex: 1),
                _ => default,
            };
            return info.DisplayName != null;
        }

        private static bool IsRitsuLibContentRegistrationInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (method != null)
                return IsRitsuLibContentRegistrationMethod(method);

            if (methodName.StartsWith("Register", StringComparison.Ordinal))
                return IsObviousRitsuLibRegistryReceiver(
                    GetInvocationReceiver(invocation),
                    semanticModel,
                    cancellationToken);

            return IsObviousRitsuLibContentPackReceiver(
                GetInvocationReceiver(invocation),
                semanticModel,
                cancellationToken);
        }

        private static bool IsRitsuLibContentRegistrationMethod(IMethodSymbol method)
        {
            var containingType = method.ContainingType;
            if (IsNamedType(containingType, "STS2RitsuLib.Content.ModContentRegistry"))
                return method.Name.StartsWith("Register", StringComparison.Ordinal);

            return IsNamedType(containingType, "STS2RitsuLib.Scaffolding.Content.ModContentPackBuilder");
        }

        private static bool IsRitsuLibTimelineRegistrationInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (method != null)
                return IsRitsuLibTimelineRegistrationMethod(method);

            return methodName.StartsWith("Register", StringComparison.Ordinal) &&
                   IsObviousRitsuLibTimelineRegistryReceiver(
                       GetInvocationReceiver(invocation),
                       semanticModel,
                       cancellationToken);
        }

        private static bool IsRitsuLibTimelineRegistrationMethod(IMethodSymbol method)
        {
            return IsNamedType(method.ContainingType, "STS2RitsuLib.Timeline.ModTimelineRegistry") &&
                   method.Name.StartsWith("Register", StringComparison.Ordinal);
        }

        private static bool IsRitsuLibTimelineColumnEpochInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (methodName != "Epoch")
                return false;

            if (method != null)
                return IsNamedTypeDefinition(method.ContainingType, "STS2RitsuLib.Scaffolding.Content.TimelineColumnBuilder`1");

            var receiver = GetInvocationReceiver(invocation);
            if (receiver == null)
                return false;

            var receiverType = semanticModel.GetTypeInfo(receiver, cancellationToken).Type as INamedTypeSymbol;
            return IsNamedTypeDefinition(receiverType, "STS2RitsuLib.Scaffolding.Content.TimelineColumnBuilder`1");
        }

        private static bool IsRitsuLibOwnedRegistrationMethod(
            IMethodSymbol? method,
            string methodName,
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var containingType = ResolveInvocationContainingType(invocation, method, semanticModel, cancellationToken);
            if (method != null)
            {
                return methodName switch
                {
                    "RegisterOwned" => IsNamedType(containingType, "STS2RitsuLib.Keywords.ModKeywordRegistry") ||
                                       IsNamedType(containingType, "STS2RitsuLib.CardPiles.ModCardPileRegistry") ||
                                       IsNamedType(containingType, "STS2RitsuLib.TopBar.ModTopBarButtonRegistry"),
                    "RegisterCardKeywordOwnedByLocNamespace" => IsNamedType(containingType, "STS2RitsuLib.Keywords.ModKeywordRegistry"),
                    "CardKeywordOwnedByLocNamespace" or "KeywordOwned" => IsNamedType(containingType, "STS2RitsuLib.Scaffolding.Content.ModContentPackBuilder"),
                    _ => false,
                };
            }

            var receiver = GetInvocationReceiver(invocation);
            return methodName switch
            {
                "RegisterOwned" => IsObviousRitsuLibOwnedRegistryReceiver(receiver, semanticModel, cancellationToken),
                "RegisterCardKeywordOwnedByLocNamespace" => IsObviousRitsuLibKeywordRegistryReceiver(receiver, semanticModel, cancellationToken),
                "CardKeywordOwnedByLocNamespace" or "KeywordOwned" => IsObviousRitsuLibContentPackReceiver(receiver, semanticModel, cancellationToken),
                _ => false,
            };
        }

        private static bool IsRitsuLibI18NMethod(
            IMethodSymbol? method,
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var containingType = ResolveInvocationContainingType(invocation, method, semanticModel, cancellationToken);
            return method != null
                ? IsNamedType(containingType, "STS2RitsuLib.Utils.I18N")
                : IsObviousRitsuLibI18NReceiver(GetInvocationReceiver(invocation), semanticModel, cancellationToken);
        }

        private static bool IsRitsuLibAncientDialogueMethod(
            IMethodSymbol? method,
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var containingType = ResolveInvocationContainingType(invocation, method, semanticModel, cancellationToken);
            return method != null
                ? IsNamedType(containingType, "STS2RitsuLib.Localization.AncientDialogueLocalization")
                : string.Equals(GetInvocationReceiver(invocation)?.ToString(), "AncientDialogueLocalization", StringComparison.Ordinal);
        }

        private static bool IsRitsuLibModSettingsTextMethod(
            IMethodSymbol? method,
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var containingType = ResolveInvocationContainingType(invocation, method, semanticModel, cancellationToken);
            return method != null
                ? IsNamedType(containingType, "STS2RitsuLib.Settings.ModSettingsText")
                : string.Equals(GetInvocationReceiver(invocation)?.ToString(), "ModSettingsText", StringComparison.Ordinal);
        }

        private static INamedTypeSymbol? ResolveInvocationContainingType(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (method?.ContainingType != null)
                return method.ContainingType;

            return GetInvocationReceiver(invocation) == null
                ? null
                : semanticModel.GetTypeInfo(GetInvocationReceiver(invocation)!, cancellationToken).Type as INamedTypeSymbol;
        }

        private static bool IsObviousRitsuLibRegistryReceiver(
            ExpressionSyntax? receiver,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return IsObviousRitsuLibContentReceiver(
                receiver,
                semanticModel,
                cancellationToken,
                allowContentPackBuilder: false,
                allowContentRegistry: true);
        }

        private static bool IsObviousRitsuLibTimelineRegistryReceiver(
            ExpressionSyntax? receiver,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (receiver == null)
                return false;

            receiver = Unwrap(receiver);
            var receiverType = semanticModel.GetTypeInfo(receiver, cancellationToken).Type as INamedTypeSymbol;
            if (IsNamedType(receiverType, "STS2RitsuLib.Timeline.ModTimelineRegistry"))
                return true;

            if (receiver is not InvocationExpressionSyntax invocation)
                return false;

            var invokedMethod = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            if (invokedMethod != null)
            {
                if (invokedMethod.Name == "GetTimelineRegistry" &&
                    IsNamedType(invokedMethod.ContainingType, "STS2RitsuLib.RitsuLibFramework"))
                {
                    return true;
                }

                if (invokedMethod.Name == "For" &&
                    IsNamedType(invokedMethod.ContainingType, "STS2RitsuLib.Timeline.ModTimelineRegistry"))
                {
                    return true;
                }
            }

            var invokedName = GetInvokedMemberName(invocation);
            var receiverName = GetInvocationReceiver(invocation)?.ToString();
            return (invokedName == "GetTimelineRegistry" && receiverName == "RitsuLibFramework") ||
                   (invokedName == "For" && receiverName is "ModTimelineRegistry" or "STS2RitsuLib.Timeline.ModTimelineRegistry");
        }

        private static bool IsObviousRitsuLibKeywordRegistryReceiver(
            ExpressionSyntax? receiver,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (receiver == null)
                return false;

            receiver = Unwrap(receiver);
            var receiverType = semanticModel.GetTypeInfo(receiver, cancellationToken).Type as INamedTypeSymbol;
            if (IsNamedType(receiverType, "STS2RitsuLib.Keywords.ModKeywordRegistry"))
                return true;

            if (receiver is not InvocationExpressionSyntax invocation)
                return false;

            var invokedMethod = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            if (invokedMethod != null)
            {
                if (invokedMethod.Name == "GetKeywordRegistry" &&
                    IsNamedType(invokedMethod.ContainingType, "STS2RitsuLib.RitsuLibFramework"))
                {
                    return true;
                }

                if (invokedMethod.Name == "For" &&
                    IsNamedType(invokedMethod.ContainingType, "STS2RitsuLib.Keywords.ModKeywordRegistry"))
                {
                    return true;
                }
            }

            var invokedName = GetInvokedMemberName(invocation);
            var receiverName = GetInvocationReceiver(invocation)?.ToString();
            return (invokedName == "GetKeywordRegistry" && receiverName == "RitsuLibFramework") ||
                   (invokedName == "For" && receiverName is "ModKeywordRegistry" or "STS2RitsuLib.Keywords.ModKeywordRegistry");
        }

        private static bool IsObviousRitsuLibOwnedRegistryReceiver(
            ExpressionSyntax? receiver,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (receiver == null)
                return false;

            receiver = Unwrap(receiver);
            var receiverType = semanticModel.GetTypeInfo(receiver, cancellationToken).Type as INamedTypeSymbol;
            if (IsNamedType(receiverType, "STS2RitsuLib.Keywords.ModKeywordRegistry") ||
                IsNamedType(receiverType, "STS2RitsuLib.CardPiles.ModCardPileRegistry") ||
                IsNamedType(receiverType, "STS2RitsuLib.TopBar.ModTopBarButtonRegistry"))
            {
                return true;
            }

            if (receiver is not InvocationExpressionSyntax invocation)
                return false;

            var invokedMethod = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            if (invokedMethod != null &&
                invokedMethod.Name == "For" &&
                (IsNamedType(invokedMethod.ContainingType, "STS2RitsuLib.Keywords.ModKeywordRegistry") ||
                 IsNamedType(invokedMethod.ContainingType, "STS2RitsuLib.CardPiles.ModCardPileRegistry") ||
                 IsNamedType(invokedMethod.ContainingType, "STS2RitsuLib.TopBar.ModTopBarButtonRegistry")))
            {
                return true;
            }

            var invokedName = GetInvokedMemberName(invocation);
            var receiverName = GetInvocationReceiver(invocation)?.ToString();
            return invokedName == "For" &&
                   receiverName is "ModKeywordRegistry" or "STS2RitsuLib.Keywords.ModKeywordRegistry" or
                       "ModCardPileRegistry" or "STS2RitsuLib.CardPiles.ModCardPileRegistry" or
                       "ModTopBarButtonRegistry" or "STS2RitsuLib.TopBar.ModTopBarButtonRegistry";
        }

        private static bool IsObviousRitsuLibI18NReceiver(
            ExpressionSyntax? receiver,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (receiver == null)
                return false;

            receiver = Unwrap(receiver);
            var receiverType = semanticModel.GetTypeInfo(receiver, cancellationToken).Type as INamedTypeSymbol;
            if (IsNamedType(receiverType, "STS2RitsuLib.Utils.I18N"))
                return true;

            if (receiver is not InvocationExpressionSyntax invocation)
                return false;

            var invokedMethod = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            if (invokedMethod != null)
            {
                return invokedMethod.Name == "CreateModLocalization" &&
                       IsNamedType(invokedMethod.ContainingType, "STS2RitsuLib.RitsuLibFramework");
            }

            return GetInvokedMemberName(invocation) == "CreateModLocalization" &&
                   string.Equals(GetInvocationReceiver(invocation)?.ToString(), "RitsuLibFramework", StringComparison.Ordinal);
        }

        private static bool IsObviousRitsuLibContentPackReceiver(
            ExpressionSyntax? receiver,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return IsObviousRitsuLibContentReceiver(
                receiver,
                semanticModel,
                cancellationToken,
                allowContentPackBuilder: true,
                allowContentRegistry: false);
        }

        private static bool IsObviousRitsuLibContentReceiver(
            ExpressionSyntax? receiver,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            bool allowContentPackBuilder,
            bool allowContentRegistry)
        {
            if (receiver == null)
                return false;

            receiver = Unwrap(receiver);

            var receiverType = semanticModel.GetTypeInfo(receiver, cancellationToken).Type as INamedTypeSymbol;
            if (allowContentRegistry && IsNamedType(receiverType, "STS2RitsuLib.Content.ModContentRegistry"))
                return true;
            if (allowContentPackBuilder && IsNamedType(receiverType, "STS2RitsuLib.Scaffolding.Content.ModContentPackBuilder"))
                return true;

            if (receiver is InvocationExpressionSyntax invocation)
            {
                var invokedMethod = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                if (invokedMethod != null)
                {
                    var invokedType = invokedMethod.ContainingType;
                    if (allowContentRegistry &&
                        invokedMethod.Name == "GetContentRegistry" &&
                        IsNamedType(invokedType, "STS2RitsuLib.RitsuLibFramework"))
                    {
                        return true;
                    }

                    if (allowContentRegistry &&
                        invokedMethod.Name == "For" &&
                        IsNamedType(invokedType, "STS2RitsuLib.Content.ModContentRegistry"))
                    {
                        return true;
                    }

                    if (allowContentPackBuilder &&
                        invokedMethod.Name == "CreateContentPack" &&
                        IsNamedType(invokedType, "STS2RitsuLib.RitsuLibFramework"))
                    {
                        return true;
                    }

                    if (allowContentPackBuilder &&
                        invokedMethod.Name == "For" &&
                        IsNamedType(invokedType, "STS2RitsuLib.Scaffolding.Content.ModContentPackBuilder"))
                    {
                        return true;
                    }
                }

                if (IsObviousRitsuLibFactoryInvocation(invocation, allowContentPackBuilder, allowContentRegistry))
                    return true;

                return IsObviousRitsuLibContentReceiver(
                    GetInvocationReceiver(invocation),
                    semanticModel,
                    cancellationToken,
                    allowContentPackBuilder,
                    allowContentRegistry);
            }

            return false;
        }

        private static bool IsObviousRitsuLibFactoryInvocation(
            InvocationExpressionSyntax invocation,
            bool allowContentPackBuilder,
            bool allowContentRegistry)
        {
            var invokedName = GetInvokedMemberName(invocation);
            var receiverName = GetInvocationReceiver(invocation)?.ToString();

            return (allowContentRegistry &&
                    ((invokedName == "GetContentRegistry" && receiverName == "RitsuLibFramework") ||
                     (invokedName == "For" && receiverName is "ModContentRegistry" or "STS2RitsuLib.Content.ModContentRegistry"))) ||
                   (allowContentPackBuilder &&
                    ((invokedName == "CreateContentPack" && receiverName == "RitsuLibFramework") ||
                     (invokedName == "For" && receiverName is "ModContentPackBuilder" or "STS2RitsuLib.Scaffolding.Content.ModContentPackBuilder")));
        }

        private static bool IsNamedType(INamedTypeSymbol? type, string metadataName)
        {
            return string.Equals(type?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), metadataName, StringComparison.Ordinal);
        }

        private static bool IsNamedTypeDefinition(INamedTypeSymbol? type, string metadataName)
        {
            var original = type?.OriginalDefinition;
            if (original == null)
                return false;

            if (string.Equals(original.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), metadataName, StringComparison.Ordinal))
                return true;

            var lastDot = metadataName.LastIndexOf('.');
            var namespaceName = lastDot < 0 ? string.Empty : metadataName.Substring(0, lastDot);
            var typeName = lastDot < 0 ? metadataName : metadataName.Substring(lastDot + 1);
            return string.Equals(original.MetadataName, typeName, StringComparison.Ordinal) &&
                   string.Equals(original.ContainingNamespace?.ToDisplayString(), namespaceName, StringComparison.Ordinal);
        }

        private static bool IsRegisterModAssembly(string methodName, IMethodSymbol? method)
        {
            return methodName == "RegisterModAssembly" &&
                   (method?.ContainingType?.Name == "ModTypeDiscoveryHub" || method == null);
        }

        private static bool IsEnsureGodotScriptsRegistered(string methodName, IMethodSymbol? method)
        {
            return methodName == "EnsureGodotScriptsRegistered" &&
                   (method?.ContainingType?.Name == "RitsuLibFramework" || method == null);
        }

        private static PublicEntryOverride ResolveAttributePublicEntry(
            AttributeSyntax attribute,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var stem = GetAttributeNamedString(attribute, semanticModel, "StableEntryStem", cancellationToken);
            var full = GetAttributeNamedString(attribute, semanticModel, "FullPublicEntry", cancellationToken);
            if (!string.IsNullOrWhiteSpace(full))
                return PublicEntryOverride.Full(full!);

            return !string.IsNullOrWhiteSpace(stem)
                ? PublicEntryOverride.Stem(stem!)
                : PublicEntryOverride.None;
        }

        private static PublicEntryOverride ResolveInvocationPublicEntry(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var argument = FindInvocationArgument(invocation, method, "publicEntry", 0)
                           ?? FindInvocationArgument(invocation, method, "publicEntry", 2);
            if (argument == null)
                return PublicEntryOverride.None;

            return ResolvePublicEntryExpression(argument.Expression, semanticModel, cancellationToken);
        }

        private static PublicEntryOverride ResolvePublicEntryExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            expression = Unwrap(expression);
            if (expression is not InvocationExpressionSyntax invocation)
                return PublicEntryOverride.None;

            var method = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            var methodName = method?.Name ?? GetInvokedMemberName(invocation);
            var value = GetInvocationStringArgument(invocation, method, "entryStem", 0, semanticModel, cancellationToken)
                        ?? GetInvocationStringArgument(invocation, method, "fullPublicEntry", 0, semanticModel, cancellationToken);
            if (string.IsNullOrWhiteSpace(value))
                return PublicEntryOverride.None;

            return methodName switch
            {
                "FromStem" => PublicEntryOverride.Stem(value!),
                "FromFullPublicEntry" => PublicEntryOverride.Full(value!),
                _ => PublicEntryOverride.None,
            };
        }

        private static string? ResolveModelTypeName(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            ContentRegistrationInfo info,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var typeSymbol = ResolveModelTypeSymbol(invocation, method, info, semanticModel, cancellationToken);
            if (typeSymbol != null)
                return typeSymbol.Name;

            var typeArgument = ResolveModelTypeSyntax(invocation, info);
            if (typeArgument != null)
                return typeArgument.ToString().Split('.').Last();

            var argument = FindInvocationArgument(invocation, method, info.ModelTypeParameterName, info.ModelTypeArgumentIndex);
            if (argument?.Expression is TypeOfExpressionSyntax typeOf)
                return typeOf.Type.ToString().Split('.').Last();

            return null;
        }

        private static INamedTypeSymbol? ResolveModelTypeSymbol(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            ContentRegistrationInfo info,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (method is { TypeArguments.Length: > 0 } && info.ModelTypeArgumentIndex < method.TypeArguments.Length)
                return method.TypeArguments[info.ModelTypeArgumentIndex] as INamedTypeSymbol;

            var typeArgument = ResolveModelTypeSyntax(invocation, info);
            if (typeArgument != null)
                return semanticModel.GetTypeInfo(typeArgument, cancellationToken).Type as INamedTypeSymbol;

            var argument = FindInvocationArgument(invocation, method, info.ModelTypeParameterName, info.ModelTypeArgumentIndex);
            if (argument?.Expression is TypeOfExpressionSyntax typeOf)
                return semanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type as INamedTypeSymbol;

            return null;
        }

        private static TypeSyntax? ResolveModelTypeSyntax(
            InvocationExpressionSyntax invocation,
            ContentRegistrationInfo info)
        {
            var typeArgumentList = invocation.Expression switch
            {
                MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } => genericName.TypeArgumentList,
                GenericNameSyntax genericName => genericName.TypeArgumentList,
                _ => null,
            };
            if (typeArgumentList != null && info.ModelTypeArgumentIndex < typeArgumentList.Arguments.Count)
                return typeArgumentList.Arguments[info.ModelTypeArgumentIndex];

            return null;
        }

        private static string? ResolveConstantStringProperty(
            INamedTypeSymbol? type,
            string propertyName,
            Compilation compilation,
            System.Threading.CancellationToken cancellationToken)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers(propertyName).OfType<IPropertySymbol>())
                {
                    if (member.IsStatic || member.Type.SpecialType != SpecialType.System_String)
                        continue;

                    foreach (var syntaxReference in member.DeclaringSyntaxReferences)
                    {
                        var syntax = syntaxReference.GetSyntax(cancellationToken);
                        if (syntax is not PropertyDeclarationSyntax property)
                            continue;

                        var expression = property.ExpressionBody?.Expression ?? property.Initializer?.Value;
                        if (expression == null)
                            continue;

                        var semanticModel = compilation.GetSemanticModel(expression.SyntaxTree);
                        if (TryResolveConstantStringExpression(expression, semanticModel, cancellationToken, out var value))
                            return value;
                    }
                }
            }

            return null;
        }

        private static bool TryResolveConstantStringExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out string? value)
        {
            expression = Unwrap(expression);
            var constant = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constant.HasValue && constant.Value is string text)
            {
                value = text;
                return true;
            }

            if (expression is InterpolatedStringExpressionSyntax interpolated)
                return TryResolveConstantInterpolatedString(interpolated, semanticModel, cancellationToken, out value);

            value = null;
            return false;
        }

        private static bool TryResolveConstantInterpolatedString(
            InterpolatedStringExpressionSyntax interpolated,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out string? value)
        {
            StringBuilder builder = new();
            foreach (var content in interpolated.Contents)
            {
                switch (content)
                {
                    case InterpolatedStringTextSyntax text:
                        builder.Append(text.TextToken.ValueText);
                        break;
                    case InterpolationSyntax interpolation:
                        if (interpolation.AlignmentClause != null || interpolation.FormatClause != null)
                        {
                            value = null;
                            return false;
                        }

                        if (!TryResolveConstantStringExpression(interpolation.Expression, semanticModel, cancellationToken, out var part))
                        {
                            value = null;
                            return false;
                        }

                        builder.Append(part);
                        break;
                    default:
                        value = null;
                        return false;
                }
            }

            value = builder.ToString();
            return true;
        }

        private static string? ResolveAttributeOwnerModId(
            AttributeSyntax attribute,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var typeDeclaration = attribute.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (typeDeclaration == null)
                return null;

            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
            if (typeSymbol == null)
                return null;

            foreach (var attr in typeSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.Name != "RitsuLibOwnedByAttribute")
                    continue;

                if (attr.ConstructorArguments.Length == 0)
                    return null;

                return attr.ConstructorArguments[0].Value as string;
            }

            return null;
        }

        private static string? ResolveReceiverModId(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var receiver = GetInvocationReceiver(invocation);
            return receiver == null ? null : ResolveFactoryModId(receiver, semanticModel, cancellationToken, 0);
        }

        private static string? ResolveFactoryModId(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            int depth)
        {
            if (depth > 5)
                return null;

            expression = Unwrap(expression);

            if (expression is InvocationExpressionSyntax invocation)
            {
                var method = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                var methodName = method?.Name ?? GetInvokedMemberName(invocation);
                if (methodName is "GetKeywordRegistry" or "GetContentRegistry" or "CreateContentPack" or "CreateModLocalization" or "For")
                {
                    var modId = GetInvocationStringArgument(invocation, method, "modId", 0, semanticModel, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(modId))
                        return modId;
                }

                var receiver = GetInvocationReceiver(invocation);
                if (receiver != null)
                    return ResolveFactoryModId(receiver, semanticModel, cancellationToken, depth + 1);
            }

            if (expression is IdentifierNameSyntax or MemberAccessExpressionSyntax)
            {
                var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
                foreach (var syntaxReference in symbol?.DeclaringSyntaxReferences ?? ImmutableArray<SyntaxReference>.Empty)
                {
                    var syntax = syntaxReference.GetSyntax(cancellationToken);
                    ExpressionSyntax? initializer = syntax switch
                    {
                        VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                        PropertyDeclarationSyntax property => property.Initializer?.Value ?? property.ExpressionBody?.Expression,
                        _ => null,
                    };

                    if (initializer == null)
                        continue;

                    var resolved = ResolveFactoryModId(initializer, semanticModel, cancellationToken, depth + 1);
                    if (resolved != null)
                        return resolved;
                }
            }

            return null;
        }

        private static ExpressionSyntax? GetInvocationReceiver(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
                MemberBindingExpressionSyntax => null,
                _ => null,
            };
        }

        private static string? GetInvokedMemberName(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => null,
            };
        }

        private static string? GetAttributeShortName(AttributeSyntax attribute)
        {
            var name = attribute.Name.ToString().Split('.').Last();
            return name.EndsWith("Attribute", StringComparison.Ordinal) ? name : name + "Attribute";
        }

        private static string? GetAttributeStringArgument(
            AttributeSyntax attribute,
            SemanticModel semanticModel,
            int index,
            System.Threading.CancellationToken cancellationToken)
        {
            var args = attribute.ArgumentList?.Arguments;
            if (args == null)
                return null;

            var positional = args.Value
                .Where(arg => arg.NameEquals == null && arg.NameColon == null)
                .ToArray();
            return index >= positional.Length
                ? null
                : GetConstantString(positional[index].Expression, semanticModel, cancellationToken);
        }

        private static string? GetAttributeNamedString(
            AttributeSyntax attribute,
            SemanticModel semanticModel,
            string name,
            System.Threading.CancellationToken cancellationToken)
        {
            var argument = attribute.ArgumentList?.Arguments
                .FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.ValueText == name);
            return argument == null ? null : GetConstantString(argument.Expression, semanticModel, cancellationToken);
        }

        private static string? GetInvocationStringArgument(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string parameterName,
            int position,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var argument = FindInvocationArgument(invocation, method, parameterName, position);
            return argument == null ? null : GetConstantString(argument.Expression, semanticModel, cancellationToken);
        }

        private static ArgumentSyntax? FindInvocationArgument(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string parameterName,
            int position)
        {
            var arguments = invocation.ArgumentList.Arguments;
            foreach (var argument in arguments)
            {
                if (argument.NameColon?.Name.Identifier.ValueText == parameterName)
                    return argument;
            }

            if (method != null)
            {
                for (var i = 0; i < arguments.Count && i < method.Parameters.Length; i++)
                {
                    if (method.Parameters[i].Name == parameterName)
                        return arguments[i];
                }
            }

            return position < arguments.Count ? arguments[position] : null;
        }

        private static string? GetConstantString(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var constant = semanticModel.GetConstantValue(expression, cancellationToken);
            return constant.HasValue ? constant.Value as string : null;
        }

        private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
                expression = parenthesized.Expression;
            return expression;
        }

        private void AddRequirement(SyntaxNodeAnalysisContext context, LocalizationRequirement requirement)
        {
            var resolved = requirement.ResolveOwner(_fallbackOwner);
            if (resolved != null)
            {
                ReportMissingLocalization(context, resolved);
                return;
            }

            lock (_gate)
                _requirements.Add(requirement);
        }

        private void AddCharacter(OwnedModel model)
        {
            lock (_gate)
                _characters.Add(model);
        }

        private void AddAncient(OwnedModel model)
        {
            lock (_gate)
                _ancients.Add(model);
        }

        private void AddAssemblyModId(string modId)
        {
            lock (_gate)
                _assemblyModIds.Add(modId);
        }
    }

    private sealed class LocalizationData
    {
        private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _tableKeysByLanguage;
        private readonly Dictionary<string, HashSet<string>> _i18NKeysByLanguage;
        private readonly Dictionary<string, Dictionary<string, string>> _tablePathsByLanguage;
        private readonly Dictionary<string, string> _i18NPathsByLanguage;
        private readonly string[] _roots;

        public LocalizationData(
            Dictionary<string, Dictionary<string, HashSet<string>>> tableKeysByLanguage,
            Dictionary<string, HashSet<string>> i18NKeysByLanguage,
            Dictionary<string, Dictionary<string, string>> tablePathsByLanguage,
            Dictionary<string, string> i18NPathsByLanguage,
            List<string> roots,
            HashSet<string> directoryLanguages)
        {
            _tableKeysByLanguage = tableKeysByLanguage;
            _i18NKeysByLanguage = i18NKeysByLanguage;
            _tablePathsByLanguage = tablePathsByLanguage;
            _i18NPathsByLanguage = i18NPathsByLanguage;
            _roots = roots.OrderBy(root => root, StringComparer.OrdinalIgnoreCase).ToArray();
            Languages = tableKeysByLanguage.Keys
                .Concat(i18NKeysByLanguage.Keys)
                .Concat(directoryLanguages)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string[] Languages { get; }

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

        /// <summary>
        ///     Snapshot of all per-language game LocTable keys for the given table name. Returns empty when the
        ///     language has not loaded that table. Used by RITSU002 to inspect ancients keys.
        /// </summary>
        public IReadOnlyCollection<string> GetTableKeys(string language, string table)
        {
            return _tableKeysByLanguage.TryGetValue(language, out var tables) &&
                   tables.TryGetValue(table, out var keys)
                ? keys
                : Array.Empty<string>();
        }

        /// <summary>
        ///     Enumerates every (language, table, jsonPath) tuple of game LocTable JSON files indexed for the
        ///     compilation. Excludes I18N bridge files. Used by RITSU003.
        /// </summary>
        public IEnumerable<(string Language, string Table, string Path)> EnumerateTableFiles()
        {
            foreach (var languagePair in _tablePathsByLanguage)
            foreach (var tablePair in languagePair.Value)
                yield return (languagePair.Key, tablePair.Key, tablePair.Value);
        }

        private static string CombinePath(params string[] parts)
        {
            return string.Join(Path.DirectorySeparatorChar.ToString(), parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    private abstract class LocalizationRequirement
    {
        protected LocalizationRequirement(string displayName, string subject, Location location, ImmutableArray<RequiredLocalizationKey> keys)
        {
            DisplayName = displayName;
            Subject = subject;
            Location = location;
            Keys = keys;
        }

        public string DisplayName { get; }
        public string Subject { get; }
        public Location Location { get; }
        public ImmutableArray<RequiredLocalizationKey> Keys { get; }

        public virtual LocalizationRequirement? ResolveOwner(string? fallbackOwner)
        {
            return this;
        }

        public static LocalizationRequirement Table(
            string displayName,
            string subject,
            Location location,
            string table,
            ImmutableArray<string> keys)
        {
            return new ResolvedLocalizationRequirement(
                displayName,
                subject,
                location,
                keys.Select(key => new RequiredLocalizationKey(table, key)).ToImmutableArray());
        }

        /// <summary>
        ///     Creates a table requirement whose keys share a single <see cref="RequiredLocalizationKey.AlternativeGroup" /> —
        ///     the requirement is satisfied when ANY of the supplied keys exists for the language. When all variants are
        ///     missing, only the first (canonical) variant is reported.
        /// </summary>
        public static LocalizationRequirement TableAnyOf(
            string displayName,
            string subject,
            Location location,
            string table,
            string alternativeGroup,
            ImmutableArray<string> variants)
        {
            return new ResolvedLocalizationRequirement(
                displayName,
                subject,
                location,
                variants
                    .Select(key => new RequiredLocalizationKey(table, key, alternativeGroup))
                    .ToImmutableArray());
        }

        public static LocalizationRequirement I18N(
            string displayName,
            string subject,
            Location location,
            ImmutableArray<string> keys)
        {
            return new ResolvedLocalizationRequirement(
                displayName,
                subject,
                location,
                keys.Select(key => new RequiredLocalizationKey(I18NTable, key)).ToImmutableArray());
        }
    }

    private sealed class ResolvedLocalizationRequirement : LocalizationRequirement
    {
        public ResolvedLocalizationRequirement(
            string displayName,
            string subject,
            Location location,
            ImmutableArray<RequiredLocalizationKey> keys)
            : base(displayName, subject, location, keys)
        {
        }
    }

    private sealed class CompoundLocalizationRequirement : LocalizationRequirement
    {
        private readonly string? _ownerModId;
        private readonly string _kind;
        private readonly string _localStem;
        private readonly ImmutableArray<LocalizationTemplate> _templates;

        public CompoundLocalizationRequirement(
            string displayName,
            string localStem,
            string kind,
            ImmutableArray<LocalizationTemplate> templates,
            Location location,
            string? ownerModId)
            : base(displayName, localStem, location, ImmutableArray<RequiredLocalizationKey>.Empty)
        {
            _localStem = localStem;
            _kind = kind;
            _templates = templates;
            _ownerModId = ownerModId;
        }

        public override LocalizationRequirement? ResolveOwner(string? fallbackOwner)
        {
            var owner = _ownerModId ?? fallbackOwner;
            if (string.IsNullOrWhiteSpace(owner))
                return null;

            var id = GetCompoundId(owner!, _kind, _localStem);
            var keys = _templates
                .Select(template => new RequiredLocalizationKey(template.Table, template.Resolve(id)))
                .ToImmutableArray();
            return new ResolvedLocalizationRequirement(DisplayName, id, Location, keys);
        }
    }

    private sealed class ModelLocalizationRequirement : LocalizationRequirement
    {
        private readonly OwnedModel _model;
        private readonly ImmutableArray<LocalizationTemplate> _templates;

        public ModelLocalizationRequirement(OwnedModel model, ImmutableArray<LocalizationTemplate> templates)
            : base(model.DisplayName, model.TypeName, model.Location, ImmutableArray<RequiredLocalizationKey>.Empty)
        {
            _model = model;
            _templates = templates;
        }

        public override LocalizationRequirement? ResolveOwner(string? fallbackOwner)
        {
            var resolved = _model.ResolveOwner(fallbackOwner);
            if (resolved == null)
                return null;

            var keys = _templates
                .Select(template => new RequiredLocalizationKey(template.Table, template.Resolve(resolved.Entry)))
                .ToImmutableArray();
            return new ResolvedLocalizationRequirement(resolved.DisplayName, resolved.Entry, resolved.Location, keys);
        }
    }

    private sealed class OwnedModel
    {
        public OwnedModel(
            string displayName,
            string categoryStem,
            string typeName,
            PublicEntryOverride publicEntryOverride,
            string? ownerModId,
            Location location)
        {
            DisplayName = displayName;
            CategoryStem = categoryStem;
            TypeName = typeName;
            PublicEntryOverride = publicEntryOverride;
            OwnerModId = ownerModId;
            Location = location;
        }

        public string DisplayName { get; }
        public string CategoryStem { get; }
        public string TypeName { get; }
        public PublicEntryOverride PublicEntryOverride { get; }
        public string? OwnerModId { get; }
        public Location Location { get; }

        public ResolvedOwnedModel? ResolveOwner(string? fallbackOwner)
        {
            var owner = OwnerModId ?? fallbackOwner;
            if (string.IsNullOrWhiteSpace(owner))
                return null;

            return new ResolvedOwnedModel(
                DisplayName,
                GetModelEntry(owner!, CategoryStem, TypeName, PublicEntryOverride),
                Location);
        }
    }

    private sealed class ResolvedOwnedModel
    {
        public ResolvedOwnedModel(string displayName, string entry, Location location)
        {
            DisplayName = displayName;
            Entry = entry;
            Location = location;
        }

        public string DisplayName { get; }
        public string Entry { get; }
        public Location Location { get; }
    }

    private readonly struct RequiredLocalizationKey
    {
        public RequiredLocalizationKey(string table, string key)
            : this(table, key, null)
        {
        }

        public RequiredLocalizationKey(string table, string key, string? alternativeGroup)
        {
            Table = table;
            Key = key;
            AlternativeGroup = alternativeGroup;
        }

        public string Table { get; }
        public string Key { get; }

        /// <summary>
        ///     When non-null, this key is a member of an "any-of" group: the requirement is satisfied as long as ANY
        ///     key sharing the same <c>AlternativeGroup</c> (and <c>Table</c>) is present in the language file.
        ///     Used for cases like ancient dialogue lines where <c>.ancient | .char | r.ancient | r.char</c> are
        ///     interchangeable per <see cref="STS2RitsuLib.Localization.AncientDialogueLocalization" />.
        /// </summary>
        public string? AlternativeGroup { get; }
    }

    private sealed class MissingLocalizationRecord
    {
        public MissingLocalizationRecord(
            string language,
            string table,
            bool isI18N,
            string targetPath,
            string displayPath,
            string[] keys)
        {
            Language = language;
            Table = table;
            IsI18N = isI18N;
            TargetPath = targetPath;
            DisplayPath = displayPath;
            Keys = keys;
        }

        public string Language { get; }
        public string Table { get; }
        public bool IsI18N { get; }
        public string TargetPath { get; }
        public string DisplayPath { get; }
        public string[] Keys { get; }
    }

    private readonly struct LocalizationTemplate
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

    private readonly struct ContentRegistrationInfo
    {
        private ContentRegistrationInfo(
            string displayName,
            string categoryStem,
            string modelTypeParameterName,
            int modelTypeArgumentIndex,
            ImmutableArray<LocalizationTemplate> templates)
        {
            DisplayName = displayName;
            CategoryStem = categoryStem;
            ModelTypeParameterName = modelTypeParameterName;
            ModelTypeArgumentIndex = modelTypeArgumentIndex;
            Templates = templates;
        }

        public string? DisplayName { get; }
        public string CategoryStem { get; }
        public string ModelTypeParameterName { get; }
        public int ModelTypeArgumentIndex { get; }
        public ImmutableArray<LocalizationTemplate> Templates { get; }

        public static ContentRegistrationInfo Card()
        {
            return new("card model", "CARD", "cardType", 1, CreateTemplates("cards", "title", "description"));
        }

        public static ContentRegistrationInfo Relic()
        {
            return new("relic model", "RELIC", "relicType", 1, CreateTemplates("relics", "title", "description", "flavor"));
        }

        public static ContentRegistrationInfo Potion()
        {
            return new("potion model", "POTION", "potionType", 1, CreateTemplates("potions", "title", "description"));
        }

        public static ContentRegistrationInfo Character()
        {
            return new("character model", "CHARACTER", "characterType", 0, CreateTemplates(
                "characters",
                "title",
                "titleObject",
                "description",
                "flavor",
                "selectMessage",
                "victoryMessage",
                "defeatMessage",
                "pronounObject",
                "possessiveAdjective",
                "pronounPossessive",
                "pronounSubject",
                "goldMonologue",
                "aromaPrinciple",
                "cardsModifierTitle",
                "cardsModifierDescription",
                "eventDeathPrevention",
                "banter.alive.endTurnPing",
                "banter.dead.endTurnPing",
                "unlockText"));
        }

        public static ContentRegistrationInfo Act()
        {
            return new("act model", "ACT", "actType", 0, CreateTemplates("acts", "title"));
        }

        public static ContentRegistrationInfo Monster()
        {
            return new("monster model", "MONSTER", "monsterType", 0, CreateTemplates("monsters", "title"));
        }

        public static ContentRegistrationInfo Power()
        {
            return new("power model", "POWER", "powerType", 0, CreateTemplates("powers", "title", "description"));
        }

        public static ContentRegistrationInfo Orb()
        {
            return new("orb model", "ORB", "orbType", 0, CreateTemplates("orbs", "title", "description"));
        }

        public static ContentRegistrationInfo Enchantment()
        {
            return new("enchantment model", "ENCHANTMENT", "enchantmentType", 0, CreateTemplates("enchantments", "title", "description"));
        }

        public static ContentRegistrationInfo Affliction()
        {
            return new("affliction model", "AFFLICTION", "afflictionType", 0, CreateTemplates("afflictions", "title", "description"));
        }

        public static ContentRegistrationInfo Achievement()
        {
            return new("achievement model", "ACHIEVEMENT", "achievementType", 0, ImmutableArray<LocalizationTemplate>.Empty);
        }

        public static ContentRegistrationInfo Event(int modelTypeArgumentIndex = 0)
        {
            return new("event model", "EVENT", "eventType", modelTypeArgumentIndex, CreateTemplates(
                "events",
                "title",
                "pages.INITIAL.description"));
        }

        public static ContentRegistrationInfo Encounter(int modelTypeArgumentIndex = 0)
        {
            return new("encounter model", "ENCOUNTER", "encounterType", modelTypeArgumentIndex, CreateTemplates(
                "encounters",
                "title",
                "loss",
                "customRewardDescription"));
        }

        public static ContentRegistrationInfo Epoch(int modelTypeArgumentIndex = 0)
        {
            return new("epoch model", "EPOCH", "epochType", modelTypeArgumentIndex, ImmutableArray<LocalizationTemplate>.Empty);
        }

        public static ContentRegistrationInfo Ancient(int modelTypeArgumentIndex = 0)
        {
            return new("ancient model", "ANCIENT", "ancientType", modelTypeArgumentIndex, CreateTemplates(
                "ancients",
                "title",
                "pages.INITIAL.description"));
        }

        private static ImmutableArray<LocalizationTemplate> CreateTemplates(string table, params string[] suffixes)
        {
            return suffixes
                .Select(suffix => new LocalizationTemplate(table, "{id}." + suffix))
                .ToImmutableArray();
        }
    }

    private readonly struct PublicEntryOverride
    {
        private PublicEntryOverride(PublicEntryOverrideKind kind, string? value)
        {
            Kind = kind;
            Value = value;
        }

        public PublicEntryOverrideKind Kind { get; }
        public string? Value { get; }

        public static PublicEntryOverride None => default;

        public static PublicEntryOverride Stem(string value)
        {
            return new(PublicEntryOverrideKind.Stem, value);
        }

        public static PublicEntryOverride Full(string value)
        {
            return new(PublicEntryOverrideKind.FullEntry, value);
        }
    }

    private enum PublicEntryOverrideKind
    {
        None = 0,
        Stem = 1,
        FullEntry = 2,
    }

    private static class JsonTopLevelKeyScanner
    {
        public static HashSet<string> ReadKeys(string json)
        {
            HashSet<string> keys = new(StringComparer.Ordinal);
            var index = 0;
            SkipWhiteSpace(json, ref index);
            if (index >= json.Length || json[index] != '{')
                return keys;

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

                keys.Add(key);
                index++;
                SkipValue(json, ref index);
            }

            return keys;
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

            while (index < json.Length && json[index] is not ',' and not '}')
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
            while (index < json.Length && (char.IsWhiteSpace(json[index]) || json[index] == '\uFEFF'))
                index++;
        }
    }
}
