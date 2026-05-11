using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

public sealed partial class RitsuLibModAnalyzer
{
    private sealed partial class CompilationState
    {
        private static readonly Regex RecommendedLiteralIdRegex =
            new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.Compiled);

        private static readonly Regex FmodGuidRegex =
            new("^\\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\}?$", RegexOptions.Compiled);

        private readonly List<DataStoreRegistration> _dataStoreRegistrations = new();
        private readonly List<SettingsSubpageReference> _settingsSubpageReferences = new();

        public void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
        {
            var creation = (BaseObjectCreationExpressionSyntax)context.Node;
            var type = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type as INamedTypeSymbol;
            var typeName = type?.Name ?? creation switch
            {
                ObjectCreationExpressionSyntax objectCreation => objectCreation.Type.ToString().Split('.').Last(),
                _ => null,
            };
            if (typeName == null)
                return;

            if (typeName == "ModPatchTarget")
            {
                MarkUsesRitsuLib();
                AnalyzeModPatchTargetCreation(creation, context);
                return;
            }

            if (typeName == "RuntimeHotkeyOptions")
            {
                MarkUsesRitsuLib();
                AnalyzeRuntimeHotkeyOptionsCreation(creation, context);
            }
        }

        public void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
        {
            var declaration = (TypeDeclarationSyntax)context.Node;
            var type = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
            if (type == null)
                return;

            if (HasBaseType(type, "GodotObject") || HasBaseType(type, "Node") || HasBaseType(type, "Control"))
            {
                lock (_gate)
                    _usesGodotScriptType = true;
            }

            if (Implements(type, "IPatchMethod"))
            {
                MarkUsesRitsuLib();
                AnalyzePatchMethodType(declaration, type, context);
            }

            if (Implements(type, "IModPatches"))
            {
                MarkUsesRitsuLib();
                AnalyzeModPatchesType(declaration, type, context);
            }
        }

        public void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
        {
            var property = (PropertyDeclarationSyntax)context.Node;
            if (!property.Modifiers.Any(SyntaxKind.OverrideKeyword))
                return;

            var name = property.Identifier.ValueText;
            if (name is "CardTypes" or "RelicTypes" or "PotionTypes")
            {
                MarkUsesRitsuLib();
                Report(
                    context,
                    RitsuLibDiagnostics.LegacyPoolHookRule,
                    property.Identifier.GetLocation(),
                    EmptyProperties(),
                name);
                return;
            }

            // RITSU021: Character template legacy override
            if (name is "StartingDeckTypes" or "StartingRelicTypes" or "StartingPotionTypes")
            {
                var type = context.SemanticModel.GetDeclaredSymbol(property.Parent, context.CancellationToken) as INamedTypeSymbol;
                if (type != null && HasBaseType(type, "ModCharacterTemplate"))
                {
                    MarkUsesRitsuLib();
                    Report(
                        context,
                        RitsuLibDiagnostics.CharacterTemplateLegacyRule,
                        property.Identifier.GetLocation(),
                        EmptyProperties(),
                        name);
                }
            }
        }

