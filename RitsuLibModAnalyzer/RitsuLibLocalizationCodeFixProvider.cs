using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RitsuLibLocalizationCodeFixProvider)), Shared]
public sealed class RitsuLibLocalizationCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds { get; } =
        RitsuLibDiagnostics.FixableIds;

    public sealed override FixAllProvider? GetFixAllProvider()
    {
        return null;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var registrations = context.Diagnostics
            .Where(diagnostic => diagnostic.Id == RitsuLibDiagnostics.MissingLocalizationId)
            .SelectMany(diagnostic => ReadRequests(diagnostic)
                .Select(request => new LocalizationFixRegistration(diagnostic, request)))
            .ToImmutableArray();

        registrations = await ExpandMissingLocalizationRegistrationsAsync(
            context.Document.Project,
            registrations,
            context.CancellationToken).ConfigureAwait(false);

        var requests = registrations
            .Select(registration => registration.Request!)
            .ToImmutableArray();

        if (requests.Length > 0)
        {
            foreach (var group in registrations
                         .GroupBy(registration => registration.Request!.TargetPath, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => GetTargetLabel(group.First().Request!), StringComparer.OrdinalIgnoreCase))
            {
                var targetRequests = group
                    .Select(registration => registration.Request!)
                    .ToImmutableArray();
                var targetDiagnostics = group
                    .Select(registration => registration.Diagnostic)
                    .ToImmutableArray();
                var targetPath = group.Key;

                context.RegisterCodeFix(
                    new LocalizationJsonCodeAction(
                        RitsuLibUiText.AddMissingKeysToTargetTitle(GetTargetLabel(targetRequests)),
                        "AddMissingRitsuLibLocalizationKeys:" + targetPath,
                        cancellationToken => AddMissingKeysAsync(
                            context.Document.Project.Solution,
                            context.Document.Project.Id,
                            targetRequests,
                            cancellationToken)),
                    targetDiagnostics);
            }

            var primaryRegistrations = registrations
                .Where(registration => IsPrimaryFixSource(registration.Diagnostic))
                .ToImmutableArray();
            if (primaryRegistrations.Length > 0)
            {
                context.RegisterCodeFix(
                    new LocalizationJsonCodeAction(
                        RitsuLibUiText.FixAllMissingLocalizationTitle,
                        "FixAllMissingRitsuLibLocalizationKeysInProject",
                        cancellationToken => AddMissingKeysForProjectAsync(context.Document.Project, requests, cancellationToken),
                        isInlinable: false),
                    primaryRegistrations.Select(registration => registration.Diagnostic).ToImmutableArray());
            }
        }

        var snippetRegistrations = registrations
            .Where(registration => IsPrimaryFixSource(registration.Diagnostic))
            .ToImmutableArray();
        if (snippetRegistrations.Length > 0)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    RitsuLibUiText.InsertSnippetTitle,
                    cancellationToken => InsertSnippetForRelatedDiagnosticsAsync(
                        context.Document,
                        snippetRegistrations[0].Diagnostic,
                        requests,
                        cancellationToken),
                    "InsertMissingRitsuLibLocalizationSnippet"),
                snippetRegistrations.Select(registration => registration.Diagnostic).ToImmutableArray());
        }

        foreach (var diagnostic in context.Diagnostics.Where(diagnostic => diagnostic.Id != RitsuLibDiagnostics.MissingLocalizationId))
            RegisterContractCodeFix(context, diagnostic);
    }

    private static void RegisterContractCodeFix(CodeFixContext context, Diagnostic diagnostic)
    {
        switch (diagnostic.Id)
        {
            case RitsuLibDiagnostics.ResourcePathId:
                if (IsStubKind(diagnostic, "ResourcePath") &&
                    TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.ResourcePath, out _))
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            RitsuLibUiText.AddPrefixTitle("res://"),
                            cancellationToken => AddResourcePathPrefixAsync(context.Document, diagnostic, cancellationToken),
                            "AddRitsuLibResourcePathResPrefix"),
                        diagnostic);
                }
                else if (IsResourcePathNotFoundDiagnostic(diagnostic))
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            RitsuLibUiText.InsertTodoFixTitle,
                            cancellationToken => InsertResourcePathNotFoundTodoCommentAsync(context.Document, diagnostic, cancellationToken),
                            "InsertRitsuLibResourcePathNotFoundTodoSnippet"),
                        diagnostic);
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            RitsuLibUiText.InsertCurrentFileResourcePathTodosTitle,
                            cancellationToken => InsertCurrentDocumentResourcePathTodosAsync(context.Document, diagnostic, cancellationToken),
                            "InsertRitsuLibResourcePathNotFoundTodosInCurrentFile"),
                        diagnostic);
                }
                else
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            RitsuLibUiText.InsertTodoFixTitle,
                            cancellationToken => InsertTodoCommentAsync(context.Document, diagnostic, null, cancellationToken),
                            "InsertRitsuLibTodoSnippet"),
                        diagnostic);
                }
                return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                RitsuLibUiText.InsertTodoFixTitle,
                cancellationToken => InsertTodoCommentAsync(context.Document, diagnostic, null, cancellationToken),
                "InsertRitsuLibTodoSnippet"),
            diagnostic);
    }

    private static bool IsPrimaryFixSource(Diagnostic diagnostic)
    {
        return !diagnostic.Properties.TryGetValue(RitsuLibDiagnosticProperties.PrimaryFixSource, out var value) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ImmutableArray<LocalizationFixRegistration>> ExpandMissingLocalizationRegistrationsAsync(
        Project project,
        ImmutableArray<LocalizationFixRegistration> registrations,
        CancellationToken cancellationToken)
    {
        if (registrations.Length == 0)
            return registrations;

        var primaryRegistration = registrations.FirstOrDefault(registration => IsPrimaryFixSource(registration.Diagnostic));
        if (primaryRegistration == null)
            return ImmutableArray<LocalizationFixRegistration>.Empty;

        var diagnostics = await CollectProjectDiagnosticsAsync(project, cancellationToken).ConfigureAwait(false);
        var expanded = diagnostics
            .Where(diagnostic => diagnostic.Id == RitsuLibDiagnostics.MissingLocalizationId)
            .Where(diagnostic => IsSameSourceLocation(diagnostic, primaryRegistration.Diagnostic))
            .SelectMany(diagnostic => ReadRequests(diagnostic)
                .Select(request => new LocalizationFixRegistration(diagnostic, request)))
            .GroupBy(registration => GetRegistrationKey(registration), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToImmutableArray();

        return expanded.Length == 0 ? registrations : expanded;
    }

    private static string GetRegistrationKey(LocalizationFixRegistration registration)
    {
        var request = registration.Request;
        return request == null
            ? string.Empty
            : request.TargetPath + "\u001f" +
              string.Join(RitsuLibDiagnosticProperties.ListSeparator, request.Entries.Select(entry => entry.Key));
    }

    private static async Task<Document> AddResourcePathPrefixAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        if (!TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.ResourcePath, out var resourcePath))
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        var expression = FindResourcePathExpression(root, diagnostic, semanticModel, resourcePath, cancellationToken);
        if (expression == null)
            return document;

        TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.SuggestedResourcePath, out var suggestedPath);
        var resourceIndex = await RitsuLibResourcePathIndex.CreateAsync(document.Project, cancellationToken).ConfigureAwait(false);
        var targetPath = !string.IsNullOrWhiteSpace(suggestedPath)
            ? suggestedPath!
            : resourceIndex.TryFindExistingResourcePath(resourcePath) ?? resourceIndex.GetFallbackResourcePath(resourcePath);

        var symbols = await RitsuLibResourcePathFacts.FindResourceRootSymbolsAsync(document, cancellationToken).ConfigureAwait(false);
        var replacementText = CreateResourcePathExpressionText(expression, resourcePath, targetPath, symbols);
        if (replacementText == null)
            return document;

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        return document.WithText(sourceText.Replace(expression.Span, replacementText));
    }

    private static ExpressionSyntax? FindResourcePathExpression(
        SyntaxNode root,
        Diagnostic diagnostic,
        SemanticModel semanticModel,
        string resourcePath,
        CancellationToken cancellationToken)
    {
        var node = FindNodeForDiagnostic(root, diagnostic);
        var expression = FindResourcePathExpressionInNode(node, semanticModel, resourcePath, cancellationToken);
        if (expression != null)
            return expression;

        if (!diagnostic.Location.IsInSource)
            return null;

        var token = root.FindToken(Math.Min(diagnostic.Location.SourceSpan.Start, Math.Max(0, root.FullSpan.End - 1)));
        return token.Parent == null
            ? null
            : FindResourcePathExpressionInNode(token.Parent, semanticModel, resourcePath, cancellationToken);
    }

    private static ExpressionSyntax? FindResourcePathExpressionInNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        string resourcePath,
        CancellationToken cancellationToken)
    {
        return node.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Where(IsEditableResourcePathExpression)
            .OrderBy(expression => expression.Span.Length)
            .FirstOrDefault(expression =>
                RitsuLibResourcePathFacts.TryResolveStringExpression(expression, semanticModel, cancellationToken, out var value) &&
                string.Equals(value, resourcePath, StringComparison.Ordinal));
    }

    private static bool IsEditableResourcePathExpression(ExpressionSyntax expression)
    {
        return expression is InterpolatedStringExpressionSyntax ||
               expression is LiteralExpressionSyntax literal &&
               literal.IsKind(SyntaxKind.StringLiteralExpression);
    }

    private static string? CreateResourcePathExpressionText(
        ExpressionSyntax expression,
        string currentResourcePath,
        string targetResourcePath,
        ImmutableArray<ResourceRootSymbol> symbols)
    {
        if (!TryGetExpressionTextParts(expression, out var prefix, out var content, out var suffix))
            return null;

        var isInterpolated = expression is InterpolatedStringExpressionSyntax;
        var currentIsResourcePath = RitsuLibResourcePathFacts.IsResourcePath(currentResourcePath);
        var currentRelative = RitsuLibResourcePathFacts.NormalizeResourcePath(currentResourcePath);
        if (string.IsNullOrWhiteSpace(currentRelative))
            return null;

        if (RitsuLibResourcePathFacts.TrySelectRootSymbol(targetResourcePath, symbols, out var symbol) &&
            TryCreateSymbolRootedContent(content, currentIsResourcePath, currentRelative, targetResourcePath, symbol, isInterpolated, out var symbolContent))
        {
            return isInterpolated
                ? prefix + symbolContent + suffix
                : "$\"" + symbolContent + "\"";
        }

        if (isInterpolated &&
            TryGetResourcePrefixToInsert(targetResourcePath, currentRelative, out var prefixToInsert))
        {
            var newContent = currentIsResourcePath && content.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
                ? prefixToInsert + content.Substring("res://".Length)
                : prefixToInsert + content;
            return prefix + newContent + suffix;
        }

        return SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(targetResourcePath))
            .ToString();
    }

    private static bool TryCreateSymbolRootedContent(
        string content,
        bool currentIsResourcePath,
        string currentRelative,
        string targetResourcePath,
        ResourceRootSymbol symbol,
        bool isInterpolated,
        out string newContent)
    {
        newContent = string.Empty;
        var symbolRelative = RitsuLibResourcePathFacts.NormalizeResourcePath(symbol.Value);
        var targetRelative = RitsuLibResourcePathFacts.NormalizeResourcePath(targetResourcePath);
        if (!targetRelative.Equals(symbolRelative, StringComparison.OrdinalIgnoreCase) &&
            !targetRelative.StartsWith(symbolRelative + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (isInterpolated)
        {
            if (currentIsResourcePath && content.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                newContent = "{" + symbol.Expression + "}/" + content.Substring("res://".Length);
                return true;
            }

            if (targetRelative.EndsWith(currentRelative, StringComparison.OrdinalIgnoreCase))
            {
                newContent = "{" + symbol.Expression + "}/" + content.TrimStart('/');
                return true;
            }
        }
        else
        {
            var rest = targetRelative.Length == symbolRelative.Length
                ? string.Empty
                : targetRelative.Substring(symbolRelative.Length);
            newContent = "{" + symbol.Expression + "}" + rest;
            return true;
        }

        return false;
    }

    private static bool TryGetResourcePrefixToInsert(
        string targetResourcePath,
        string currentRelative,
        out string prefix)
    {
        prefix = string.Empty;
        var targetRelative = RitsuLibResourcePathFacts.NormalizeResourcePath(targetResourcePath);
        if (!targetRelative.EndsWith(currentRelative, StringComparison.OrdinalIgnoreCase))
            return false;

        var targetPrefixRelativeLength = targetRelative.Length - currentRelative.Length;
        if (targetPrefixRelativeLength < 0)
            return false;

        prefix = "res://" + targetRelative.Substring(0, targetPrefixRelativeLength);
        return true;
    }

    private static bool TryGetExpressionTextParts(
        ExpressionSyntax expression,
        out string prefix,
        out string content,
        out string suffix)
    {
        var text = expression.ToString();
        var firstQuote = text.IndexOf('"');
        var lastQuote = text.LastIndexOf('"');
        if (firstQuote < 0 || lastQuote <= firstQuote)
        {
            prefix = string.Empty;
            content = string.Empty;
            suffix = string.Empty;
            return false;
        }

        prefix = text.Substring(0, firstQuote + 1);
        content = text.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
        suffix = text.Substring(lastQuote);
        return true;
    }

    private static async Task<Document> InsertTodoCommentAsync(
        Document document,
        Diagnostic diagnostic,
        string? detail,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var node = FindNodeForDiagnostic(root, diagnostic);
        while (node.Parent != null &&
               node.Kind() is not SyntaxKind.ClassDeclaration and
                   not SyntaxKind.StructDeclaration and
                   not SyntaxKind.RecordDeclaration and
                   not SyntaxKind.MethodDeclaration and
                   not SyntaxKind.InvocationExpression and
                   not SyntaxKind.Attribute)
        {
            node = node.Parent;
        }

        var comment = SyntaxFactory.Comment(BuildTodoComment(diagnostic, detail));
        var newNode = node.WithLeadingTrivia(node.GetLeadingTrivia().Add(comment).Add(SyntaxFactory.ElasticCarriageReturnLineFeed));
        return document.WithSyntaxRoot(root.ReplaceNode(node, newNode));
    }

    private static async Task<Document> InsertResourcePathNotFoundTodoCommentAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null)
            return document;

        if (!TryFindResourcePathTodoTarget(root, diagnostic, semanticModel, cancellationToken, out var target))
            return await InsertTodoCommentAsync(document, diagnostic, null, cancellationToken).ConfigureAwait(false);

        return document.WithSyntaxRoot(root.ReplaceNode(target, AddTodoComment(target, diagnostic, null)));
    }

    private static async Task<Document> InsertCurrentDocumentResourcePathTodosAsync(
        Document document,
        Diagnostic triggerDiagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null)
            return document;

        var diagnostics = await CollectProjectDiagnosticsAsync(document.Project, cancellationToken).ConfigureAwait(false);
        var insertions = diagnostics
            .Where(IsResourcePathNotFoundDiagnostic)
            .Where(diagnostic => IsDiagnosticInDocument(diagnostic, document))
            .Concat(IsResourcePathNotFoundDiagnostic(triggerDiagnostic) && IsDiagnosticInDocument(triggerDiagnostic, document)
                ? new[] { triggerDiagnostic }
                : Array.Empty<Diagnostic>())
            .Distinct(ResourcePathTodoDiagnosticComparer.Instance)
            .Select(diagnostic => TryCreateResourcePathTodoInsertion(root, semanticModel, diagnostic, cancellationToken, out var insertion)
                ? insertion
                : null)
            .Where(insertion => insertion != null)
            .Cast<ResourcePathTodoInsertion>()
            .Where(insertion => !ResourcePathNotFoundTodoTextMatches(insertion.Target.GetLeadingTrivia(), insertion.ResourcePath) &&
                                !ResourcePathNotFoundTodoTextMatches(insertion.Target.GetTrailingTrivia(), insertion.ResourcePath))
            .ToArray();

        if (insertions.Length == 0)
            return document;

        var insertionsByTarget = insertions
            .GroupBy(insertion => insertion.Target.Span)
            .ToDictionary(group => group.Key, group => group.OrderBy(insertion => insertion.Diagnostic.Location.SourceSpan.Start).ToArray());

        var newRoot = root.ReplaceNodes(
            insertionsByTarget.Values.Select(group => group[0].Target),
            (original, _) => AddTodoComments(original, insertionsByTarget[original.Span].Select(insertion => insertion.Diagnostic)));
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool TryCreateResourcePathTodoInsertion(
        SyntaxNode root,
        SemanticModel semanticModel,
        Diagnostic diagnostic,
        CancellationToken cancellationToken,
        out ResourcePathTodoInsertion? insertion)
    {
        insertion = null;
        if (!TryGetResourcePathNotFoundDiagnosticPath(diagnostic, out var resourcePath) ||
            !TryFindResourcePathTodoTarget(root, diagnostic, semanticModel, cancellationToken, out var target))
            return false;

        insertion = new ResourcePathTodoInsertion(diagnostic, target, resourcePath);
        return true;
    }

    private static bool TryFindResourcePathTodoTarget(
        SyntaxNode root,
        Diagnostic diagnostic,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SyntaxNode target)
    {
        target = root;
        if (!TryGetResourcePathNotFoundDiagnosticPath(diagnostic, out var resourcePath) ||
            !diagnostic.Location.IsInSource)
            return false;

        var node = FindNodeForDiagnostic(root, diagnostic);
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

    private static SyntaxNode AddTodoComment(SyntaxNode node, Diagnostic diagnostic, string? detail)
    {
        var comment = SyntaxFactory.Comment(BuildTodoComment(diagnostic, detail));
        return node.WithLeadingTrivia(node.GetLeadingTrivia().Add(comment).Add(SyntaxFactory.ElasticCarriageReturnLineFeed));
    }

    private static SyntaxNode AddTodoComments(SyntaxNode node, IEnumerable<Diagnostic> diagnostics)
    {
        var trivia = node.GetLeadingTrivia();
        foreach (var diagnostic in diagnostics)
        {
            trivia = trivia
                .Add(SyntaxFactory.Comment(BuildTodoComment(diagnostic, null)))
                .Add(SyntaxFactory.ElasticCarriageReturnLineFeed);
        }

        return node.WithLeadingTrivia(trivia);
    }

    private static string BuildTodoComment(Diagnostic diagnostic, string? detail)
    {
        var message = SanitizeCommentText(diagnostic.GetMessage());
        var extra = string.IsNullOrWhiteSpace(detail) ? string.Empty : Environment.NewLine + SanitizeCommentText(detail!);
        return $"/*{Environment.NewLine}TODO RitsuLib analyzer: {message}{extra}{Environment.NewLine}*/";
    }

    private static SyntaxNode FindNodeForDiagnostic(SyntaxNode root, Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource)
            return root.ChildNodes().FirstOrDefault() ?? root;

        var span = GetSourceSpan(diagnostic);
        if (span.Length == 0 && span.Start < root.FullSpan.End)
            return root.FindToken(span.Start).Parent ?? root;

        return root.FindNode(span, getInnermostNodeForTie: true);
    }

    private static bool IsStubKind(Diagnostic diagnostic, string stubKind)
    {
        return TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.StubKind, out var actual) &&
               string.Equals(actual, stubKind, StringComparison.Ordinal);
    }

    private static bool TryGetProperty(Diagnostic diagnostic, string name, out string value)
    {
        if (diagnostic.Properties.TryGetValue(name, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsResourcePathNotFoundDiagnostic(Diagnostic diagnostic)
    {
        return TryGetResourcePathNotFoundDiagnosticPath(diagnostic, out _);
    }

    private static bool TryGetResourcePathNotFoundDiagnosticPath(Diagnostic diagnostic, out string resourcePath)
    {
        if (diagnostic.Id == RitsuLibDiagnostics.ResourcePathId &&
            IsStubKind(diagnostic, "ResourcePathNotFound") &&
            TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.ResourcePath, out resourcePath))
        {
            return true;
        }

        resourcePath = string.Empty;
        return false;
    }

    private static bool IsDiagnosticInDocument(Diagnostic diagnostic, Document document)
    {
        if (!diagnostic.Location.IsInSource)
            return false;

        var diagnosticPath = diagnostic.Location.SourceTree?.FilePath;
        return !string.IsNullOrWhiteSpace(diagnosticPath) &&
               !string.IsNullOrWhiteSpace(document.FilePath) &&
               string.Equals(diagnosticPath, document.FilePath, StringComparison.OrdinalIgnoreCase);
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

    private static TextSpan GetSourceSpan(Diagnostic diagnostic)
    {
        return diagnostic.Location.IsInSource ? diagnostic.Location.SourceSpan : default;
    }

    private static string SanitizeCommentText(string value)
    {
        return value.Replace("*/", "* /");
    }

    private static LocalizationFixRequest? ReadRequest(Diagnostic diagnostic)
    {
        var properties = diagnostic.Properties;
        if (!properties.TryGetValue(RitsuLibDiagnosticProperties.TargetPath, out var targetPath) ||
            string.IsNullOrWhiteSpace(targetPath))
            return null;

        if (!properties.TryGetValue(RitsuLibDiagnosticProperties.Keys, out var keysText) ||
            string.IsNullOrWhiteSpace(keysText))
            return null;

        properties.TryGetValue(RitsuLibDiagnosticProperties.Values, out var valuesText);
        properties.TryGetValue(RitsuLibDiagnosticProperties.Language, out var language);
        properties.TryGetValue(RitsuLibDiagnosticProperties.Table, out var table);
        properties.TryGetValue(RitsuLibDiagnosticProperties.IsI18N, out var isI18NText);

        var keys = SplitList(keysText);
        if (keys.Length == 0)
            return null;

        var values = SplitList(valuesText);
        if (values.Length < keys.Length)
            values = values.Concat(Enumerable.Repeat(string.Empty, keys.Length - values.Length)).ToArray();

        return new LocalizationFixRequest(
            targetPath!,
            language ?? string.Empty,
            table ?? string.Empty,
            string.Equals(isI18NText, "true", StringComparison.OrdinalIgnoreCase),
            keys.Zip(values, (key, value) => new KeyValuePair<string, string>(key, value)).ToImmutableArray());
    }

    private static ImmutableArray<LocalizationFixRequest> ReadRequests(Diagnostic diagnostic)
    {
        var properties = diagnostic.Properties;
        if (!properties.TryGetValue(RitsuLibDiagnosticProperties.TargetPaths, out var targetPathsText) ||
            string.IsNullOrWhiteSpace(targetPathsText))
        {
            var request = ReadRequest(diagnostic);
            return request == null ? ImmutableArray<LocalizationFixRequest>.Empty : ImmutableArray.Create(request);
        }

        var targetPaths = SplitRecords(targetPathsText);
        var languages = SplitRecords(GetProperty(properties, RitsuLibDiagnosticProperties.Languages));
        var tables = SplitRecords(GetProperty(properties, RitsuLibDiagnosticProperties.Tables));
        var isI18NValues = SplitRecords(GetProperty(properties, RitsuLibDiagnosticProperties.IsI18NValues));
        var keyGroups = SplitRecords(GetProperty(properties, RitsuLibDiagnosticProperties.KeyGroups));
        var valueGroups = SplitRecords(GetProperty(properties, RitsuLibDiagnosticProperties.ValueGroups));

        var builder = ImmutableArray.CreateBuilder<LocalizationFixRequest>();
        for (var i = 0; i < targetPaths.Length; i++)
        {
            var targetPath = targetPaths[i];
            if (string.IsNullOrWhiteSpace(targetPath))
                continue;

            var keys = SplitList(GetAt(keyGroups, i));
            if (keys.Length == 0)
                continue;

            var values = SplitList(GetAt(valueGroups, i));
            if (values.Length < keys.Length)
                values = values.Concat(Enumerable.Repeat(string.Empty, keys.Length - values.Length)).ToArray();

            builder.Add(new LocalizationFixRequest(
                targetPath,
                GetAt(languages, i),
                GetAt(tables, i),
                string.Equals(GetAt(isI18NValues, i), "true", StringComparison.OrdinalIgnoreCase),
                keys.Zip(values, (key, value) => new KeyValuePair<string, string>(key, value)).ToImmutableArray()));
        }

        return builder.ToImmutable();
    }

    private static string? GetProperty(
        ImmutableDictionary<string, string?> properties,
        string key)
    {
        return properties.TryGetValue(key, out var value) ? value : null;
    }

    private static string GetAt(string[] values, int index)
    {
        return index >= 0 && index < values.Length ? values[index] : string.Empty;
    }

    private static string[] SplitRecords(string? value)
    {
        return value == null || value.Length == 0
            ? Array.Empty<string>()
            : value.Split(new[] { RitsuLibDiagnosticProperties.RecordSeparator }, StringSplitOptions.None);
    }

    private static string[] SplitList(string? value)
    {
        return value == null || value.Length == 0
            ? Array.Empty<string>()
            : value.Split(new[] { RitsuLibDiagnosticProperties.ListSeparator }, StringSplitOptions.None);
    }

    private static async Task<LocalizationJsonChange> AddMissingKeysForProjectAsync(
        Project project,
        ImmutableArray<LocalizationFixRequest> fallbackRequests,
        CancellationToken cancellationToken)
    {
        var projectRequests = await CollectProjectRequestsAsync(project, cancellationToken).ConfigureAwait(false);
        var requests = projectRequests
            .Concat(fallbackRequests)
            .ToImmutableArray();

        return await AddMissingKeysAsync(project.Solution, project.Id, requests, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ImmutableArray<LocalizationFixRequest>> CollectProjectRequestsAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var diagnostics = await CollectProjectDiagnosticsAsync(project, cancellationToken).ConfigureAwait(false);
        return diagnostics
            .Where(diagnostic => diagnostic.Id == RitsuLibDiagnostics.MissingLocalizationId)
            .SelectMany(diagnostic => ReadRequests(diagnostic).AsEnumerable())
            .Distinct()
            .ToImmutableArray();
    }

    private static async Task<ImmutableArray<Diagnostic>> CollectProjectDiagnosticsAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation == null)
            return ImmutableArray<Diagnostic>.Empty;

        var additionalTexts = project.AdditionalDocuments
            .Select(document => new AdditionalDocumentText(document))
            .Cast<AdditionalText>()
            .ToImmutableArray();

        var analyzer = new RitsuLibModAnalyzer();
        var options = new AnalyzerOptions(
            additionalTexts,
            CreateAnalyzerConfigOptionsProvider(project));
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
            new CompilationWithAnalyzersOptions(
                options,
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AnalyzerConfigOptionsProvider CreateAnalyzerConfigOptionsProvider(Project project)
    {
        var projectDirectory = project.FilePath == null ? null : Path.GetDirectoryName(project.FilePath);
        return new ProjectAnalyzerConfigOptionsProvider(projectDirectory);
    }

    private static async Task<LocalizationJsonChange> AddMissingKeysAsync(
        Solution solution,
        ProjectId projectId,
        ImmutableArray<LocalizationFixRequest> requests,
        CancellationToken cancellationToken)
    {
        var project = solution.GetProject(projectId);
        if (project == null)
            return LocalizationJsonChange.Empty(solution);

        var fileWrites = ImmutableArray.CreateBuilder<LocalizationFileWrite>();

        foreach (var group in requests.GroupBy(request => request.TargetPath, StringComparer.OrdinalIgnoreCase))
        {
            var targetPath = group.Key;
            var fullPath = ResolveFullPath(targetPath, project);
            var document = FindAdditionalDocument(project, targetPath);
            var existingText = document == null
                ? ReadLocalizationFile(fullPath) ?? string.Empty
                : (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();

            if (!CanPatchTopLevelObject(existingText))
                continue;

            var existingKeys = RitsuLibAdditionalFileIndex.JsonTopLevelKeyScanner.ReadKeys(existingText);
            var entries = group
                .SelectMany(request => request.Entries)
                .GroupBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => entry.First())
                .Where(entry => !existingKeys.Contains(entry.Key))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToImmutableArray();

            if (entries.Length == 0)
            {
                if (document == null && !string.IsNullOrEmpty(existingText))
                {
                    var existingDocumentId = DocumentId.CreateNewId(projectId, Path.GetFileName(targetPath));
                    solution = solution.AddAdditionalDocument(
                        existingDocumentId,
                        Path.GetFileName(targetPath),
                        SourceText.From(existingText, Encoding.UTF8),
                        folders: ImmutableArray<string>.Empty,
                        filePath: targetPath);

                    project = solution.GetProject(projectId);
                    if (project == null)
                        return new LocalizationJsonChange(solution, fileWrites.ToImmutable());
                }

                continue;
            }

            var updatedContent = AddEntriesToJsonObject(existingText, entries);
            var updatedText = SourceText.From(updatedContent, Encoding.UTF8);
            if (ShouldWritePhysicalLocalizationFile(fullPath))
                fileWrites.Add(new LocalizationFileWrite(fullPath, updatedContent));

            if (document != null)
            {
                solution = solution.WithAdditionalDocumentText(document.Id, updatedText);
            }
            else
            {
                var additionalDocumentId = DocumentId.CreateNewId(projectId, Path.GetFileName(targetPath));
                solution = solution.AddAdditionalDocument(
                    additionalDocumentId,
                    Path.GetFileName(targetPath),
                    updatedText,
                    folders: ImmutableArray<string>.Empty,
                    filePath: targetPath);
            }

            project = solution.GetProject(projectId);
            if (project == null)
                return new LocalizationJsonChange(solution, fileWrites.ToImmutable());
        }

        return new LocalizationJsonChange(solution, fileWrites.ToImmutable());
    }

    private static async Task<Document> InsertSnippetAsync(
        Document document,
        ImmutableArray<LocalizationFixRequest> requests,
        TextSpan diagnosticSpan,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var source = sourceText.ToString();
        if (ContainsEquivalentLocalizationSnippet(source, requests))
            return document;

        var target = FindSnippetInsertionTarget(root, diagnosticSpan);
        var insertionPosition = GetLineStartPosition(sourceText, target);
        var newLine = DetectNewLine(source);
        var indent = GetLineIndent(sourceText, target.SpanStart);
        var snippet = IndentMultiline(BuildSnippetComment(requests, newLine), indent, newLine);
        var changedText = sourceText.WithChanges(new TextChange(new TextSpan(insertionPosition, 0), snippet));
        return document.WithText(changedText);
    }

    private static async Task<Document> InsertSnippetForRelatedDiagnosticsAsync(
        Document document,
        Diagnostic triggerDiagnostic,
        ImmutableArray<LocalizationFixRequest> fallbackRequests,
        CancellationToken cancellationToken)
    {
        var diagnostics = await CollectProjectDiagnosticsAsync(document.Project, cancellationToken).ConfigureAwait(false);
        var requests = diagnostics
            .Where(diagnostic => diagnostic.Id == RitsuLibDiagnostics.MissingLocalizationId)
            .Where(diagnostic => IsSameSourceLocation(diagnostic, triggerDiagnostic))
            .SelectMany(diagnostic => ReadRequests(diagnostic).AsEnumerable())
            .ToImmutableArray();

        if (requests.Length == 0)
            requests = fallbackRequests;

        return await InsertSnippetAsync(document, requests, GetSourceSpan(triggerDiagnostic), cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSameSourceLocation(Diagnostic left, Diagnostic right)
    {
        if (left.Location.IsInSource != right.Location.IsInSource)
            return false;

        if (!left.Location.SourceSpan.Equals(right.Location.SourceSpan))
            return false;

        return string.Equals(
            left.Location.SourceTree?.FilePath,
            right.Location.SourceTree?.FilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSnippetComment(ImmutableArray<LocalizationFixRequest> requests)
    {
        return BuildSnippetComment(requests, Environment.NewLine);
    }

    private static string BuildSnippetComment(ImmutableArray<LocalizationFixRequest> requests, string newLine)
    {
        StringBuilder builder = new();
        builder.Append("/*").Append(newLine);
        builder.Append(RitsuLibUiText.MissingLocalizationSnippetHeader).Append(newLine);

        foreach (var group in requests.GroupBy(request => request.TargetPath, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(group.Key).Append(newLine);
            builder.Append('{').Append(newLine);

            var entries = group
                .SelectMany(request => request.Entries)
                .GroupBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => entry.First())
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToArray();

            for (var i = 0; i < entries.Length; i++)
            {
                var comma = i == entries.Length - 1 ? string.Empty : ",";
                builder.Append("  \"")
                    .Append(EscapeJson(entries[i].Key))
                    .Append("\": \"")
                    .Append(EscapeJson(entries[i].Value))
                    .Append('"')
                    .Append(comma)
                    .Append(newLine);
            }

            builder.Append('}').Append(newLine);
        }

        builder.Append("*/").Append(newLine);
        return builder.ToString();
    }

    private static SyntaxNode FindSnippetInsertionTarget(SyntaxNode root, TextSpan diagnosticSpan)
    {
        var position = Math.Min(diagnosticSpan.Start, Math.Max(0, root.FullSpan.End - 1));
        var token = root.FindToken(position);
        var node = token.Parent;
        if (node == null)
            return root;

        var attributeList = node.AncestorsAndSelf().OfType<AttributeListSyntax>().FirstOrDefault();
        if (attributeList != null)
        {
            var parentMember = attributeList.Parent as MemberDeclarationSyntax;
            return parentMember ?? (SyntaxNode)attributeList;
        }

        var statement = node.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (statement != null)
            return statement;

        var member = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        if (member != null)
            return member;

        return node.AncestorsAndSelf().LastOrDefault(candidate => candidate.Parent == root) ?? root;
    }

    private static int GetLineStartPosition(SourceText sourceText, SyntaxNode target)
    {
        if (sourceText.Length == 0)
            return 0;

        var position = Math.Max(0, Math.Min(target.FullSpan.Start, sourceText.Length - 1));
        return sourceText.Lines.GetLineFromPosition(position).Start;
    }

    private static string GetLineIndent(SourceText sourceText, int position)
    {
        if (sourceText.Length == 0)
            return string.Empty;

        var line = sourceText.Lines.GetLineFromPosition(Math.Max(0, Math.Min(position, sourceText.Length - 1))).ToString();
        var index = 0;
        while (index < line.Length && line[index] is ' ' or '\t')
            index++;

        return line.Substring(0, index);
    }

    private static string IndentMultiline(string text, string indent, string newLine)
    {
        if (indent.Length == 0 || text.Length == 0)
            return text;

        var lines = text.Split(new[] { newLine }, StringSplitOptions.None);
        StringBuilder builder = new();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i == lines.Length - 1 && lines[i].Length == 0)
                break;

            builder.Append(indent).Append(lines[i]).Append(newLine);
        }

        return builder.ToString();
    }

    private static bool ContainsEquivalentLocalizationSnippet(
        string source,
        ImmutableArray<LocalizationFixRequest> requests)
    {
        if (source.IndexOf("Missing RitsuLib localization:", StringComparison.OrdinalIgnoreCase) < 0 &&
            source.IndexOf("缺失的 RitsuLib 本地化:", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        foreach (var group in requests.GroupBy(request => request.TargetPath, StringComparer.OrdinalIgnoreCase))
        {
            if (source.IndexOf(group.Key, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            foreach (var key in group.SelectMany(request => request.Entries.Select(entry => entry.Key)).Distinct(StringComparer.Ordinal))
            {
                if (source.IndexOf(key, StringComparison.Ordinal) < 0)
                    return false;
            }
        }

        return true;
    }

    private static TextDocument? FindAdditionalDocument(Project project, string targetPath)
    {
        return project.AdditionalDocuments.FirstOrDefault(document =>
            string.Equals(document.FilePath, targetPath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanPatchTopLevelObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var bomPrefix = GetBomPrefix(text);
        if (bomPrefix.Length > 0 && string.IsNullOrWhiteSpace(text.Substring(bomPrefix.Length)))
            return true;

        return TryReadTopLevelObject(text, out _);
    }

    private static string AddEntriesToJsonObject(
        string existingText,
        ImmutableArray<KeyValuePair<string, string>> entries)
    {
        if (entries.Length == 0)
            return existingText;

        var newLine = DetectNewLine(existingText);
        var bomPrefix = GetBomPrefix(existingText);
        var textWithoutBom = bomPrefix.Length == 0 ? existingText : existingText.Substring(bomPrefix.Length);
        if (string.IsNullOrWhiteSpace(textWithoutBom))
            return bomPrefix + BuildNewJsonObject(entries, newLine);

        if (!TryReadTopLevelObject(existingText, out var objectInfo))
            return existingText;

        var before = existingText.Substring(0, objectInfo.ClosingBraceIndex).TrimEnd();
        var after = existingText.Substring(objectInfo.ClosingBraceIndex);
        StringBuilder builder = new();
        builder.Append(before);
        if (objectInfo.HasEntries && !objectInfo.HasTrailingComma)
            builder.Append(',');

        builder.Append(newLine);
        for (var i = 0; i < entries.Length; i++)
        {
            var comma = i == entries.Length - 1 ? string.Empty : ",";
            builder.Append("  \"")
                .Append(EscapeJson(entries[i].Key))
                .Append("\": \"")
                .Append(EscapeJson(entries[i].Value))
                .Append('"')
                .Append(comma)
                .Append(newLine);
        }

        builder.Append(after.TrimStart());
        if (!EndsWithNewLine(builder))
            builder.Append(newLine);

        return builder.ToString();
    }

    private static string BuildNewJsonObject(ImmutableArray<KeyValuePair<string, string>> entries)
    {
        return BuildNewJsonObject(entries, Environment.NewLine);
    }

    private static string BuildNewJsonObject(ImmutableArray<KeyValuePair<string, string>> entries, string newLine)
    {
        StringBuilder builder = new();
        builder.Append('{').Append(newLine);
        for (var i = 0; i < entries.Length; i++)
        {
            var comma = i == entries.Length - 1 ? string.Empty : ",";
            builder.Append("  \"")
                .Append(EscapeJson(entries[i].Key))
                .Append("\": \"")
                .Append(EscapeJson(entries[i].Value))
                .Append('"')
                .Append(comma)
                .Append(newLine);
        }

        builder.Append('}').Append(newLine);
        return builder.ToString();
    }

    private static string DetectNewLine(string text)
    {
        return text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
    }

    private static string GetBomPrefix(string text)
    {
        return text.Length > 0 && text[0] == '\uFEFF' ? "\uFEFF" : string.Empty;
    }

    private static bool EndsWithNewLine(StringBuilder builder)
    {
        return builder.Length > 0 && builder[builder.Length - 1] is '\n' or '\r';
    }

    private static bool TryReadTopLevelObject(string text, out TopLevelObjectInfo objectInfo)
    {
        objectInfo = default;
        var index = 0;
        SkipWhiteSpaceAndBom(text, ref index);
        if (index >= text.Length || text[index] != '{')
            return false;

        var openBraceIndex = index;
        index++;
        var hasEntries = false;
        var hasTrailingComma = false;

        SkipWhiteSpace(text, ref index);
        if (index < text.Length && text[index] == '}')
            return TryFinishTopLevelObject(text, index, hasEntries, hasTrailingComma, out objectInfo);

        while (index < text.Length)
        {
            SkipWhiteSpace(text, ref index);
            if (index >= text.Length)
                return false;

            if (text[index] == '}')
            {
                if (!hasEntries)
                    return false;

                return TryFinishTopLevelObject(text, index, hasEntries, hasTrailingComma, out objectInfo);
            }

            if (!TrySkipJsonString(text, ref index))
                return false;

            SkipWhiteSpace(text, ref index);
            if (index >= text.Length || text[index] != ':')
                return false;

            index++;
            if (!TrySkipJsonValue(text, ref index, allowTrailingComma: false))
                return false;

            hasEntries = true;
            SkipWhiteSpace(text, ref index);

            if (index < text.Length && text[index] == ',')
            {
                index++;
                hasTrailingComma = true;
                continue;
            }

            hasTrailingComma = false;
            if (index >= text.Length || text[index] != '}')
                return false;
        }

        return false;

        bool TryFinishTopLevelObject(
            string source,
            int closingBraceIndex,
            bool entries,
            bool trailingComma,
            out TopLevelObjectInfo info)
        {
            var trailingIndex = closingBraceIndex + 1;
            SkipWhiteSpace(source, ref trailingIndex);
            if (trailingIndex != source.Length)
            {
                info = default;
                return false;
            }

            info = new(openBraceIndex, closingBraceIndex, entries, trailingComma);
            return true;
        }
    }

    private static bool TrySkipJsonValue(string text, ref int index, bool allowTrailingComma)
    {
        SkipWhiteSpace(text, ref index);
        if (index >= text.Length)
            return false;

        return text[index] switch
        {
            '"' => TrySkipJsonString(text, ref index),
            '{' => TrySkipJsonObject(text, ref index, allowTrailingComma),
            '[' => TrySkipJsonArray(text, ref index, allowTrailingComma),
            't' => TrySkipKeyword(text, ref index, "true"),
            'f' => TrySkipKeyword(text, ref index, "false"),
            'n' => TrySkipKeyword(text, ref index, "null"),
            '-' or >= '0' and <= '9' => TrySkipJsonNumber(text, ref index),
            _ => false,
        };
    }

    private static bool TrySkipJsonObject(string text, ref int index, bool allowTrailingComma)
    {
        if (index >= text.Length || text[index] != '{')
            return false;

        index++;
        var hasEntries = false;
        var justReadComma = false;

        while (index < text.Length)
        {
            SkipWhiteSpace(text, ref index);
            if (index >= text.Length)
                return false;

            if (text[index] == '}')
            {
                if (justReadComma && (!allowTrailingComma || !hasEntries))
                    return false;

                index++;
                return true;
            }

            if (!TrySkipJsonString(text, ref index))
                return false;

            SkipWhiteSpace(text, ref index);
            if (index >= text.Length || text[index] != ':')
                return false;

            index++;
            if (!TrySkipJsonValue(text, ref index, allowTrailingComma: false))
                return false;

            hasEntries = true;
            justReadComma = false;
            SkipWhiteSpace(text, ref index);

            if (index < text.Length && text[index] == ',')
            {
                index++;
                justReadComma = true;
                continue;
            }

            if (index >= text.Length || text[index] != '}')
                return false;
        }

        return false;
    }

    private static bool TrySkipJsonArray(string text, ref int index, bool allowTrailingComma)
    {
        if (index >= text.Length || text[index] != '[')
            return false;

        index++;
        var hasEntries = false;
        var justReadComma = false;

        while (index < text.Length)
        {
            SkipWhiteSpace(text, ref index);
            if (index >= text.Length)
                return false;

            if (text[index] == ']')
            {
                if (justReadComma && (!allowTrailingComma || !hasEntries))
                    return false;

                index++;
                return true;
            }

            if (!TrySkipJsonValue(text, ref index, allowTrailingComma: false))
                return false;

            hasEntries = true;
            justReadComma = false;
            SkipWhiteSpace(text, ref index);

            if (index < text.Length && text[index] == ',')
            {
                index++;
                justReadComma = true;
                continue;
            }

            if (index >= text.Length || text[index] != ']')
                return false;
        }

        return false;
    }

    private static bool TrySkipJsonString(string text, ref int index)
    {
        if (index >= text.Length || text[index] != '"')
            return false;

        index++;
        while (index < text.Length)
        {
            var ch = text[index++];
            if (ch == '"')
                return true;

            if (ch < ' ')
                return false;

            if (ch != '\\')
                continue;

            if (index >= text.Length)
                return false;

            var escaped = text[index++];
            if (escaped is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't')
                continue;

            if (escaped != 'u' || index + 4 > text.Length)
                return false;

            for (var i = 0; i < 4; i++)
            {
                if (!IsHexDigit(text[index + i]))
                    return false;
            }

            index += 4;
        }

        return false;
    }

    private static bool TrySkipKeyword(string text, ref int index, string keyword)
    {
        if (index + keyword.Length > text.Length ||
            !string.Equals(text.Substring(index, keyword.Length), keyword, StringComparison.Ordinal))
        {
            return false;
        }

        var next = index + keyword.Length;
        if (next < text.Length && !IsJsonDelimiter(text[next]))
            return false;

        index = next;
        return true;
    }

    private static bool TrySkipJsonNumber(string text, ref int index)
    {
        var start = index;
        if (text[index] == '-')
            index++;

        if (index >= text.Length)
            return false;

        if (text[index] == '0')
        {
            index++;
        }
        else if (text[index] is >= '1' and <= '9')
        {
            do
            {
                index++;
            } while (index < text.Length && text[index] is >= '0' and <= '9');
        }
        else
        {
            return false;
        }

        if (index < text.Length && text[index] == '.')
        {
            index++;
            var digitStart = index;
            while (index < text.Length && text[index] is >= '0' and <= '9')
                index++;

            if (index == digitStart)
                return false;
        }

        if (index < text.Length && text[index] is 'e' or 'E')
        {
            index++;
            if (index < text.Length && text[index] is '+' or '-')
                index++;

            var digitStart = index;
            while (index < text.Length && text[index] is >= '0' and <= '9')
                index++;

            if (index == digitStart)
                return false;
        }

        return index > start && (index >= text.Length || IsJsonDelimiter(text[index]));
    }

    private static bool IsJsonDelimiter(char ch)
    {
        return char.IsWhiteSpace(ch) || ch is ',' or '}' or ']';
    }

    private static bool IsHexDigit(char ch)
    {
        return ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static void SkipWhiteSpaceAndBom(string text, ref int index)
    {
        if (index < text.Length && text[index] == '\uFEFF')
            index++;

        SkipWhiteSpace(text, ref index);
    }

    private static void SkipWhiteSpace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
    }

    private readonly struct TopLevelObjectInfo
    {
        public TopLevelObjectInfo(int openBraceIndex, int closingBraceIndex, bool hasEntries, bool hasTrailingComma)
        {
            OpenBraceIndex = openBraceIndex;
            ClosingBraceIndex = closingBraceIndex;
            HasEntries = hasEntries;
            HasTrailingComma = hasTrailingComma;
        }

        public int OpenBraceIndex { get; }
        public int ClosingBraceIndex { get; }
        public bool HasEntries { get; }
        public bool HasTrailingComma { get; }
    }

    private static string EscapeJson(string value)
    {
        StringBuilder builder = new();
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string? ReadLocalizationFile(string fullPath)
    {
        try
        {
            return File.Exists(fullPath) ? File.ReadAllText(fullPath, Encoding.UTF8) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveFullPath(string targetPath, Project project)
    {
        if (Path.IsPathRooted(targetPath))
            return targetPath;

        if (project.FilePath != null)
        {
            var projectDir = Path.GetDirectoryName(project.FilePath);
            if (projectDir != null)
                return Path.Combine(projectDir, targetPath);
        }

        return targetPath;
    }

    private static bool ShouldWritePhysicalLocalizationFile(string fullPath)
    {
        return !string.IsNullOrWhiteSpace(fullPath) && Path.IsPathRooted(fullPath);
    }

    private sealed class LocalizationJsonCodeAction : CodeAction
    {
        private readonly string _title;
        private readonly string _equivalenceKey;
        private readonly Func<CancellationToken, Task<LocalizationJsonChange>> _createChangeAsync;
        private readonly bool _isInlinable;

        public LocalizationJsonCodeAction(
            string title,
            string equivalenceKey,
            Func<CancellationToken, Task<LocalizationJsonChange>> createChangeAsync,
            bool isInlinable = true)
        {
            _title = title;
            _equivalenceKey = equivalenceKey;
            _createChangeAsync = createChangeAsync;
            _isInlinable = isInlinable;
        }

        public override string Title => _title;
        public override string EquivalenceKey => _equivalenceKey;
        public override bool IsInlinable => _isInlinable;

        protected override async Task<IEnumerable<CodeActionOperation>> ComputeOperationsAsync(CancellationToken cancellationToken)
        {
            var change = await _createChangeAsync(cancellationToken).ConfigureAwait(false);
            var operations = ImmutableArray.CreateBuilder<CodeActionOperation>();
            operations.Add(new ApplyChangesOperation(change.Solution));
            if (change.FileWrites.Length > 0)
                operations.Add(new WriteLocalizationFilesOperation(change.FileWrites));

            return operations.ToImmutable();
        }
    }

    private sealed class WriteLocalizationFilesOperation : CodeActionOperation
    {
        private readonly ImmutableArray<LocalizationFileWrite> _fileWrites;

        public WriteLocalizationFilesOperation(ImmutableArray<LocalizationFileWrite> fileWrites)
        {
            _fileWrites = fileWrites;
        }

        public override string Title => RitsuLibUiText.FixAllMissingLocalizationTitle;

        public override void Apply(Workspace workspace, CancellationToken cancellationToken)
        {
            foreach (var fileWrite in _fileWrites)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var directory = Path.GetDirectoryName(fileWrite.FullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory!);

                File.WriteAllText(fileWrite.FullPath, fileWrite.Content, Encoding.UTF8);
            }
        }
    }

    private readonly struct LocalizationJsonChange
    {
        public LocalizationJsonChange(Solution solution, ImmutableArray<LocalizationFileWrite> fileWrites)
        {
            Solution = solution;
            FileWrites = fileWrites;
        }

        public Solution Solution { get; }
        public ImmutableArray<LocalizationFileWrite> FileWrites { get; }

        public static LocalizationJsonChange Empty(Solution solution)
        {
            return new(solution, ImmutableArray<LocalizationFileWrite>.Empty);
        }
    }

    private readonly struct LocalizationFileWrite
    {
        public LocalizationFileWrite(string fullPath, string content)
        {
            FullPath = fullPath;
            Content = content;
        }

        public string FullPath { get; }
        public string Content { get; }
    }

    private sealed class LocalizationFixRegistration
    {
        public LocalizationFixRegistration(Diagnostic diagnostic, LocalizationFixRequest? request)
        {
            Diagnostic = diagnostic;
            Request = request;
        }

        public Diagnostic Diagnostic { get; }
        public LocalizationFixRequest? Request { get; }
    }

    private sealed class LocalizationFixRequest
    {
        public LocalizationFixRequest(
            string targetPath,
            string language,
            string table,
            bool isI18N,
            ImmutableArray<KeyValuePair<string, string>> entries)
        {
            TargetPath = targetPath;
            Language = language;
            Table = table;
            IsI18N = isI18N;
            Entries = entries;
        }

        public string TargetPath { get; }
        public string Language { get; }
        public string Table { get; }
        public bool IsI18N { get; }
        public ImmutableArray<KeyValuePair<string, string>> Entries { get; }

    }

    private sealed class ResourcePathTodoInsertion
    {
        public ResourcePathTodoInsertion(Diagnostic diagnostic, SyntaxNode target, string resourcePath)
        {
            Diagnostic = diagnostic;
            Target = target;
            ResourcePath = resourcePath;
        }

        public Diagnostic Diagnostic { get; }
        public SyntaxNode Target { get; }
        public string ResourcePath { get; }
    }

    private sealed class ResourcePathTodoDiagnosticComparer : IEqualityComparer<Diagnostic>
    {
        public static ResourcePathTodoDiagnosticComparer Instance { get; } = new();

        public bool Equals(Diagnostic? x, Diagnostic? y)
        {
            if (ReferenceEquals(x, y))
                return true;

            if (x == null || y == null)
                return false;

            return string.Equals(x.Id, y.Id, StringComparison.Ordinal) &&
                   x.Location.SourceSpan.Equals(y.Location.SourceSpan) &&
                   string.Equals(x.Location.SourceTree?.FilePath, y.Location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(GetResourcePath(x), GetResourcePath(y), StringComparison.Ordinal);
        }

        public int GetHashCode(Diagnostic obj)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(obj.Id);
                hash = hash * 31 + obj.Location.SourceSpan.GetHashCode();
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Location.SourceTree?.FilePath ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(GetResourcePath(obj));
                return hash;
            }
        }

        private static string GetResourcePath(Diagnostic diagnostic)
        {
            return diagnostic.Properties.TryGetValue(RitsuLibDiagnosticProperties.ResourcePath, out var resourcePath)
                ? resourcePath ?? string.Empty
                : string.Empty;
        }
    }

    private sealed class AdditionalDocumentText : AdditionalText
    {
        private readonly TextDocument _document;

        public AdditionalDocumentText(TextDocument document)
        {
            _document = document;
            Path = document.FilePath ?? document.Name;
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return _document.GetTextAsync(cancellationToken).GetAwaiter().GetResult();
        }
    }

    private sealed class ProjectAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _globalOptions;

        public ProjectAnalyzerConfigOptionsProvider(string? projectDirectory)
        {
            Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(projectDirectory))
            {
                options["build_property.MSBuildProjectDirectory"] = projectDirectory!;
                options["build_property.ProjectDir"] = projectDirectory!;
                options["build_property.MSBuildProjectFullPath"] = Path.Combine(projectDirectory!, "AnalyzerTests.csproj");
            }

            _globalOptions = new ProjectAnalyzerConfigOptions(options);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return Empty;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return Empty;
        }

        private static AnalyzerConfigOptions Empty { get; } =
            new ProjectAnalyzerConfigOptions(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class ProjectAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _options;

        public ProjectAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, out string value)
        {
            return _options.TryGetValue(key, out value!);
        }
    }

    private static string GetTargetLabel(ImmutableArray<LocalizationFixRequest> requests)
    {
        var distinct = requests
            .Select(GetTargetLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinct.Length == 1 ? distinct[0] : $"{distinct.Length} files";
    }

    private static string GetTargetLabel(LocalizationFixRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Language) &&
            !string.IsNullOrWhiteSpace(request.Table))
            return request.Language + "/" + GetTableFileName(request.Table, request.IsI18N);

        return Path.GetFileName(request.TargetPath);
    }

    private static string GetTableFileName(string table, bool isI18N)
    {
        return isI18N ? table : table + ".json";
    }
}
