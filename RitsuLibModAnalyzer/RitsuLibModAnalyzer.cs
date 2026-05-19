using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

    private static readonly Regex NonAlphaNumericRegex = new("[^A-Za-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex AcronymBoundaryRegex = new("([A-Z]+)([A-Z][a-z])", RegexOptions.Compiled);
    private static readonly Regex CamelBoundaryRegex = new("([a-z0-9])([A-Z])", RegexOptions.Compiled);
    private static readonly Regex RepeatedUnderscoreRegex = new("_+", RegexOptions.Compiled);

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
        AddLocalizationDirectoryLanguages(context.Options, roots, directoryLanguages);

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

        return new LocalizationData(tableKeysByLanguage, i18NKeysByLanguage, tablePathsByLanguage, i18NPathsByLanguage, roots, directoryLanguages);
    }

    private static void AddLocalizationDirectoryLanguages(
        AnalyzerOptions options,
        List<string> roots,
        HashSet<string> languages)
    {
        var projectDirectory = GetBuildProperty(options, "MSBuildProjectDirectory");
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            return;

        foreach (var localizationRoot in EnumerateLocalizationRoots(projectDirectory!))
        {
            if (!roots.Contains(localizationRoot, StringComparer.OrdinalIgnoreCase))
                roots.Add(localizationRoot);

            foreach (var languageDirectory in Directory.EnumerateDirectories(localizationRoot))
            {
                var language = NormalizeLanguageCode(Path.GetFileName(languageDirectory));
                if (!string.IsNullOrWhiteSpace(language))
                    languages.Add(language);
            }
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

    private static string GetCompoundId(string modId, string typeStem, string localStem)
    {
        return $"{NormalizePublicStem(modId)}_{typeStem.Trim().ToUpperInvariant()}_{NormalizePublicStem(localStem)}";
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
        var normalized = NonAlphaNumericRegex.Replace(value.Trim(), "_");
        normalized = AcronymBoundaryRegex.Replace(normalized, "$1_$2");
        normalized = CamelBoundaryRegex.Replace(normalized, "$1_$2");
        normalized = RepeatedUnderscoreRegex.Replace(normalized, "_");
        return normalized.Trim('_').ToUpperInvariant();
    }

    private static string NormalizeFullPublicEntry(string value)
    {
        return NormalizePublicStem(value);
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
                    var requirement = LocalizationRequirement.Table(
                        "Ancient dialogue",
                        $"{ancientModel.Entry} -> {characterModel.Entry}",
                        ancient.Location,
                        "ancients",
                        ImmutableArray.Create(
                            $"{baseKey}0-0.ancient",
                            $"{baseKey}0-0.char"));
                    ReportMissingLocalization(context, requirement);
                }
            }
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

                AddRequirement(context, LocalizationRequirement.Table(
                    "Ancient dialogue",
                    baseKey!,
                    invocation.GetLocation(),
                    table!,
                    ImmutableArray.Create($"{baseKey}0-0.ancient", $"{baseKey}0-0.char")));
                return;
            }

            if (methodName == "BuildDialogueSetForModAncient")
            {
                if (!IsRitsuLibAncientDialogueMethod(method, invocation, context.SemanticModel, context.CancellationToken))
                    return;

                var ancientEntry = GetInvocationStringArgument(invocation, method, "ancientEntry", 0, context.SemanticModel, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(ancientEntry))
                    return;

                AddRequirement(context, LocalizationRequirement.Table(
                    "Ancient dialogue",
                    ancientEntry!,
                    invocation.GetLocation(),
                    "ancients",
                    ImmutableArray.Create(
                        $"{ancientEntry}.talk.firstVisitEver.0-0.ancient",
                        $"{ancientEntry}.talk.ANY.0-0.ancient")));
            }
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
                    $"{id}.unlockText")));
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

            foreach (var language in _localization.Languages)
            {
                foreach (var group in requirement.Keys.GroupBy(key => key.Table, StringComparer.OrdinalIgnoreCase))
                {
                    var isI18N = string.Equals(group.Key, I18NTable, StringComparison.OrdinalIgnoreCase);
                    var missing = group
                        .Where(key => IsMissingLocalizationKey(language, isI18N, key))
                        .Select(key => key.Key)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(key => key, StringComparer.Ordinal)
                        .ToArray();

                    if (missing.Length == 0)
                        continue;

                    var table = isI18N ? string.Empty : group.Key;
                    var targetPath = _localization.GetTargetPath(language, table, isI18N);
                    var displayPath = isI18N ? $"{language}.json" : $"{language}/{table}.json";
                    foreach (var severityGroup in missing.GroupBy(key => GetMissingLocalizationSeverity(language, isI18N, table, key)))
                    {
                        var severityMissing = severityGroup.OrderBy(key => key, StringComparer.Ordinal).ToArray();
                        var properties = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
                        properties[RitsuLibDiagnosticProperties.Language] = language;
                        properties[RitsuLibDiagnosticProperties.Table] = table;
                        properties[RitsuLibDiagnosticProperties.IsI18N] = isI18N ? "true" : "false";
                        properties[RitsuLibDiagnosticProperties.TargetPath] = targetPath;
                        properties[RitsuLibDiagnosticProperties.Keys] = string.Join(RitsuLibDiagnosticProperties.ListSeparator, severityMissing);
                        properties[RitsuLibDiagnosticProperties.Values] = string.Join(RitsuLibDiagnosticProperties.ListSeparator, severityMissing.Select(_ => string.Empty));

                        yield return Diagnostic.Create(
                            CreateMissingLocalizationRule(),
                            requirement.Location,
                            severityGroup.Key,
                            additionalLocations: null,
                            properties: properties.ToImmutable(),
                            requirement.DisplayName,
                            requirement.Subject,
                            severityMissing.Length,
                            displayPath,
                            string.Join(", ", severityMissing));
                    }
                }
            }
        }

        private bool IsMissingLocalizationKey(string language, bool isI18N, RequiredLocalizationKey key)
        {
            return isI18N
                ? !_localization.ContainsI18N(language, key.Key)
                : !_localization.ContainsTable(language, key.Table, key.Key);
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
        {
            Table = table;
            Key = key;
        }

        public string Table { get; }
        public string Key { get; }
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
                "pronounObject",
                "possessiveAdjective",
                "pronounPossessive",
                "pronounSubject",
                "cardsModifierTitle",
                "cardsModifierDescription",
                "eventDeathPrevention",
                "unlockText"));
        }

        public static ContentRegistrationInfo Act()
        {
            return new("act model", "ACT", "actType", 0, CreateTemplates("acts", "title"));
        }

        public static ContentRegistrationInfo Monster()
        {
            return new("monster model", "MONSTER", "monsterType", 0, CreateTemplates("monsters", "name"));
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
            return new("encounter model", "ENCOUNTER", "encounterType", modelTypeArgumentIndex, CreateTemplates("encounters", "title", "loss"));
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
                "epithet",
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