        public void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            foreach (var list in method.AttributeLists)
            {
                foreach (var attribute in list.Attributes)
                {
                    var attributeName = GetAttributeShortName(attribute);
                    if (attributeName == "ModSettingsButtonAttribute")
                        AnalyzeSettingsButtonMethod(method, attribute, context);
                }
            }
        }

        private void AnalyzeContractAttribute(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            if (!IsRitsuLibAttribute(attributeName))
                return;

            MarkUsesRitsuLib();
            AnalyzeModIdAttributeArgument(attributeName, attribute, context);
            AnalyzeIdShapeAttribute(attributeName, attribute, context);
            AnalyzeAttributeResourcePaths(attributeName, attribute, context);
            AnalyzeSettingsAttribute(attributeName, attribute, context);

            if (IsAutoRegistrationAttribute(attributeName))
            {
                lock (_gate)
                    _usesAutoRegistration = true;
            }
        }

        private void AnalyzeContractInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (!IsRitsuLibInvocation(methodName, method))
                return;

            MarkUsesRitsuLib();
            AnalyzeInvocationModId(invocation, method, methodName, context);
            AnalyzeGeneralIdShapeInvocation(invocation, method, methodName, context);
            AnalyzeContentPackApply(invocation, method, methodName, context);
            AnalyzeDynamicVarTooltipInvocation(invocation, method, methodName, context);
        }

        private void AnalyzeSettingsInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (!IsSettingsInvocation(methodName, method))
                return;

            MarkUsesRitsuLib();

            if (methodName == "RegisterModSettings")
                RegisterSettingsPage(invocation, method, context);
            else if (methodName == "AddSection")
                RegisterSettingsSection(invocation, method, context);
            else if (IsSettingsEntryBuilderMethod(methodName))
                RegisterSettingsEntry(invocation, method, methodName, context);
            else if (methodName is "RegisterModSettingsReflectionProvider" or "RegisterModSettingsReflectionProviderAndTryRegister")
                RegisterSettingsReflectionProvider(invocation, method);

            if (methodName is "AddSlider" or "AddIntSlider")
                AnalyzeSliderArguments(invocation, method, methodName, context);
            else if (methodName == "AddChoice")
                AnalyzeChoiceArguments(invocation, method, context);

            // RITSU022/RITSU024: Track subpage references
            if (methodName == "AddSubpage")
            {
                var targetPageId = GetInvocationStringArgument(invocation, method, "targetPageId", 2, context.SemanticModel, context.CancellationToken)
                                   ?? GetInvocationStringArgument(invocation, method, "pageId", 2, context.SemanticModel, context.CancellationToken);
                if (!string.IsNullOrWhiteSpace(targetPageId))
                {
                    var page = FindContainingSettingsPage(invocation, context);
                    var sectionId = FindContainingSettingsSectionId(invocation, context);
                    lock (_gate)
                        _settingsSubpageReferences.Add(new(page, sectionId ?? "?", targetPageId!, invocation.GetLocation()));
                }
            }
        }

        private void AnalyzeDataStoreInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName != "Register" || method?.ContainingType?.Name != "ModDataStore")
                return;

            MarkUsesRitsuLib();
            var key = GetInvocationStringArgument(invocation, method, "key", 0, context.SemanticModel, context.CancellationToken);
            var fileName = GetInvocationStringArgument(invocation, method, "fileName", 1, context.SemanticModel, context.CancellationToken);
            if (string.IsNullOrWhiteSpace(key))
            {
                ReportContract(context, RitsuLibDiagnostics.DataStoreContractRule, invocation.GetLocation(),
                    RitsuLibUiText.DataStoreKeyEmpty);
            }
            else
            {
                ReportIdShapeIfNeeded(context, invocation.GetLocation(), "ModDataStore key", key!);
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                ReportContract(context, RitsuLibDiagnostics.DataStoreContractRule, invocation.GetLocation(),
                    RitsuLibUiText.DataStoreFileNameEmpty);
            }
            else
            {
                if (fileName!.IndexOfAny(new[] { '/', '\\' }) >= 0)
                    ReportContract(context, RitsuLibDiagnostics.DataStoreContractRule, invocation.GetLocation(),
                        RitsuLibUiText.DataStoreFileNameIsPath(fileName!));
                if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    ReportContract(context, RitsuLibDiagnostics.DataStoreContractRule, invocation.GetLocation(),
                        RitsuLibUiText.DataStoreFileNameMissingJson(fileName!));
            }

            var migrationConfig = FindInvocationArgument(invocation, method, "migrationConfig", 5);
            var migrations = FindInvocationArgument(invocation, method, "migrations", 6);
            if (migrations != null && !IsNullLiteral(migrations.Expression) &&
                (migrationConfig == null || IsNullLiteral(migrationConfig.Expression)))
            {
                ReportContract(context, RitsuLibDiagnostics.DataStoreContractRule, migrations.GetLocation(),
                    RitsuLibUiText.DataStoreMigrationRequiresConfig);
            }

            if (!string.IsNullOrWhiteSpace(key))
            {
                lock (_gate)
                    _dataStoreRegistrations.Add(new(key!, invocation.GetLocation()));
            }
        }

        private void AnalyzePatchInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName == "FromMethod" &&
                (method?.ContainingType?.Name == "DynamicPatchBuilder" || method == null))
            {
                MarkUsesRitsuLib();
                AnalyzeDynamicPatchFromMethod(invocation, method, context);
                return;
            }

            if (methodName is "AddMethod" or "AddPropertyGetter" &&
                (method?.ContainingType?.Name == "DynamicPatchBuilder" || method == null))
            {
                MarkUsesRitsuLib();
                AnalyzeDynamicPatchTargetInvocation(invocation, method, methodName, context);
            }
        }

        private void AnalyzeResourceInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (!MayUseResourcePath(methodName, method))
                return;

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                var name = argument.NameColon?.Name.Identifier.ValueText;
                if (!IsResourceArgumentName(name) && !IsLikelyResourceMethod(methodName))
                    continue;

                var value = GetConstantString(argument.Expression, context.SemanticModel, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                AnalyzeResourceString(value!, argument.GetLocation(), context);
                AnalyzeAudioString(value!, methodName, argument.GetLocation(), context);
            }
        }

        private void AnalyzeRuntimeHelperInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName is "RegisterHealthBarForecast" or "RegisterHealthBarVisualGraft")
            {
                MarkUsesRitsuLib();
                var sourceId = GetInvocationStringArgument(invocation, method, "sourceId", 1, context.SemanticModel, context.CancellationToken);
                if (!string.IsNullOrWhiteSpace(sourceId))
                    ReportIdShapeIfNeeded(context, invocation.GetLocation(), "healthbar source id", sourceId!);
                return;
            }

            if (methodName == "RegisterFreePlayBinding")
            {
                MarkUsesRitsuLib();
                var bindingId = GetInvocationStringArgument(invocation, method, "bindingId", 0, context.SemanticModel, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(bindingId))
                    ReportContract(context, RitsuLibDiagnostics.RuntimeHelperRule, invocation.GetLocation(),
                        RitsuLibUiText.FreePlayBindingIdEmpty);
                else
                    ReportIdShapeIfNeeded(context, invocation.GetLocation(), "free-play binding id", bindingId!);
                return;
            }

            if (methodName == "Register" && method?.ContainingType?.Name == "RuntimeHotkeyService")
            {
                MarkUsesRitsuLib();
                var binding = GetInvocationStringArgument(invocation, method, "bindingText", 0, context.SemanticModel, context.CancellationToken);
                if (!IsPlausibleHotkey(binding))
                    ReportContract(context, RitsuLibDiagnostics.RuntimeHelperRule, invocation.GetLocation(),
                        RitsuLibUiText.HotkeyBindingInvalid(binding ?? string.Empty));
            }
        }

        private void MarkRegisterModAssembly(
            InvocationExpressionSyntax invocation,
            string? modId,
            SyntaxNodeAnalysisContext context)
        {
            MarkUsesRitsuLib();
            lock (_gate)
                _callsRegisterModAssembly = true;

            AnalyzeModIdLiteral(modId, invocation.GetLocation(), context);
        }

        private void MarkEnsureGodotScriptsRegistered(
            InvocationExpressionSyntax invocation,
            SyntaxNodeAnalysisContext context)
        {
            MarkUsesRitsuLib();
            lock (_gate)
                _callsEnsureGodotScriptsRegistered = true;
        }

        private void AddPublicEntry(
            ContentRegistrationInfo info,
            string typeName,
            PublicEntryOverride publicEntryOverride,
            string? ownerModId,
            Location location)
        {
            lock (_gate)
                _publicEntries.Add(new(info.DisplayName ?? "content", info.CategoryStem, typeName, publicEntryOverride, ownerModId, location));
        }

        private void ReportContractCompilationEnd(
            CompilationAnalysisContext context,
            string? fallbackOwner,
            string[] assemblyModIds)
        {
            RitsuDiagnostic[] deferred;
            RegisteredPublicEntry[] publicEntries;
            ContentPackChain[] contentPackChains;
            SettingsPageRegistration[] settingsPages;
            Dictionary<string, HashSet<string>> settingsSectionsByPage;
            Dictionary<string, List<SettingsEntryRegistration>> settingsEntriesByPageSection;
            DataStoreRegistration[] dataStoreRegistrations;
            SettingsSubpageReference[] subpageReferences;
            bool usesRitsuLib;
            bool usesAutoRegistration;
            bool usesGodotScriptType;
            bool callsRegisterModAssembly;
            bool callsEnsureGodotScriptsRegistered;

            lock (_gate)
            {
                deferred = _deferredDiagnostics.ToArray();
                publicEntries = _publicEntries.ToArray();
                contentPackChains = _contentPackChains.ToArray();
                settingsPages = _settingsPages.ToArray();
                settingsSectionsByPage = _settingsSectionsByPage.ToDictionary(
                    pair => pair.Key,
                    pair => new HashSet<string>(pair.Value, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
                settingsEntriesByPageSection = _settingsEntriesByPageSection.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase);
                dataStoreRegistrations = _dataStoreRegistrations.ToArray();
                subpageReferences = _settingsSubpageReferences.ToArray();
                usesRitsuLib = _usesRitsuLib;
                usesAutoRegistration = _usesAutoRegistration;
                usesGodotScriptType = _usesGodotScriptType;
                callsRegisterModAssembly = _callsRegisterModAssembly;
                callsEnsureGodotScriptsRegistered = _callsEnsureGodotScriptsRegistered;
            }

            if (usesRitsuLib && _additionalFiles.Manifest.Exists && !_additionalFiles.Manifest.DependsOnRitsuLib)
                Report(context, RitsuLibDiagnostics.ManifestDependencyRule, Location.None, EmptyProperties());

            if (usesAutoRegistration && !callsRegisterModAssembly)
            {
                var modId = fallbackOwner ?? _additionalFiles.Manifest.ModId ?? assemblyModIds.FirstOrDefault() ?? "YourModId";
                Report(context, RitsuLibDiagnostics.MissingRegistrationRule, FirstLocationOrNone(deferred),
                    Properties(
                        (RitsuLibDiagnosticProperties.ModId, modId),
                        (RitsuLibDiagnosticProperties.InsertionText,
                            $"STS2RitsuLib.Interop.ModTypeDiscoveryHub.RegisterModAssembly({FormatModIdExpression(modId)}, System.Reflection.Assembly.GetExecutingAssembly());")));
            }

            if ((usesGodotScriptType || _additionalFiles.HasGodotTextResources) && !callsEnsureGodotScriptsRegistered)
            {
                Report(context, RitsuLibDiagnostics.MissingGodotScriptsRule, Location.None,
                    Properties((RitsuLibDiagnosticProperties.InsertionText,
                        "STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered(System.Reflection.Assembly.GetExecutingAssembly());")));
            }

            foreach (var diagnostic in deferred)
                Report(context, diagnostic);

            foreach (var chain in contentPackChains)
            {
                if (!chain.HasRegistration || chain.HasApply)
                    continue;

                var descriptor = chain.EntryPoint == "For"
                    ? RitsuLibDiagnostics.ContentPackBuilderNotAppliedRule
                    : RitsuLibDiagnostics.ContentPackNotAppliedRule;
                Report(context, descriptor, chain.Location,
                    Properties((RitsuLibDiagnosticProperties.StubKind, "Apply")));
            }

            ReportDuplicatePublicEntries(context, publicEntries, fallbackOwner);
            ReportSettingsGraphIssues(context, settingsPages, settingsSectionsByPage, settingsEntriesByPageSection);
            ReportDuplicateDataStoreKeys(context, dataStoreRegistrations);
            ReportSettingsSubpageIssues(context, settingsPages, subpageReferences);
        }

        private void AnalyzeModIdAttributeArgument(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            string? modId = attributeName switch
            {
                "RitsuLibOwnedByAttribute" => GetAttributeStringArgument(attribute, context.SemanticModel, 0, context.CancellationToken),
                "ModSettingsPageAttribute" => GetAttributeStringArgument(attribute, context.SemanticModel, 0, context.CancellationToken),
                _ => null,
            };

            AnalyzeModIdLiteral(modId, attribute.GetLocation(), context);
        }

        private void AnalyzeModIdLiteral(string? modId, Location location, SyntaxNodeAnalysisContext context)
        {
            if (string.IsNullOrWhiteSpace(modId))
                return;

            ReportModIdMismatchIfNeeded(context, location, modId!);
        }

        private void AnalyzeInvocationModId(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            var modId = GetInvocationStringArgument(invocation, method, "modId", 0, context.SemanticModel, context.CancellationToken)
                        ?? GetInvocationStringArgument(invocation, method, "ownerModId", 0, context.SemanticModel, context.CancellationToken);
            if (!string.IsNullOrWhiteSpace(modId))
                AnalyzeModIdLiteral(modId, invocation.GetLocation(), context);

            if (methodName == "CreatePatcher")
            {
                var owner = GetInvocationStringArgument(invocation, method, "ownerModId", 0, context.SemanticModel, context.CancellationToken);
                if (!string.IsNullOrWhiteSpace(owner))
                    AnalyzeModIdLiteral(owner, invocation.GetLocation(), context);
            }
        }

        private void ReportModIdMismatchIfNeeded(SyntaxNodeAnalysisContext context, Location location, string actual)
        {
            var expected = _additionalFiles.Manifest.ModId;
            if (string.IsNullOrWhiteSpace(expected) || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                return;

            Report(
                context,
                RitsuLibDiagnostics.ModIdMismatchRule,
                location,
                Properties(
                    (RitsuLibDiagnosticProperties.ExpectedModId, expected!),
                    (RitsuLibDiagnosticProperties.ActualModId, actual)),
                actual,
                expected!);
        }

        private void AnalyzeIdShapeAttribute(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            switch (attributeName)
            {
                case "RegisterOwnedCardKeywordAttribute":
                case "RegisterOwnedKeywordAttribute":
                case "RegisterOwnedCardPileAttribute":
                case "RegisterOwnedTopBarButtonAttribute":
                case "RegisterOwnedCardTagAttribute":
                    var stem = GetAttributeStringArgument(attribute, context.SemanticModel, 0, context.CancellationToken);
                    ReportIdShapeIfNeeded(context, attribute.GetLocation(), GetRegistrationDisplayName(attributeName), stem);
                    break;
                case "ModSettingsSectionAttribute":
                    ReportIdShapeIfNeeded(context, attribute.GetLocation(), "settings section id",
                        GetAttributeStringArgument(attribute, context.SemanticModel, 0, context.CancellationToken));
                    break;
            }
        }

        private void AnalyzeGeneralIdShapeInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName is "RegisterOwned" or "CardKeywordOwnedByLocNamespace" or "KeywordOwned" or "CardTagOwned")
            {
                var stem = GetInvocationStringArgument(invocation, method, "localStem", 0, context.SemanticModel, context.CancellationToken)
                           ?? GetInvocationStringArgument(invocation, method, "localKeywordStem", 0, context.SemanticModel, context.CancellationToken)
                           ?? GetInvocationStringArgument(invocation, method, "localButtonStem", 0, context.SemanticModel, context.CancellationToken)
                           ?? GetInvocationStringArgument(invocation, method, "localPileStem", 0, context.SemanticModel, context.CancellationToken)
                           ?? GetInvocationStringArgument(invocation, method, "localTagStem", 0, context.SemanticModel, context.CancellationToken);
                ReportIdShapeIfNeeded(context, invocation.GetLocation(), "RitsuLib local stem", stem);
            }
        }

        private void ReportIdShapeIfNeeded(SyntaxNodeAnalysisContext context, Location location, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || RecommendedLiteralIdRegex.IsMatch(value!))
                return;

            Report(
                context,
                RitsuLibDiagnostics.IdShapeRule,
                location,
                EmptyProperties(),
                label,
                value!);
        }

        private void AnalyzeContentPackApply(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName == "CreateContentPack")
            {
                var outer = GetOutermostInvocationInChain(invocation);
                var hasApply = InvocationChainContains(outer, "Apply");
                var hasRegistration = InvocationChainContainsContentPackRegistration(outer);
                lock (_gate)
                    _contentPackChains.Add(new(outer.GetLocation(), hasRegistration, hasApply, "CreateContentPack"));
                return;
            }

            // RITSU018: Also detect For() chains on ModContentPackBuilder
            if (method?.ContainingType?.Name == "ModContentPackBuilder" && methodName != "For")
            {
                var receiver = GetInvocationReceiver(invocation);
                if (receiver != null && ChainStartsWithFor(receiver, context))
                {
                    var outer = GetOutermostInvocationInChain(invocation);
                    var hasApply = InvocationChainContains(outer, "Apply");
                    var hasRegistration = InvocationChainContainsContentPackRegistration(outer);
                    lock (_gate)
                        _contentPackChains.Add(new(outer.GetLocation(), hasRegistration, hasApply, "For"));
                }
            }
        }

        private bool ChainStartsWithFor(ExpressionSyntax receiver, SyntaxNodeAnalysisContext context)
        {
            var current = Unwrap(receiver);
            for (var depth = 0; depth < 7 && current != null; depth++)
            {
                if (current is not InvocationExpressionSyntax receiverInvocation)
                    break;

                var receiverMethod = context.SemanticModel.GetSymbolInfo(receiverInvocation, context.CancellationToken).Symbol as IMethodSymbol;
                var receiverMethodName = receiverMethod?.Name ?? GetInvokedMemberName(receiverInvocation);
                if (receiverMethodName == "For" && receiverMethod?.ContainingType?.Name == "ModContentPackBuilder")
                    return true;

                current = GetInvocationReceiver(receiverInvocation);
                if (current != null)
                    current = Unwrap(current);
            }
            return false;
        }

        private void AnalyzeDynamicVarTooltipInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName == "WithSharedTooltip")
            {
                var prefix = GetInvocationStringArgument(invocation, method, "entryPrefix", 0, context.SemanticModel, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(prefix))
                    return;

                AddRequirement(context, LocalizationRequirement.Table(
                    "dynamic var tooltip",
                    prefix!,
                    invocation.GetLocation(),
                    "static_hover_tips",
                    ImmutableArray.Create($"{prefix}.title", $"{prefix}.description")));
                return;
            }

            if (methodName != "WithTooltip")
                return;

            var titleTable = GetInvocationStringArgument(invocation, method, "titleTable", 1, context.SemanticModel, context.CancellationToken);
            var titleKey = GetInvocationStringArgument(invocation, method, "titleKey", 2, context.SemanticModel, context.CancellationToken);
            var descTable = GetInvocationStringArgument(invocation, method, "descriptionTable", 3, context.SemanticModel, context.CancellationToken);
            var descKey = GetInvocationStringArgument(invocation, method, "descriptionKey", 4, context.SemanticModel, context.CancellationToken);
            if (!string.IsNullOrWhiteSpace(titleTable) && !string.IsNullOrWhiteSpace(titleKey))
                AddRequirement(context, LocalizationRequirement.Table(
                    "dynamic var tooltip",
                    titleKey!,
                    invocation.GetLocation(),
                    titleTable!,
                    ImmutableArray.Create(titleKey!)));
            if (!string.IsNullOrWhiteSpace(descTable) && !string.IsNullOrWhiteSpace(descKey))
                AddRequirement(context, LocalizationRequirement.Table(
                    "dynamic var tooltip",
                    descKey!,
                    invocation.GetLocation(),
                    descTable!,
                    ImmutableArray.Create(descKey!)));
        }

        private void AnalyzeSettingsAttribute(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            if (!attributeName.StartsWith("ModSettings", StringComparison.Ordinal))
                return;

            AnalyzeSettingsAttributeLocalization(attribute, context);

            if (attributeName == "ModSettingsPageAttribute")
            {
                var modId = GetAttributeStringArgument(attribute, context.SemanticModel, 0, context.CancellationToken);
                var pageId = GetAttributeStringArgument(attribute, context.SemanticModel, 1, context.CancellationToken) ?? modId;
                ReportIdShapeIfNeeded(context, attribute.GetLocation(), "settings page id", pageId);
                RegisterSettingsPage(attribute.GetLocation(), modId, pageId);
                return;
            }

            if (attributeName == "ModSettingsSectionAttribute")
            {
                var sectionId = GetAttributeStringArgument(attribute, context.SemanticModel, 0, context.CancellationToken);
                var page = FindContainingSettingsPage(attribute, context);
                RegisterSettingsSection(attribute.GetLocation(), page, sectionId, context);
                return;
            }

            if (IsSettingsEntryAttribute(attributeName))
            {
                var entryId = GetAttributeStringArgument(attribute, context.SemanticModel, 0, context.CancellationToken);
                var sectionId = GetAttributeStringArgument(attribute, context.SemanticModel, 1, context.CancellationToken);
                var page = FindContainingSettingsPage(attribute, context);
                RegisterSettingsEntry(attribute.GetLocation(), page, sectionId, entryId, context);
                AnalyzeSettingsAttributeShape(attributeName, attribute, context);
            }

            if (attributeName == "ModSettingsBindingAttribute")
                AnalyzeSettingsBindingCallbacks(attribute, context);
        }

        private void AnalyzeSettingsAttributeLocalization(
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            var names = attribute.ArgumentList?.Arguments
                .Where(arg => arg.NameEquals != null)
                .Select(arg => arg.NameEquals!.Name.Identifier.ValueText)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (names == null)
                return;

            foreach (var name in names)
            {
                if (name.EndsWith("LocKey", StringComparison.Ordinal))
                {
                    var key = GetAttributeNamedString(attribute, context.SemanticModel, name, context.CancellationToken);
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    var prefix = name.Substring(0, name.Length - "LocKey".Length);
                    var table = GetAttributeNamedString(attribute, context.SemanticModel, prefix + "LocTable", context.CancellationToken) ?? "settings_ui";
                    AddRequirement(context, LocalizationRequirement.Table(
                        "ModSettings text",
                        key!,
                        attribute.GetLocation(),
                        table,
                        ImmutableArray.Create(key!)));
                    continue;
                }

                if (!name.EndsWith("Key", StringComparison.Ordinal) || name.EndsWith("DataKey", StringComparison.Ordinal))
                    continue;

                var i18nKey = GetAttributeNamedString(attribute, context.SemanticModel, name, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(i18nKey))
                    continue;

                AddRequirement(context, LocalizationRequirement.I18N(
                    "ModSettings text",
                    i18nKey!,
                    attribute.GetLocation(),
                    ImmutableArray.Create(i18nKey!)));
            }
        }

        private void AnalyzeSettingsAttributeShape(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            if (attributeName is "ModSettingsSliderAttribute" or "ModSettingsIntSliderAttribute")
            {
                var min = GetAttributeNumericArgument(attribute, context.SemanticModel, 2, context.CancellationToken);
                var max = GetAttributeNumericArgument(attribute, context.SemanticModel, 3, context.CancellationToken);
                var step = GetAttributeNumericArgument(attribute, context.SemanticModel, 4, context.CancellationToken);
                AnalyzeNumericRange(min, max, step ?? 1, attribute.GetLocation(), "settings slider", context);
            }

            if (attributeName == "ModSettingsStringAttribute")
            {
                var maxLength = GetAttributeNamedInt(attribute, context.SemanticModel, "MaxLength", context.CancellationToken);
                if (maxLength < 0)
                    ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, attribute.GetLocation(),
                        RitsuLibUiText.SettingsMaxLengthNegative);
            }
        }

        private void AnalyzeSettingsBindingCallbacks(AttributeSyntax attribute, SyntaxNodeAnalysisContext context)
        {
            foreach (var name in new[]
                     {
                         "ReadUsing", "WriteUsing", "SaveUsing", "DefaultUsing", "AdapterUsing",
                         "ProjectParentReadUsing", "ProjectParentWriteUsing", "ProjectParentSaveUsing",
                         "ProjectGetUsing", "ProjectSetUsing", "ValidateUsing", "VisibleWhen",
                     })
            {
                var methodName = GetAttributeNamedString(attribute, context.SemanticModel, name, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(methodName))
                    continue;

                var type = attribute.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                var symbol = type == null ? null : context.SemanticModel.GetDeclaredSymbol(type, context.CancellationToken);
                if (symbol == null || HasAnyMethod(symbol, methodName!))
                    continue;

                ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, attribute.GetLocation(),
                    RitsuLibUiText.SettingsCallbackNotFound(methodName!, symbol.Name),
                    Properties(
                        (RitsuLibDiagnosticProperties.StubKind, "SettingsCallback"),
                        (RitsuLibDiagnosticProperties.TypeName, symbol.Name),
                        (RitsuLibDiagnosticProperties.MethodName, methodName!)));
            }
        }

        private void RegisterSettingsPage(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            SyntaxNodeAnalysisContext context)
        {
            var modId = GetInvocationStringArgument(invocation, method, "modId", 0, context.SemanticModel, context.CancellationToken);
            var pageId = GetInvocationStringArgument(invocation, method, "pageId", 2, context.SemanticModel, context.CancellationToken) ?? modId;
            ReportIdShapeIfNeeded(context, invocation.GetLocation(), "settings page id", pageId);
            RegisterSettingsPage(invocation.GetLocation(), modId, pageId);
        }

        private void RegisterSettingsPage(Location location, string? modId, string? pageId)
        {
            if (string.IsNullOrWhiteSpace(modId))
                modId = _fallbackOwner ?? _additionalFiles.Manifest.ModId ?? "?";
            if (string.IsNullOrWhiteSpace(pageId))
                pageId = modId;

            lock (_gate)
                _settingsPages.Add(new(modId!, pageId!, location));
        }

        private void RegisterSettingsSection(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            SyntaxNodeAnalysisContext context)
        {
            var sectionId = GetInvocationStringArgument(invocation, method, "id", 0, context.SemanticModel, context.CancellationToken);
            var page = FindContainingSettingsPage(invocation, context);
            RegisterSettingsSection(invocation.GetLocation(), page, sectionId, context);
        }

        private void RegisterSettingsSection(
            Location location,
            SettingsPageKey page,
            string? sectionId,
            SyntaxNodeAnalysisContext context)
        {
            if (string.IsNullOrWhiteSpace(sectionId))
            {
                ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, location,
                    RitsuLibUiText.SettingsSectionIdEmpty);
                return;
            }

            ReportIdShapeIfNeeded(context, location, "settings section id", sectionId);
            var pageKey = page.ToKey();
            bool duplicate;
            lock (_gate)
            {
                if (!_settingsSectionsByPage.TryGetValue(pageKey, out var sections))
                {
                    sections = new(StringComparer.OrdinalIgnoreCase);
                    _settingsSectionsByPage[pageKey] = sections;
                }

                duplicate = !sections.Add(sectionId!);
            }

            if (duplicate)
                ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, location,
                    RitsuLibUiText.SettingsSectionDuplicate(sectionId!, page.PageId));
        }

        private void RegisterSettingsEntry(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            var entryId = GetInvocationStringArgument(invocation, method, "id", 0, context.SemanticModel, context.CancellationToken);
            var page = FindContainingSettingsPage(invocation, context);
            var sectionId = FindContainingSettingsSectionId(invocation, context);
            RegisterSettingsEntry(invocation.GetLocation(), page, sectionId, entryId, context);
        }

        private void RegisterSettingsEntry(
            Location location,
            SettingsPageKey page,
            string? sectionId,
            string? entryId,
            SyntaxNodeAnalysisContext context)
        {
            if (string.IsNullOrWhiteSpace(entryId))
            {
                ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, location,
                    RitsuLibUiText.SettingsEntryIdEmpty);
                return;
            }

            if (string.IsNullOrWhiteSpace(sectionId))
            {
                ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, location,
                    RitsuLibUiText.SettingsEntryNoSection(entryId!));
                return;
            }

            ReportIdShapeIfNeeded(context, location, "settings entry id", entryId);
            var key = SettingsEntryKey(page, sectionId!);
            lock (_gate)
            {
                if (!_settingsEntriesByPageSection.TryGetValue(key, out var entries))
                {
                    entries = new();
                    _settingsEntriesByPageSection[key] = entries;
                }

                entries.Add(new(page, sectionId!, entryId!, location));
            }
        }

        private void RegisterSettingsReflectionProvider(InvocationExpressionSyntax invocation, IMethodSymbol? method)
        {
            string? provider = null;
            if (method is { TypeArguments.Length: > 0 })
                provider = method.TypeArguments[0].ToDisplayString();
            else
            {
                var arg = FindInvocationArgument(invocation, method, "providerType", 0);
                if (arg?.Expression is TypeOfExpressionSyntax typeOf)
                {
                    provider = typeOf.Type.ToString();
                }
            }

            if (!string.IsNullOrWhiteSpace(provider))
                lock (_gate)
                    _settingsProviderTypes.Add(provider!);
        }

        private void AnalyzeSliderArguments(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            var min = GetInvocationNumericArgument(invocation, method, "minValue", 3, context.SemanticModel, context.CancellationToken)
                      ?? GetInvocationNumericArgument(invocation, method, "min", 3, context.SemanticModel, context.CancellationToken);
            var max = GetInvocationNumericArgument(invocation, method, "maxValue", 4, context.SemanticModel, context.CancellationToken)
                      ?? GetInvocationNumericArgument(invocation, method, "max", 4, context.SemanticModel, context.CancellationToken);
            var step = GetInvocationNumericArgument(invocation, method, "step", 5, context.SemanticModel, context.CancellationToken);
            AnalyzeNumericRange(min, max, step ?? 1, invocation.GetLocation(), methodName, context);
        }

        private void AnalyzeChoiceArguments(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            SyntaxNodeAnalysisContext context)
        {
            var options = FindInvocationArgument(invocation, method, "options", 3);
            if (options?.Expression is not ArrayCreationExpressionSyntax { Initializer.Expressions.Count: 0 } and
                not ImplicitArrayCreationExpressionSyntax { Initializer.Expressions.Count: 0 })
                return;

            ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, options.GetLocation(),
                RitsuLibUiText.SettingsChoiceEmpty);
        }

        private void AnalyzeNumericRange(
            double? min,
            double? max,
            double? step,
            Location location,
            string label,
            SyntaxNodeAnalysisContext context)
        {
            if (min.HasValue && max.HasValue && min.Value >= max.Value)
                ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, location,
                    RitsuLibUiText.SettingsMinMustBeLessThanMax(label));
            if (step.HasValue && step.Value <= 0)
                ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, location,
                    RitsuLibUiText.SettingsStepMustBePositive(label));
        }

        private void AnalyzeSettingsButtonMethod(
            MethodDeclarationSyntax method,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            var useHost = GetAttributeNamedBool(attribute, context.SemanticModel, "UseHostContext", context.CancellationToken);
            if (useHost == true && method.ParameterList.Parameters.Count != 1)
            {
                ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, method.Identifier.GetLocation(),
                    RitsuLibUiText.SettingsButtonUseHostParamCount);
            }
            else if (useHost != true && method.ParameterList.Parameters.Count != 0)
            {
                ReportContract(context, RitsuLibDiagnostics.SettingsContractRule, method.Identifier.GetLocation(),
                    RitsuLibUiText.SettingsButtonShouldBeParameterless);
            }
        }

        private void AnalyzePatchMethodType(
            TypeDeclarationSyntax declaration,
            INamedTypeSymbol type,
            SyntaxNodeAnalysisContext context)
        {
            if (!HasStaticProperty(type, "PatchId"))
            {
                ReportContract(context, RitsuLibDiagnostics.PatchContractRule, declaration.Identifier.GetLocation(),
                    RitsuLibUiText.PatchMethodMissingPatchId(type.Name),
                    Properties(
                        (RitsuLibDiagnosticProperties.StubKind, "PatchMethod"),
                        (RitsuLibDiagnosticProperties.TypeName, type.Name)));
            }

            if (!HasStaticMethod(type, "GetTargets"))
            {
                ReportContract(context, RitsuLibDiagnostics.PatchContractRule, declaration.Identifier.GetLocation(),
                    RitsuLibUiText.PatchMethodMissingGetTargets(type.Name),
                    Properties(
                        (RitsuLibDiagnosticProperties.StubKind, "PatchMethod"),
                        (RitsuLibDiagnosticProperties.TypeName, type.Name)));
            }
        }

        private void AnalyzeModPatchesType(
            TypeDeclarationSyntax declaration,
            INamedTypeSymbol type,
            SyntaxNodeAnalysisContext context)
        {
            if (HasStaticMethod(type, "AddTo"))
                return;

            ReportContract(context, RitsuLibDiagnostics.PatchContractRule, declaration.Identifier.GetLocation(),
                RitsuLibUiText.ModPatchesMissingAddTo(type.Name),
                Properties(
                    (RitsuLibDiagnosticProperties.StubKind, "ModPatches"),
                    (RitsuLibDiagnosticProperties.TypeName, type.Name)));
        }

        private void AnalyzeDynamicPatchFromMethod(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            SyntaxNodeAnalysisContext context)
        {
            var typeArg = FindInvocationArgument(invocation, method, "patchType", 0);
            var methodName = GetInvocationStringArgument(invocation, method, "methodName", 1, context.SemanticModel, context.CancellationToken);
            var type = RitsuLibSyntaxFacts.GetTypeSymbolFromTypeOf(typeArg?.Expression, context.SemanticModel, context.CancellationToken);
            if (type == null || string.IsNullOrWhiteSpace(methodName))
                return;

            var target = type.GetMembers(methodName!)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(member => member.IsStatic);
            if (target != null)
                return;

            ReportContract(context, RitsuLibDiagnostics.PatchTargetRule, invocation.GetLocation(),
                RitsuLibUiText.DynamicPatchFromMethodNotFound(methodName!, type.Name),
                Properties(
                    (RitsuLibDiagnosticProperties.StubKind, "PatchTargetMethod"),
                    (RitsuLibDiagnosticProperties.TypeName, type.Name),
                    (RitsuLibDiagnosticProperties.MethodName, methodName!)));
        }

        private void AnalyzeDynamicPatchTargetInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            var typeArg = FindInvocationArgument(invocation, method, "targetType", 0);
            var memberNameParameter = methodName == "AddPropertyGetter" ? "propertyName" : "methodName";
            var targetName = GetInvocationStringArgument(invocation, method, memberNameParameter, 1, context.SemanticModel, context.CancellationToken);
            var type = RitsuLibSyntaxFacts.GetTypeSymbolFromTypeOf(typeArg?.Expression, context.SemanticModel, context.CancellationToken);
            if (type == null || string.IsNullOrWhiteSpace(targetName))
                return;

            var exists = methodName == "AddPropertyGetter"
                ? type.GetMembers(targetName!).OfType<IPropertySymbol>().Any()
                : HasAnyMethod(type, targetName!);
            if (exists)
                return;

            ReportContract(context, RitsuLibDiagnostics.PatchTargetRule, invocation.GetLocation(),
                RitsuLibUiText.DynamicPatchTargetNotFound(methodName, type.Name, targetName!),
                Properties(
                    (RitsuLibDiagnosticProperties.StubKind, methodName == "AddPropertyGetter" ? "PatchTargetProperty" : "PatchTargetMethod"),
                    (RitsuLibDiagnosticProperties.TypeName, type.Name),
                    (RitsuLibDiagnosticProperties.MethodName, targetName!)));
        }

        private void AnalyzeModPatchTargetCreation(BaseObjectCreationExpressionSyntax creation, SyntaxNodeAnalysisContext context)
        {
            var args = creation.ArgumentList?.Arguments;
            if (args == null || args.Value.Count < 2)
                return;

            var type = RitsuLibSyntaxFacts.GetTypeSymbolFromTypeOf(args.Value[0].Expression, context.SemanticModel, context.CancellationToken);
            var methodName = GetConstantString(args.Value[1].Expression, context.SemanticModel, context.CancellationToken);
            if (type == null || string.IsNullOrWhiteSpace(methodName))
                return;

            if (HasAnyMethod(type, methodName!))
                return;

            ReportContract(context, RitsuLibDiagnostics.PatchTargetRule, creation.GetLocation(),
                RitsuLibUiText.ModPatchTargetMethodNotFound(type.Name, methodName!),
                Properties(
                    (RitsuLibDiagnosticProperties.StubKind, "PatchTargetMethod"),
                    (RitsuLibDiagnosticProperties.TypeName, type.Name),
                    (RitsuLibDiagnosticProperties.MethodName, methodName!)));
        }

        private void AnalyzeAttributeResourcePaths(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            foreach (var name in new[] { "IconPath", "ScenePath", "TexturePath", "ResourcePath" })
            {
                var value = GetAttributeNamedString(attribute, context.SemanticModel, name, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                AnalyzeResourceString(value!, attribute.GetLocation(), context);
            }
        }

        private void AnalyzeResourceString(string value, Location location, SyntaxNodeAnalysisContext context)
        {
            if (value.StartsWith("event:/", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("snapshot:/", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("bus:/", StringComparison.OrdinalIgnoreCase))
                return;

            if (!RitsuLibAdditionalFileIndex.IsResourcePath(value))
            {
                if (LooksLikeFileResource(value))
                {
                    ReportContract(context, RitsuLibDiagnostics.ResourcePathRule, location,
                        RitsuLibUiText.ResourcePathMissingPrefix(value),
                        Properties(
                            (RitsuLibDiagnosticProperties.StubKind, "ResourcePath"),
                            (RitsuLibDiagnosticProperties.ResourcePath, value)));
                }

                return;
            }

            if (_additionalFiles.HasAssetIndex && !_additionalFiles.ResourceExists(value))
            {
                ReportContract(context, RitsuLibDiagnostics.ResourcePathRule, location,
                    RitsuLibUiText.ResourcePathNotFound(value),
                    Properties(
                        (RitsuLibDiagnosticProperties.StubKind, "ResourcePath"),
                        (RitsuLibDiagnosticProperties.ResourcePath, value)));
            }
        }

        private void AnalyzeAudioString(
            string value,
            string methodName,
            Location location,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName.Contains("Bus", StringComparison.OrdinalIgnoreCase) && value.Contains(":/", StringComparison.Ordinal) &&
                !value.StartsWith("bus:/", StringComparison.OrdinalIgnoreCase))
            {
                ReportContract(context, RitsuLibDiagnostics.AudioStringRule, location,
                    RitsuLibUiText.FmodBusPathPrefix(value));
            }

            if ((methodName.Contains("Event", StringComparison.OrdinalIgnoreCase) || methodName == "Event") &&
                value.Contains(":/", StringComparison.Ordinal) &&
                !value.StartsWith("event:/", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("snapshot:/", StringComparison.OrdinalIgnoreCase))
            {
                ReportContract(context, RitsuLibDiagnostics.AudioStringRule, location,
                    RitsuLibUiText.FmodEventPathPrefix(value));
            }

            if (methodName.Contains("Guid", StringComparison.OrdinalIgnoreCase) &&
                !RitsuLibAdditionalFileIndex.IsResourcePath(value) &&
                !value.EndsWith(".guids.txt", StringComparison.OrdinalIgnoreCase) &&
                !FmodGuidRegex.IsMatch(value))
            {
                ReportContract(context, RitsuLibDiagnostics.AudioStringRule, location,
                    RitsuLibUiText.FmodGuidInvalid(value));
            }

            if (methodName.Contains("Bank", StringComparison.OrdinalIgnoreCase) && !value.EndsWith(".bank", StringComparison.OrdinalIgnoreCase))
                ReportContract(context, RitsuLibDiagnostics.AudioStringRule, location,
                    RitsuLibUiText.FmodBankMissingExtension(value));
        }

        private void AnalyzeRuntimeHotkeyOptionsCreation(BaseObjectCreationExpressionSyntax creation, SyntaxNodeAnalysisContext context)
        {
            if (creation.Initializer == null)
                return;

            foreach (var expression in creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>())
            {
                if (expression.Left is not IdentifierNameSyntax { Identifier.ValueText: "Id" } and
                    not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Id" })
                    continue;

                var value = GetConstantString(expression.Right, context.SemanticModel, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(value))
                    ReportContract(context, RitsuLibDiagnostics.RuntimeHelperRule, expression.GetLocation(),
                        RitsuLibUiText.HotkeyOptionsIdEmpty);
                else
                    ReportIdShapeIfNeeded(context, expression.GetLocation(), "runtime hotkey id", value);
            }
        }

        // RITSU017: Disposable handle not disposed
        private void AnalyzeDisposableInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (!RitsuLibSyntaxFacts.IsDisposableReturningMethod(methodName, method))
                return;

            if (invocation.Parent is not ExpressionStatementSyntax)
                return;

            MarkUsesRitsuLib();
            var returnTypeName = method?.ReturnType?.Name ?? "IDisposable";
            Report(context, RitsuLibDiagnostics.DisposableNotDisposedRule, invocation.GetLocation(),
                Properties(
                    (RitsuLibDiagnosticProperties.DisposableMethod, methodName),
                    (RitsuLibDiagnosticProperties.ReturnTypeName, returnTypeName)),
                methodName, returnTypeName);
        }

        // RITSU019: AudioSource path shape
        private void AnalyzeAudioSourceInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (method?.ContainingType?.Name != "AudioSource")
                return;

            if (methodName is not ("Event" or "Snapshot" or "Guid"))
                return;

            MarkUsesRitsuLib();
            var value = GetInvocationStringArgument(invocation, method, "path", 0, context.SemanticModel, context.CancellationToken)
                        ?? GetInvocationStringArgument(invocation, method, "guid", 0, context.SemanticModel, context.CancellationToken);
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (methodName == "Event" && !value!.StartsWith("event:/", StringComparison.OrdinalIgnoreCase))
            {
                ReportContract(context, RitsuLibDiagnostics.AudioSourcePathShapeRule, invocation.GetLocation(),
                    RitsuLibUiText.AudioSourceEventPrefix(value!),
                    Properties(
                        (RitsuLibDiagnosticProperties.SourceMethod, "Event"),
                        (RitsuLibDiagnosticProperties.ExpectedPrefix, "event:/")));
            }
            else if (methodName == "Snapshot" && !value!.StartsWith("snapshot:/", StringComparison.OrdinalIgnoreCase))
            {
                ReportContract(context, RitsuLibDiagnostics.AudioSourcePathShapeRule, invocation.GetLocation(),
                    RitsuLibUiText.AudioSourceSnapshotPrefix(value!),
                    Properties(
                        (RitsuLibDiagnosticProperties.SourceMethod, "Snapshot"),
                        (RitsuLibDiagnosticProperties.ExpectedPrefix, "snapshot:/")));
            }
            else if (methodName == "Guid" && !FmodGuidRegex.IsMatch(value!))
            {
                ReportContract(context, RitsuLibDiagnostics.AudioSourcePathShapeRule, invocation.GetLocation(),
                    RitsuLibUiText.AudioSourceGuidInvalid(value!),
                    Properties(
                        (RitsuLibDiagnosticProperties.SourceMethod, "Guid"),
                        (RitsuLibDiagnosticProperties.ExpectedPrefix, "GUID")));
            }
        }

        // RITSU020/RITSU023: Interop attribute validation
        private void AnalyzeInteropAttribute(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            if (attributeName == "ModInteropAttribute")
            {
                MarkUsesRitsuLib();
                var modId = GetAttributeStringArgument(attribute, context.SemanticModel, 0, context.CancellationToken);
                if (string.IsNullOrWhiteSpace(modId))
                {
                    ReportContract(context, RitsuLibDiagnostics.ModInteropShapeRule, attribute.GetLocation(),
                        RitsuLibUiText.ModInteropRequiresModId);
                    return;
                }

                if (!RitsuLibSyntaxFacts.HasRecommendedIdShape(modId!))
                {
                    ReportContract(context, RitsuLibDiagnostics.ModInteropShapeRule, attribute.GetLocation(),
                        RitsuLibUiText.ModInteropDiscouragedFormat(modId!));
                }
                return;
            }

            if (attributeName == "InteropTargetAttribute")
            {
                MarkUsesRitsuLib();
                var containingType = attribute.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                if (containingType == null)
                    return;

                var hasModInterop = containingType.AttributeLists
                    .SelectMany(list => list.Attributes)
                    .Any(attr => GetAttributeShortName(attr) == "ModInteropAttribute");

                if (!hasModInterop)
                {
                    ReportContract(context, RitsuLibDiagnostics.InteropTargetShapeRule, attribute.GetLocation(),
                        RitsuLibUiText.InteropTargetRequiresModInterop);
                }
            }
        }

        // RITSU025: Lifecycle event type constraint
        private void AnalyzeLifecycleTypeConstraint(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            if (methodName != "SubscribeLifecycleOnce")
                return;

            if (method?.ContainingType?.Name != "RitsuLibFramework" && method != null)
                return;

            MarkUsesRitsuLib();
            var typeArg = method?.TypeArguments.FirstOrDefault();
            if (typeArg == null)
                return;

            if (typeArg is INamedTypeSymbol namedType && !namedType.IsSealed && typeArg.TypeKind != TypeKind.Struct)
            {
                Report(context, RitsuLibDiagnostics.LifecycleEventTypeRule, invocation.GetLocation(),
                    Properties((RitsuLibDiagnosticProperties.TypeName, typeArg.Name)),
                    typeArg.Name);
            }
        }

        private void ReportDuplicatePublicEntries(
            CompilationAnalysisContext context,
            RegisteredPublicEntry[] entries,
            string? fallbackOwner)
        {
            var resolved = entries
                .Select(entry => entry.Resolve(fallbackOwner))
                .Where(entry => entry != null)
                .Cast<ResolvedPublicEntry>()
                .GroupBy(entry => entry.Entry, StringComparer.OrdinalIgnoreCase);

            foreach (var group in resolved)
            {
                var distinctTypes = group.Select(entry => entry.TypeName).Distinct(StringComparer.Ordinal).ToArray();
                if (distinctTypes.Length < 2)
                    continue;

                Report(
                    context,
                    RitsuLibDiagnostics.DuplicatePublicEntryRule,
                    group.First().Location,
                    Properties((RitsuLibDiagnosticProperties.SymbolName, group.Key)),
                    group.Key);
            }
        }

        private void ReportSettingsGraphIssues(
            CompilationAnalysisContext context,
            SettingsPageRegistration[] pages,
            Dictionary<string, HashSet<string>> sectionsByPage,
            Dictionary<string, List<SettingsEntryRegistration>> entriesByPageSection)
        {
            foreach (var group in pages.GroupBy(page => page.ToKey(), StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() < 2)
                    continue;

                var first = group.First();
                Report(context, RitsuLibDiagnostics.SettingsContractRule, first.Location, EmptyProperties(),
                    $"Duplicate settings page id '{first.PageId}' for mod '{first.ModId}'.");
            }

            foreach (var pair in entriesByPageSection)
            {
                var pageKey = SettingsPageKeyPart(pair.Key);
                var sectionId = SettingsSectionKeyPart(pair.Key);
                var sectionExists = sectionsByPage.TryGetValue(pageKey, out var sections) && sections.Contains(sectionId);
                foreach (var entryGroup in pair.Value.GroupBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase))
                {
                    if (entryGroup.Count() > 1)
                    {
                        var first = entryGroup.First();
                        Report(context, RitsuLibDiagnostics.SettingsContractRule, first.Location, EmptyProperties(),
                            $"Duplicate settings entry id '{first.EntryId}' in section '{first.SectionId}'.");
                    }
                }

                if (sectionExists)
                    continue;

                foreach (var entry in pair.Value)
                {
                    Report(context, RitsuLibDiagnostics.SettingsContractRule, entry.Location,
                        Properties((RitsuLibDiagnosticProperties.TargetSection, entry.SectionId)),
                        $"Settings entry '{entry.EntryId}' references missing section '{entry.SectionId}'.");
                }
            }
        }

        private void ReportDuplicateDataStoreKeys(CompilationAnalysisContext context, DataStoreRegistration[] registrations)
        {
            foreach (var group in registrations.GroupBy(registration => registration.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() < 2)
                    continue;

                Report(context, RitsuLibDiagnostics.DataStoreContractRule, group.First().Location, EmptyProperties(),
                    $"Duplicate ModDataStore key '{group.Key}'.");
            }
        }

        // RITSU022/RITSU024: Settings subpage reference validation
        private void ReportSettingsSubpageIssues(
            CompilationAnalysisContext context,
            SettingsPageRegistration[] pages,
            SettingsSubpageReference[] subpageReferences)
        {
            var pageKeys = new HashSet<string>(
                pages.Select(p => p.PageId),
                StringComparer.OrdinalIgnoreCase);

            // RITSU022: Check for references to non-existent pages
            foreach (var subpageRef in subpageReferences)
            {
                if (!pageKeys.Contains(subpageRef.TargetPageId))
                {
                    Report(context, RitsuLibDiagnostics.SettingsSubpageReferenceRule, subpageRef.Location,
                        Properties((RitsuLibDiagnosticProperties.TargetPage, subpageRef.TargetPageId)),
                        $"AddSubpage references page '{subpageRef.TargetPageId}' which is not registered.");
                }
            }

            // RITSU024: Check for duplicate subpage references in the same section
            foreach (var group in subpageReferences
                         .GroupBy(s => s.Page.ToKey() + "" + s.SectionId + "" + s.TargetPageId, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() < 2)
                    continue;

                Report(context, RitsuLibDiagnostics.SettingsDuplicateSubpageRule, group.First().Location,
                    Properties((RitsuLibDiagnosticProperties.DuplicatePage, group.First().TargetPageId)),
                    group.First().TargetPageId);
            }
        }

        private static SettingsPageKey FindContainingSettingsPage(SyntaxNode node, SyntaxNodeAnalysisContext context)
        {
            var invocationRoot = node as InvocationExpressionSyntax ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            var invocation = invocationRoot?.DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(candidate => GetInvokedMemberName(candidate) == "RegisterModSettings");
            if (invocation == null)
            {
                var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                if (type != null)
                {
                    var pageAttr = type.AttributeLists.SelectMany(list => list.Attributes)
                        .FirstOrDefault(attr => GetAttributeShortName(attr) == "ModSettingsPageAttribute");
                    if (pageAttr != null)
                    {
                        var modId = GetAttributeStringArgument(pageAttr, context.SemanticModel, 0, context.CancellationToken);
                        var attributePageId = GetAttributeStringArgument(pageAttr, context.SemanticModel, 1, context.CancellationToken) ?? modId;
                        return new(modId ?? "?", attributePageId ?? modId ?? "?");
                    }
                }

                return SettingsPageKey.Unknown;
            }

            var method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
            var owner = GetInvocationStringArgument(invocation, method, "modId", 0, context.SemanticModel, context.CancellationToken) ?? "?";
            var pageId = GetInvocationStringArgument(invocation, method, "pageId", 2, context.SemanticModel, context.CancellationToken) ?? owner;
            return new(owner, pageId);
        }

        private static string? FindContainingSettingsSectionId(SyntaxNode node, SyntaxNodeAnalysisContext context)
        {
            var invocationRoot = node as InvocationExpressionSyntax ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            var addSection = invocationRoot?.DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(candidate => GetInvokedMemberName(candidate) == "AddSection");
            if (addSection == null)
                return null;

            var method = context.SemanticModel.GetSymbolInfo(addSection, context.CancellationToken).Symbol as IMethodSymbol;
            return GetInvocationStringArgument(addSection, method, "id", 0, context.SemanticModel, context.CancellationToken);
        }

        private static string SettingsEntryKey(SettingsPageKey page, string sectionId)
        {
            return page.ToKey() + "\u001E" + sectionId;
        }

        private static string SettingsPageKeyPart(string key)
        {
            var index = key.IndexOf('\u001E');
            return index < 0 ? key : key.Substring(0, index);
        }

        private static string SettingsSectionKeyPart(string key)
        {
            var index = key.IndexOf('\u001E');
            return index < 0 ? string.Empty : key.Substring(index + 1);
        }

        private static bool IsRitsuLibAttribute(string attributeName)
        {
            return attributeName.StartsWith("Register", StringComparison.Ordinal) ||
                   attributeName.StartsWith("AutoTimeline", StringComparison.Ordinal) ||
                   attributeName.StartsWith("Require", StringComparison.Ordinal) ||
                   attributeName.StartsWith("ModSettings", StringComparison.Ordinal) ||
                   attributeName == "RitsuLibOwnedByAttribute";
        }

        private static bool IsAutoRegistrationAttribute(string attributeName)
        {
            return attributeName.StartsWith("Register", StringComparison.Ordinal) ||
                   attributeName.StartsWith("AutoTimeline", StringComparison.Ordinal) ||
                   attributeName.StartsWith("Require", StringComparison.Ordinal);
        }

        private static bool IsRitsuLibInvocation(string methodName, IMethodSymbol? method)
        {
            if (method?.ContainingNamespace.ToDisplayString().StartsWith("STS2RitsuLib", StringComparison.Ordinal) == true)
                return true;

            return methodName is
                "RegisterModAssembly" or
                "EnsureGodotScriptsRegistered" or
                "CreateContentPack" or
                "CreateModLocalization" or
                "GetContentRegistry" or
                "GetKeywordRegistry" or
                "GetCardTagRegistry" or
                "GetTimelineRegistry" or
                "GetUnlockRegistry" or
                "GetDataStore" or
                "CreatePatcher" or
                "RegisterModSettings" or
                "RegisterHealthBarForecast" or
                "RegisterHealthBarVisualGraft" or
                "RegisterFreePlayBinding" or
                "WithSharedTooltip" or
                "WithTooltip";
        }

        private static bool IsSettingsInvocation(string methodName, IMethodSymbol? method)
        {
            return methodName == "RegisterModSettings" ||
                   methodName is "RegisterModSettingsReflectionProvider" or "RegisterModSettingsReflectionProviderAndTryRegister" ||
                   method?.ContainingType?.Name is "ModSettingsPageBuilder" or "ModSettingsSectionBuilder" ||
                   IsSettingsEntryBuilderMethod(methodName);
        }

        private static bool IsSettingsEntryBuilderMethod(string methodName)
        {
            return methodName is
                "AddHeader" or "AddParagraph" or "AddInfoCard" or "AddRuntimeHotkeySummary" or "AddImage" or
                "AddList" or "AddToggle" or "AddIntSlider" or "AddSlider" or "AddChoice" or "AddEnumChoice" or
                "AddColor" or "AddString" or "AddMultilineString" or "AddKeyBinding" or "AddButton" or
                "AddSubpage" or "AddCustom";
        }

        private static bool IsSettingsEntryAttribute(string attributeName)
        {
            return attributeName is
                "ModSettingsToggleAttribute" or "ModSettingsSliderAttribute" or "ModSettingsIntSliderAttribute" or
                "ModSettingsStringAttribute" or "ModSettingsMultilineStringAttribute" or "ModSettingsColorAttribute" or
                "ModSettingsKeyBindingAttribute" or "ModSettingsChoiceAttribute" or "ModSettingsButtonAttribute" or
                "ModSettingsParagraphAttribute" or "ModSettingsHeaderAttribute" or "ModSettingsInfoCardAttribute" or
                "ModSettingsRuntimeHotkeySummaryAttribute" or "ModSettingsImageAttribute" or
                "ModSettingsSubpageAttribute" or "ModSettingsCustomEntryAttribute";
        }

        private static bool MayUseResourcePath(string methodName, IMethodSymbol? method)
        {
            return IsLikelyResourceMethod(methodName) ||
                   method?.Parameters.Any(parameter => IsResourceArgumentName(parameter.Name)) == true;
        }

        private static bool IsLikelyResourceMethod(string methodName)
        {
            return methodName.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
                   methodName.Contains("Scene", StringComparison.OrdinalIgnoreCase) ||
                   methodName.Contains("Texture", StringComparison.OrdinalIgnoreCase) ||
                   methodName.Contains("Icon", StringComparison.OrdinalIgnoreCase) ||
                   methodName.Contains("Bank", StringComparison.OrdinalIgnoreCase) ||
                   methodName.Contains("Guid", StringComparison.OrdinalIgnoreCase) ||
                   methodName is "Event" or "Guid" or "RegisterBank" or "RegisterStudioGuidMappings";
        }

        private static bool IsResourceArgumentName(string? name)
        {
            if (name == null)
                return false;

            return name.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Resource", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Scene", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Texture", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Icon", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Bank", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Guid", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeFileResource(string value)
        {
            return value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".tres", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".bank", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".guids.txt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsContentPackRegistrationMethod(string? methodName)
        {
            return methodName is
                "Character" or "Act" or "Monster" or "Card" or "Relic" or "Potion" or "Power" or "Orb" or
                "Enchantment" or "Affliction" or "Achievement" or "HealthBarForecast" or "ActEvent" or
                "CardKeywordOwnedByLocNamespace" or "CardKeyword" or "KeywordOwned" or "Keyword" or "Epoch" or
                "Story" or "RequireEpoch" or "BindCardUnlockEpoch" or "BindRelicUnlockEpoch" or
                "UnlockEpochAfterRunAs" or "UnlockEpochAfterWinAs" or "CardTagOwned" or "Manifest" or
                "ContentManifest" or "KeywordManifest" or "CardTagManifest" or "PackManifest" or "PackEntry" or
                "Entry" or "Entries" or "Custom" or "SharedAncient" or "ActAncient" or "ActEncounter" or
                "GlobalEncounter" or "SharedEvent";
        }

        private static InvocationExpressionSyntax GetOutermostInvocationInChain(InvocationExpressionSyntax invocation)
        {
            var current = invocation;
            for (var node = invocation.Parent; node != null; node = node.Parent)
            {
                if (node is InvocationExpressionSyntax parentInvocation && ContainsNode(parentInvocation, current))
                {
                    current = parentInvocation;
                    continue;
                }

                if (node is MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or ConditionalAccessExpressionSyntax)
                    continue;

                break;
            }

            return current;
        }

        private static bool InvocationChainContains(InvocationExpressionSyntax invocation, string methodName)
        {
            var outer = GetOutermostInvocationInChain(invocation);
            return outer.DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(candidate => GetInvokedMemberName(candidate) == methodName);
        }

        private static bool InvocationChainContainsContentPackRegistration(InvocationExpressionSyntax invocation)
        {
            var outer = GetOutermostInvocationInChain(invocation);
            return outer.DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(candidate => IsContentPackRegistrationMethod(GetInvokedMemberName(candidate)));
        }

        private static bool ContainsNode(SyntaxNode root, SyntaxNode node)
        {
            return root.Span.Contains(node.Span);
        }

        private static bool IsPlausibleHotkey(string? binding)
        {
            if (string.IsNullOrWhiteSpace(binding))
                return false;

            var parts = binding!.Split('+');
            return parts.Length > 0 && parts.All(part => !string.IsNullOrWhiteSpace(part));
        }

        private static bool Implements(INamedTypeSymbol type, string interfaceName)
        {
            return type.AllInterfaces.Any(i => i.Name == interfaceName) ||
                   type.Interfaces.Any(i => i.Name == interfaceName) ||
                   type.DeclaringSyntaxReferences
                       .Select(reference => reference.GetSyntax())
                       .OfType<TypeDeclarationSyntax>()
                       .SelectMany(declaration => declaration.BaseList?.Types ?? default)
                       .Any(baseType => baseType.Type.ToString().Split('.').Last() == interfaceName);
        }

        private static bool HasBaseType(INamedTypeSymbol type, string baseTypeName)
        {
            for (var current = type.BaseType; current != null; current = current.BaseType)
            {
                if (current.Name == baseTypeName)
                    return true;
            }

            return false;
        }

        private static bool HasStaticProperty(INamedTypeSymbol type, string name)
        {
            return type.GetMembers(name).OfType<IPropertySymbol>().Any(member => member.IsStatic);
        }

        private static bool HasStaticMethod(INamedTypeSymbol type, string name)
        {
            return type.GetMembers(name).OfType<IMethodSymbol>().Any(member => member.IsStatic);
        }

        private static bool HasAnyMethod(INamedTypeSymbol type, string name)
        {
            return type.GetMembers(name).OfType<IMethodSymbol>().Any();
        }

        private static bool IsNullLiteral(ExpressionSyntax expression)
        {
            return expression.IsKind(SyntaxKind.NullLiteralExpression);
        }

        private static double? GetInvocationNumericArgument(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string parameterName,
            int position,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var argument = FindInvocationArgument(invocation, method, parameterName, position);
            return argument == null ? null : GetNumericConstant(argument.Expression, semanticModel, cancellationToken);
        }

        private static double? GetAttributeNumericArgument(
            AttributeSyntax attribute,
            SemanticModel semanticModel,
            int index,
            System.Threading.CancellationToken cancellationToken)
        {
            var args = attribute.ArgumentList?.Arguments
                .Where(arg => arg.NameEquals == null && arg.NameColon == null)
                .ToArray();
            if (args == null || index >= args.Length)
                return null;

            return GetNumericConstant(args[index].Expression, semanticModel, cancellationToken);
        }

        private static int? GetAttributeNamedInt(
            AttributeSyntax attribute,
            SemanticModel semanticModel,
            string name,
            System.Threading.CancellationToken cancellationToken)
        {
            var argument = attribute.ArgumentList?.Arguments
                .FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.ValueText == name);
            if (argument == null)
                return null;

            var constant = semanticModel.GetConstantValue(argument.Expression, cancellationToken);
            if (!constant.HasValue || constant.Value == null)
                return null;

            return constant.Value switch
            {
                int value => value,
                short value => value,
                byte value => value,
                long value when value is <= int.MaxValue and >= int.MinValue => (int)value,
                _ => null,
            };
        }

        private static bool? GetAttributeNamedBool(
            AttributeSyntax attribute,
            SemanticModel semanticModel,
            string name,
            System.Threading.CancellationToken cancellationToken)
        {
            var argument = attribute.ArgumentList?.Arguments
                .FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.ValueText == name);
            if (argument == null)
                return null;

            var constant = semanticModel.GetConstantValue(argument.Expression, cancellationToken);
            return constant.HasValue ? constant.Value as bool? : null;
        }

        private static double? GetNumericConstant(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var constant = semanticModel.GetConstantValue(expression, cancellationToken);
            if (!constant.HasValue || constant.Value == null)
                return null;

            return constant.Value switch
            {
                double value => value,
                float value => value,
                decimal value => (double)value,
                int value => value,
                long value => value,
                short value => value,
                byte value => value,
                _ => null,
            };
        }

        private static string FormatModIdExpression(string modId)
        {
            return RecommendedLiteralIdRegex.IsMatch(modId)
                ? $"\"{modId}\""
                : "ModId";
        }

        private void MarkUsesRitsuLib()
        {
            lock (_gate)
                _usesRitsuLib = true;
        }

        private void ReportContract(
            SyntaxNodeAnalysisContext context,
            DiagnosticDescriptor descriptor,
            Location location,
            string message,
            ImmutableDictionary<string, string?>? properties = null)
        {
            Report(context, descriptor, location, properties ?? EmptyProperties(), message);
        }

        private static void Report(
            SyntaxNodeAnalysisContext context,
            DiagnosticDescriptor descriptor,
            Location location,
            ImmutableDictionary<string, string?> properties,
            params object[] args)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, properties, args));
        }

        private static void Report(
            CompilationAnalysisContext context,
            DiagnosticDescriptor descriptor,
            Location location,
            ImmutableDictionary<string, string?> properties,
            params object[] args)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, properties, args));
        }

        private static void Report(CompilationAnalysisContext context, RitsuDiagnostic diagnostic)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                diagnostic.Descriptor,
                diagnostic.Location,
                diagnostic.Properties,
                diagnostic.MessageArgs));
        }

        private static ImmutableDictionary<string, string?> EmptyProperties()
        {
            return ImmutableDictionary<string, string?>.Empty;
        }

        private static ImmutableDictionary<string, string?> Properties(params (string Key, string? Value)[] values)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
            foreach (var (key, value) in values)
                builder[key] = value;
            return builder.ToImmutable();
        }

        private static Location FirstLocationOrNone(RitsuDiagnostic[] diagnostics)
        {
            return diagnostics.FirstOrDefault().Location ?? Location.None;
        }

        private readonly struct RitsuDiagnostic
        {
            public RitsuDiagnostic(
                DiagnosticDescriptor descriptor,
                Location location,
                ImmutableDictionary<string, string?> properties,
                params object[] messageArgs)
            {
                Descriptor = descriptor;
                Location = location;
                Properties = properties;
                MessageArgs = messageArgs;
            }

            public DiagnosticDescriptor Descriptor { get; }
            public Location Location { get; }
            public ImmutableDictionary<string, string?> Properties { get; }
            public object[] MessageArgs { get; }
        }

        private readonly struct RegisteredPublicEntry
        {
            public RegisteredPublicEntry(
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

            public ResolvedPublicEntry? Resolve(string? fallbackOwner)
            {
                var owner = OwnerModId ?? fallbackOwner;
                if (string.IsNullOrWhiteSpace(owner))
                    return null;

                return new(GetModelEntry(owner!, CategoryStem, TypeName, PublicEntryOverride), TypeName, Location);
            }
        }

        private readonly struct ResolvedPublicEntry
        {
            public ResolvedPublicEntry(string entry, string typeName, Location location)
            {
                Entry = entry;
                TypeName = typeName;
                Location = location;
            }

            public string Entry { get; }
            public string TypeName { get; }
            public Location Location { get; }
        }

        private readonly struct ContentPackChain
        {
            public ContentPackChain(Location location, bool hasRegistration, bool hasApply, string entryPoint)
            {
                Location = location;
                HasRegistration = hasRegistration;
                HasApply = hasApply;
                EntryPoint = entryPoint;
            }

            public Location Location { get; }
            public bool HasRegistration { get; }
            public bool HasApply { get; }
            public string EntryPoint { get; }
        }

        private readonly struct SettingsPageKey
        {
            public static SettingsPageKey Unknown => new("?", "?");

            public SettingsPageKey(string modId, string pageId)
            {
                ModId = string.IsNullOrWhiteSpace(modId) ? "?" : modId;
                PageId = string.IsNullOrWhiteSpace(pageId) ? ModId : pageId;
            }

            public string ModId { get; }
            public string PageId { get; }

            public string ToKey()
            {
                return ModId + "\u001D" + PageId;
            }
        }

        private readonly struct SettingsPageRegistration
        {
            public SettingsPageRegistration(string modId, string pageId, Location location)
            {
                ModId = modId;
                PageId = pageId;
                Location = location;
            }

            public string ModId { get; }
            public string PageId { get; }
            public Location Location { get; }

            public string ToKey()
            {
                return new SettingsPageKey(ModId, PageId).ToKey();
            }
        }

        private readonly struct SettingsEntryRegistration
        {
            public SettingsEntryRegistration(SettingsPageKey page, string sectionId, string entryId, Location location)
            {
                Page = page;
                SectionId = sectionId;
                EntryId = entryId;
                Location = location;
            }

            public SettingsPageKey Page { get; }
            public string SectionId { get; }
            public string EntryId { get; }
            public Location Location { get; }
        }

        private readonly struct DataStoreRegistration
        {
            public DataStoreRegistration(string key, Location location)
            {
                Key = key;
                Location = location;
            }

            public string Key { get; }
            public Location Location { get; }
        }

        private readonly struct SettingsSubpageReference
        {
            public SettingsSubpageReference(SettingsPageKey page, string sectionId, string targetPageId, Location location)
            {
                Page = page;
                SectionId = sectionId;
                TargetPageId = targetPageId;
                Location = location;
            }

            public SettingsPageKey Page { get; }
            public string SectionId { get; }
            public string TargetPageId { get; }
            public Location Location { get; }
        }
    }
}
