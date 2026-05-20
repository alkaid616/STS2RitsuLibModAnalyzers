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

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return new RitsuLibLocalizationFixAllProvider();
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var requests = context.Diagnostics
            .Where(diagnostic => diagnostic.Id == RitsuLibDiagnostics.MissingLocalizationId)
            .Select(ReadRequest)
            .Where(request => request != null)
            .Cast<LocalizationFixRequest>()
            .ToImmutableArray();

        if (requests.Length > 0)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    RitsuLibUiText.AddMissingKeysToTargetTitle(GetTargetLabel(requests)),
                    cancellationToken => AddMissingKeysAsync(context.Document.Project.Solution, context.Document.Project.Id, requests, cancellationToken),
                    "AddMissingRitsuLibLocalizationKeys"),
                context.Diagnostics);

            var tableLabel = GetTableLabel(requests);
            if (!string.IsNullOrEmpty(tableLabel))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        RitsuLibUiText.AddMissingKeysToAllLanguagesTitle(tableLabel),
                        cancellationToken => AddMissingKeysToAllLanguagesForCurrentDiagnosticsAsync(
                            context.Document.Project,
                            requests,
                            cancellationToken),
                        "AddMissingRitsuLibLocalizationKeysToAllLanguages"),
                    context.Diagnostics);
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    RitsuLibUiText.FixAllMissingLocalizationTitle,
                    cancellationToken => AddMissingKeysForProjectAsync(context.Document.Project, requests, cancellationToken),
                    "FixAllMissingRitsuLibLocalizationKeysInProject"),
                context.Diagnostics);
        }

        if (requests.Length > 0)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    RitsuLibUiText.InsertSnippetTitle,
                    cancellationToken => InsertSnippetAsync(context.Document, requests, GetSourceSpan(context.Diagnostics[0]), cancellationToken),
                    "InsertMissingRitsuLibLocalizationSnippet"),
                context.Diagnostics.Where(diagnostic => diagnostic.Id == RitsuLibDiagnostics.MissingLocalizationId).ToImmutableArray());
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

    private static string[] SplitList(string? value)
    {
        return value == null || value.Length == 0
            ? Array.Empty<string>()
            : value.Split(new[] { RitsuLibDiagnosticProperties.ListSeparator }, StringSplitOptions.None);
    }

    private static async Task<Solution> AddMissingKeysForProjectAsync(
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

    private static async Task<Solution> AddMissingKeysToAllLanguagesForCurrentDiagnosticsAsync(
        Project project,
        ImmutableArray<LocalizationFixRequest> currentRequests,
        CancellationToken cancellationToken)
    {
        var projectRequests = await CollectProjectRequestsAsync(project, cancellationToken).ConfigureAwait(false);
        var requests = ExpandCurrentKeysToAllLanguages(projectRequests, currentRequests);
        if (requests.Length == 0)
            requests = currentRequests;

        return await AddMissingKeysAsync(project.Solution, project.Id, requests, cancellationToken).ConfigureAwait(false);
    }

    private static ImmutableArray<LocalizationFixRequest> ExpandCurrentKeysToAllLanguages(
        ImmutableArray<LocalizationFixRequest> projectRequests,
        ImmutableArray<LocalizationFixRequest> currentRequests)
    {
        var builder = ImmutableArray.CreateBuilder<LocalizationFixRequest>();

        foreach (var currentRequest in currentRequests)
        {
            var currentKeys = currentRequest.Entries
                .Select(entry => entry.Key)
                .ToImmutableHashSet(StringComparer.Ordinal);

            foreach (var projectRequest in projectRequests)
            {
                if (!IsSameLocalizationTable(projectRequest, currentRequest))
                    continue;

                var filtered = FilterEntries(projectRequest, currentKeys);
                if (filtered.Length == 0)
                    continue;

                builder.Add(projectRequest.WithEntries(filtered));
            }
        }

        return builder.Distinct().ToImmutableArray();
    }

    private static bool IsSameLocalizationTable(LocalizationFixRequest left, LocalizationFixRequest right)
    {
        return left.IsI18N == right.IsI18N &&
               string.Equals(left.Table, right.Table, StringComparison.Ordinal);
    }

    private static ImmutableArray<KeyValuePair<string, string>> FilterEntries(
        LocalizationFixRequest request,
        IImmutableSet<string> keys)
    {
        return request.Entries
            .Where(entry => keys.Contains(entry.Key))
            .ToImmutableArray();
    }

    private static async Task<ImmutableArray<LocalizationFixRequest>> CollectProjectRequestsAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var diagnostics = await CollectProjectDiagnosticsAsync(project, cancellationToken).ConfigureAwait(false);
        return diagnostics
            .Where(diagnostic => diagnostic.Id == RitsuLibDiagnostics.MissingLocalizationId)
            .Select(ReadRequest)
            .Where(request => request != null)
            .Cast<LocalizationFixRequest>()
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

    private static async Task<Solution> AddMissingKeysAsync(
        Solution solution,
        ProjectId projectId,
        ImmutableArray<LocalizationFixRequest> requests,
        CancellationToken cancellationToken)
    {
        var project = solution.GetProject(projectId);
        if (project == null)
            return solution;

        foreach (var group in requests.GroupBy(request => request.TargetPath, StringComparer.OrdinalIgnoreCase))
        {
            var targetPath = group.Key;
            var document = FindAdditionalDocument(project, targetPath);
            var existingText = document == null
                ? ReadLocalizationFile(targetPath, project) ?? string.Empty
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
                        return solution;
                }

                continue;
            }

            var updatedContent = AddEntriesToJsonObject(existingText, entries);
            var updatedText = SourceText.From(updatedContent, Encoding.UTF8);

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
                return solution;
        }

        return solution;
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

        var token = root.FindToken(Math.Min(diagnosticSpan.Start, Math.Max(0, root.FullSpan.End - 1)));
        var node = token.Parent;
        while (node != null && node.Parent != null && node.Kind() is not SyntaxKind.ClassDeclaration and not SyntaxKind.MethodDeclaration and not SyntaxKind.InvocationExpression and not SyntaxKind.Attribute)
            node = node.Parent;

        node ??= root;
        var comment = SyntaxFactory.Comment(BuildSnippetComment(requests));
        var newNode = node.WithLeadingTrivia(node.GetLeadingTrivia().Add(comment).Add(SyntaxFactory.ElasticCarriageReturnLineFeed));
        var newRoot = root.ReplaceNode(node, newNode);
        return document.WithSyntaxRoot(newRoot);
    }

    private static string BuildSnippetComment(ImmutableArray<LocalizationFixRequest> requests)
    {
        StringBuilder builder = new();
        builder.AppendLine("/*");
        builder.AppendLine(RitsuLibUiText.MissingLocalizationSnippetHeader);

        foreach (var group in requests.GroupBy(request => request.TargetPath, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(group.Key);
            builder.AppendLine("{");

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
                    .AppendLine(comma);
            }

            builder.AppendLine("}");
        }

        builder.AppendLine("*/");
        return builder.ToString();
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

        var trimmed = text.Trim();
        return trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal);
    }

    private static string AddEntriesToJsonObject(
        string existingText,
        ImmutableArray<KeyValuePair<string, string>> entries)
    {
        if (entries.Length == 0)
            return existingText;

        if (string.IsNullOrWhiteSpace(existingText) || existingText.Trim() == "{}")
            return BuildNewJsonObject(entries);

        var closingBrace = existingText.LastIndexOf('}');
        if (closingBrace < 0)
            return existingText;

        var before = existingText.Substring(0, closingBrace).TrimEnd();
        var after = existingText.Substring(closingBrace);
        var hasExistingEntries = before.Trim() != "{";
        StringBuilder builder = new();
        builder.Append(before);
        if (hasExistingEntries)
            builder.Append(',');

        builder.AppendLine();
        for (var i = 0; i < entries.Length; i++)
        {
            var comma = i == entries.Length - 1 ? string.Empty : ",";
            builder.Append("  \"")
                .Append(EscapeJson(entries[i].Key))
                .Append("\": \"")
                .Append(EscapeJson(entries[i].Value))
                .Append('"')
                .AppendLine(comma);
        }

        builder.Append(after.TrimStart());
        if (!builder.ToString().EndsWith("\n", StringComparison.Ordinal))
            builder.AppendLine();

        return builder.ToString();
    }

    private static string BuildNewJsonObject(ImmutableArray<KeyValuePair<string, string>> entries)
    {
        StringBuilder builder = new();
        builder.AppendLine("{");
        for (var i = 0; i < entries.Length; i++)
        {
            var comma = i == entries.Length - 1 ? string.Empty : ",";
            builder.Append("  \"")
                .Append(EscapeJson(entries[i].Key))
                .Append("\": \"")
                .Append(EscapeJson(entries[i].Value))
                .Append('"')
                .AppendLine(comma);
        }

        builder.AppendLine("}");
        return builder.ToString();
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

    private static string? ReadLocalizationFile(string targetPath, Project project)
    {
        try
        {
            var fullPath = ResolveFullPath(targetPath, project);
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

    private sealed class RitsuLibLocalizationFixAllProvider : FixAllProvider
    {
        public override async Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            var allRequests = new List<LocalizationFixRequest>();

            if (fixAllContext.Scope == FixAllScope.Solution)
            {
                foreach (var project in fixAllContext.Solution.Projects)
                {
                    var diagnostics = await fixAllContext.GetAllDiagnosticsAsync(project).ConfigureAwait(false);
                    CollectRequests(diagnostics, allRequests);
                }
            }
            else
            {
                var diagnostics = await fixAllContext.GetAllDiagnosticsAsync(fixAllContext.Project).ConfigureAwait(false);
                CollectRequests(diagnostics, allRequests);
            }

            if (allRequests.Count == 0)
                return null;

            var requestsArray = allRequests.Distinct().ToImmutableArray();
            var targetProject = fixAllContext.Scope == FixAllScope.Solution
                ? fixAllContext.Solution.Projects.FirstOrDefault() ?? fixAllContext.Project
                : fixAllContext.Project;

            return CodeAction.Create(
                RitsuLibUiText.FixAllMissingLocalizationTitle,
                _ => AddMissingKeysAsync(
                    targetProject.Solution, targetProject.Id, requestsArray, CancellationToken.None),
                "FixAllRitsuLibLocalizationKeys");
        }

        private static void CollectRequests(
            ImmutableArray<Diagnostic> diagnostics,
            List<LocalizationFixRequest> requests)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Id != RitsuLibDiagnostics.MissingLocalizationId)
                    continue;

                var request = ReadRequest(diagnostic);
                if (request != null)
                    requests.Add(request);
            }
        }
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

        public LocalizationFixRequest WithEntries(ImmutableArray<KeyValuePair<string, string>> entries)
        {
            return new LocalizationFixRequest(TargetPath, Language, Table, IsI18N, entries);
        }
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
                options["build_property.MSBuildProjectDirectory"] = projectDirectory!;

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

    private static string GetTableLabel(ImmutableArray<LocalizationFixRequest> requests)
    {
        var distinct = requests
            .Select(request => string.IsNullOrWhiteSpace(request.Table)
                ? Path.GetFileName(request.TargetPath)
                : GetTableFileName(request.Table, request.IsI18N))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinct.Length == 1 ? distinct[0] : string.Empty;
    }

    private static string GetTableFileName(string table, bool isI18N)
    {
        return isI18N ? table : table + ".json";
    }
}
