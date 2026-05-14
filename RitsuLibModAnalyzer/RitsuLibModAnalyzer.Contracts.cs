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
        public void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
        {
            var creation = (BaseObjectCreationExpressionSyntax)context.Node;
            AnalyzeObjectCreationResourcePaths(creation, context);
        }

        public void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
        {
        }

        public void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
        {
            var property = (PropertyDeclarationSyntax)context.Node;
            if (!property.Modifiers.Any(SyntaxKind.OverrideKeyword))
                return;

            AnalyzeOverrideResourceProperty(property, context);
        }

        public void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
        {
        }

        private void AnalyzeContractAttribute(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            AnalyzeSettingsAttributeLocalization(attribute, context);
            AnalyzeAttributeResourcePaths(attributeName, attribute, context);
        }

        private void AnalyzeContractInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol? method,
            string methodName,
            SyntaxNodeAnalysisContext context)
        {
            AnalyzeDynamicVarTooltipInvocation(invocation, method, methodName, context);
            AnalyzeResourceInvocation(invocation, method, methodName, context);
        }

        private void AnalyzeSettingsAttribute(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            if (!attributeName.StartsWith("ModSettings", StringComparison.Ordinal))
                return;

            AnalyzeSettingsAttributeLocalization(attribute, context);
        }

        // RITSU001: Settings attribute localization tracking
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

        // RITSU001: Dynamic var tooltip localization tracking
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

        // RITSU013: Resource path analysis
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

                if (!TryResolveStringExpression(argument.Expression, context.SemanticModel, context.CancellationToken, out var value))
                    continue;

                AnalyzeResourceString(value!, argument.GetLocation(), context, IsFixableResourcePathExpression(argument.Expression));
            }
        }

        private void AnalyzeResourceString(string value, Location location, SyntaxNodeAnalysisContext context, bool fixable = true)
        {
            value = value.Trim();

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
                        fixable
                            ? Properties(
                                (RitsuLibDiagnosticProperties.StubKind, "ResourcePath"),
                                (RitsuLibDiagnosticProperties.ResourcePath, value))
                            : EmptyProperties());
                }

                return;
            }

            if (_additionalFiles.HasAssetIndex && !_additionalFiles.ResourceExists(value))
            {
                if (HasResourcePathNotFoundTodo(location, context, value))
                    return;

                var suggestedPath = _additionalFiles.TryFindExistingResourcePath(value);
                ReportContract(context, RitsuLibDiagnostics.ResourcePathRule, location,
                    RitsuLibUiText.ResourcePathNotFound(value),
                    Properties(
                        (RitsuLibDiagnosticProperties.StubKind, suggestedPath == null ? "ResourcePathNotFound" : "ResourcePath"),
                        (RitsuLibDiagnosticProperties.ResourcePath, value),
                        (RitsuLibDiagnosticProperties.SuggestedResourcePath, suggestedPath)));
            }
        }

        private static bool HasResourcePathNotFoundTodo(
            Location location,
            SyntaxNodeAnalysisContext context,
            string resourcePath)
        {
            if (!location.IsInSource)
                return false;

            var root = context.Node.SyntaxTree.GetRoot(context.CancellationToken);
            if (root.FullSpan.Length == 0)
                return false;

            if (!TryFindResourcePathTodoTarget(
                    root,
                    location,
                    context.SemanticModel,
                    resourcePath,
                    context.CancellationToken,
                    out var target))
                return false;

            return ResourcePathNotFoundTodoTextMatches(target.GetLeadingTrivia(), resourcePath) ||
                   ResourcePathNotFoundTodoTextMatches(target.GetTrailingTrivia(), resourcePath);
        }

        private static bool TryFindResourcePathTodoTarget(
            SyntaxNode root,
            Location location,
            SemanticModel semanticModel,
            string resourcePath,
            System.Threading.CancellationToken cancellationToken,
            out SyntaxNode target)
        {
            target = root;
            if (!location.IsInSource)
                return false;

            var span = location.SourceSpan;
            var node = span.Length == 0 && span.Start < root.FullSpan.End
                ? root.FindToken(span.Start).Parent
                : root.FindNode(span, getInnermostNodeForTie: true);
            if (node == null)
                return false;

            var expression = node.DescendantNodesAndSelf()
                .OfType<ExpressionSyntax>()
                .Where(expression => RitsuLibResourcePathFacts.TryResolveStringExpression(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var value) &&
                    string.Equals(value?.Trim(), resourcePath, StringComparison.Ordinal))
                .OrderBy(expression => expression.Span.Length)
                .FirstOrDefault();
            if (expression == null)
                return false;

            target = GetResourcePathTodoTarget(expression);
            return true;
        }

        private static SyntaxNode GetResourcePathTodoTarget(ExpressionSyntax expression)
        {
            if (expression.Parent is ArgumentSyntax argument)
            {
                var invocation = argument.FirstAncestorOrSelf<InvocationExpressionSyntax>();
                return invocation != null ? invocation : argument;
            }

            if (expression.Parent is AttributeArgumentSyntax attributeArgument)
                return attributeArgument;

            if (expression.Parent is AssignmentExpressionSyntax assignment &&
                assignment.Right == expression)
                return assignment;

            return expression;
        }

        private static bool ResourcePathNotFoundTodoTextMatches(SyntaxTriviaList triviaList, string resourcePath)
        {
            foreach (var trivia in triviaList)
            {
                var text = trivia.ToFullString();
                if (text.IndexOf("TODO RitsuLib analyzer", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (text.IndexOf(resourcePath, StringComparison.Ordinal) < 0)
                    continue;

                if (text.IndexOf("项目资源索引中未找到", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("project resource index", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private bool TryResolveStringExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out string? result)
        {
            return RitsuLibResourcePathFacts.TryResolveStringExpression(expression, semanticModel, cancellationToken, out result);
        }

        private void AnalyzeAttributeResourcePaths(
            string attributeName,
            AttributeSyntax attribute,
            SyntaxNodeAnalysisContext context)
        {
            if (attribute.ArgumentList == null)
                return;

            var constructor = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol as IMethodSymbol;
            for (var i = 0; i < attribute.ArgumentList.Arguments.Count; i++)
            {
                var argument = attribute.ArgumentList.Arguments[i];
                var parameter = GetAttributeParameter(constructor, argument, i);
                var name = argument.NameEquals?.Name.Identifier.ValueText ??
                           argument.NameColon?.Name.Identifier.ValueText ??
                           parameter?.Name;
                if (!IsResourceArgumentName(name))
                    continue;

                AnalyzeResourceExpression(argument.Expression, argument.GetLocation(), context);
            }
        }

        private void AnalyzeOverrideResourceProperty(
            PropertyDeclarationSyntax property,
            SyntaxNodeAnalysisContext context)
        {
            if (!IsResourceArgumentName(property.Identifier.ValueText))
                return;

            var symbol = context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken);
            if (symbol != null && !IsStringType(symbol.Type))
                return;

            var expression = property.ExpressionBody?.Expression ?? property.Initializer?.Value;
            if (expression == null)
                return;

            AnalyzeResourceExpression(expression, expression.GetLocation(), context);
        }

        private void AnalyzeObjectCreationResourcePaths(
            BaseObjectCreationExpressionSyntax creation,
            SyntaxNodeAnalysisContext context)
        {
            var type = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
            var isAssetProfile = IsAssetProfileType(type);
            var constructor = context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol as IMethodSymbol;

            if (creation.ArgumentList != null)
            {
                for (var i = 0; i < creation.ArgumentList.Arguments.Count; i++)
                {
                    var argument = creation.ArgumentList.Arguments[i];
                    var parameter = GetConstructorParameter(constructor, argument, i);
                    var name = argument.NameColon?.Name.Identifier.ValueText ?? parameter?.Name;
                    var shouldAnalyze = isAssetProfile
                        ? parameter == null || IsStringType(parameter.Type) || IsResourceArgumentName(name)
                        : IsResourceArgumentName(name);

                    if (!shouldAnalyze)
                        continue;

                    AnalyzeResourceExpression(argument.Expression, argument.GetLocation(), context);
                }
            }

            if (isAssetProfile && creation.Initializer != null)
                AnalyzeAssetProfileInitializerResourcePaths(creation.Initializer, context);
        }

        private void AnalyzeAssetProfileInitializerResourcePaths(
            InitializerExpressionSyntax initializer,
            SyntaxNodeAnalysisContext context)
        {
            foreach (var expression in initializer.Expressions)
            {
                if (expression is not AssignmentExpressionSyntax assignment)
                    continue;

                if (!ShouldAnalyzeAssetProfileInitializerAssignment(assignment, context.SemanticModel, context.CancellationToken))
                    continue;

                AnalyzeResourceExpression(assignment.Right, assignment.GetLocation(), context);
            }
        }

        private void AnalyzeResourceExpression(
            ExpressionSyntax expression,
            Location location,
            SyntaxNodeAnalysisContext context)
        {
            if (!TryResolveStringExpression(expression, context.SemanticModel, context.CancellationToken, out var value) ||
                string.IsNullOrWhiteSpace(value))
                return;

            AnalyzeResourceString(value!, location, context, IsFixableResourcePathExpression(expression));
        }

        // RITSU013: Resource path helper predicates
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

        private static bool IsAssetProfileType(ITypeSymbol? type)
        {
            var current = type as INamedTypeSymbol;
            while (current != null)
            {
                if (current.Name.EndsWith("AssetProfile", StringComparison.Ordinal))
                    return true;

                current = current.BaseType;
            }

            return false;
        }

        private static bool IsStringType(ITypeSymbol? type)
        {
            return type?.SpecialType == SpecialType.System_String;
        }

        private static bool IsFixableResourcePathExpression(ExpressionSyntax expression)
        {
            var unwrapped = Unwrap(expression);
            return unwrapped.IsKind(SyntaxKind.StringLiteralExpression) ||
                   unwrapped is InterpolatedStringExpressionSyntax;
        }

        private static bool ShouldAnalyzeAssetProfileInitializerAssignment(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var symbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
            return symbol switch
            {
                IPropertySymbol property => IsStringType(property.Type) || IsResourceArgumentName(property.Name),
                IFieldSymbol field => IsStringType(field.Type) || IsResourceArgumentName(field.Name),
                _ => true,
            };
        }

        private static IParameterSymbol? GetConstructorParameter(IMethodSymbol? constructor, ArgumentSyntax argument, int position)
        {
            if (constructor == null)
                return null;

            var name = argument.NameColon?.Name.Identifier.ValueText;
            if (!string.IsNullOrWhiteSpace(name))
                return constructor.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal));

            return position < constructor.Parameters.Length ? constructor.Parameters[position] : null;
        }

        private static IParameterSymbol? GetAttributeParameter(IMethodSymbol? constructor, AttributeArgumentSyntax argument, int position)
        {
            if (constructor == null)
                return null;

            var name = argument.NameColon?.Name.Identifier.ValueText;
            if (!string.IsNullOrWhiteSpace(name))
                return constructor.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal));

            return position < constructor.Parameters.Length ? constructor.Parameters[position] : null;
        }

        // Reporting infrastructure (used by RITSU013)
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
    }
}
