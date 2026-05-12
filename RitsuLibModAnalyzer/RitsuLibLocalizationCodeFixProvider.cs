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
            case RitsuLibDiagnostics.DisposableNotDisposedId:
                context.RegisterCodeFix(
                    CodeAction.Create(
                        RitsuLibUiText.WrapInUsingTitle,
                        cancellationToken => WrapInUsingAsync(context.Document, diagnostic, cancellationToken),
                        "WrapRitsuLibDisposableInUsing"),
                    diagnostic);
                return;

            case RitsuLibDiagnostics.ContentPackBuilderNotAppliedId:
                context.RegisterCodeFix(
                    CodeAction.Create(
                        RitsuLibUiText.AddApplyTitle,
                        cancellationToken => AddApplyAsync(context.Document, diagnostic, cancellationToken),
                        "AddRitsuLibContentPackBuilderApply"),
                    diagnostic);
                return;

            case RitsuLibDiagnostics.AudioSourcePathShapeId:
                if (TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.ExpectedPrefix, out var expectedPrefix) &&
                    expectedPrefix is "event:/" or "snapshot:/")
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            RitsuLibUiText.AddPrefixTitle(expectedPrefix),
                            cancellationToken => AddAudioSourcePrefixAsync(context.Document, diagnostic, expectedPrefix, cancellationToken),
                            $"AddRitsuLibAudioSource{expectedPrefix.Replace(":", "")}Prefix"),
                        diagnostic);
                    return;
                }
                break;

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

            case RitsuLibDiagnostics.MissingRegistrationId:
                if (TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.InsertionText, out var registerText))
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            RitsuLibUiText.InsertRegisterModAssemblyTitle,
                            cancellationToken => InsertInitializerStatementAsync(context.Document, diagnostic, registerText, cancellationToken),
                            "InsertRitsuLibRegisterModAssembly"),
                        diagnostic);
                    return;
                }

                break;

            case RitsuLibDiagnostics.MissingGodotScriptsId:
                if (TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.InsertionText, out var godotText))
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            RitsuLibUiText.InsertEnsureGodotScriptsTitle,
                            cancellationToken => InsertInitializerStatementAsync(context.Document, diagnostic, godotText, cancellationToken),
                            "InsertRitsuLibEnsureGodotScriptsRegistered"),
                        diagnostic);
                    return;
                }

                break;

            case RitsuLibDiagnostics.ContentPackNotAppliedId:
                context.RegisterCodeFix(
                    CodeAction.Create(
                        RitsuLibUiText.AddApplyTitle,
                        cancellationToken => AddApplyAsync(context.Document, diagnostic, cancellationToken),
                        "AddRitsuLibContentPackApply"),
                    diagnostic);
                return;

            case RitsuLibDiagnostics.SettingsContractId:
                if (IsStubKind(diagnostic, "SettingsCallback"))
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            RitsuLibUiText.InsertSettingsStubTitle,
                            cancellationToken => InsertMemberStubAsync(context.Document, diagnostic, cancellationToken),
                            "InsertRitsuLibSettingsStub"),
                        diagnostic);
                    return;
                }

                break;

            case RitsuLibDiagnostics.PatchContractId:
            case RitsuLibDiagnostics.PatchTargetId:
                if (TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.StubKind, out _))
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            RitsuLibUiText.InsertPatchStubTitle,
                            cancellationToken => InsertMemberStubAsync(context.Document, diagnostic, cancellationToken),
                            "InsertRitsuLibPatchStub"),
                        diagnostic);
                    return;
                }

                break;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                RitsuLibUiText.InsertTodoFixTitle,
                cancellationToken => InsertTodoCommentAsync(context.Document, diagnostic, null, cancellationToken),
                "InsertRitsuLibTodoSnippet"),
            diagnostic);
    }

    private static async Task<Document> AddApplyAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var node = FindNodeForDiagnostic(root, diagnostic);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>() ??
                         node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null)
            return await InsertTodoCommentAsync(document, diagnostic, ".Apply()", cancellationToken).ConfigureAwait(false);

        var outer = GetOutermostInvocationInChain(invocation);
        if (InvocationChainContains(outer, "Apply"))
            return document;

        var applyInvocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    outer.WithoutTrivia(),
                    SyntaxFactory.IdentifierName("Apply")))
            .WithTriviaFrom(outer)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(outer, applyInvocation));
    }

    private static async Task<Document> WrapInUsingAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var node = FindNodeForDiagnostic(root, diagnostic);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>() ??
                         node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation?.Parent is not ExpressionStatementSyntax expressionStatement)
            return await InsertTodoCommentAsync(document, diagnostic, null, cancellationToken).ConfigureAwait(false);

        var methodName = TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.DisposableMethod, out var name) ? name : "handle";
        var variableName = $"_{char.ToLowerInvariant(methodName[0])}{methodName.Substring(1)}";

        var usingStatement = SyntaxFactory.UsingStatement(
            declaration: SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.VariableDeclarator(variableName)
                        .WithInitializer(SyntaxFactory.EqualsValueClause(invocation.WithoutTrivia()))
                })),
            expression: null,
            statement: SyntaxFactory.EmptyStatement())
            .WithTriviaFrom(expressionStatement)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(expressionStatement, usingStatement));
    }

    private static async Task<Document> AddAudioSourcePrefixAsync(
        Document document,
        Diagnostic diagnostic,
        string prefix,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var node = FindNodeForDiagnostic(root, diagnostic);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>() ??
                         node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null)
            return document;

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (argument?.Expression is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return document;

        var currentText = literal.Token.ValueText;
        if (currentText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return document;

        var newLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(prefix + currentText));

        return document.WithSyntaxRoot(root.ReplaceNode(literal, newLiteral.WithTriviaFrom(literal)));
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

    private static async Task<Document> InsertInitializerStatementAsync(
        Document document,
        Diagnostic diagnostic,
        string statementText,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var method = FindInitializerMethod(root, diagnostic);
        if (method == null)
            return await InsertTodoCommentAsync(document, diagnostic, statementText, cancellationToken).ConfigureAwait(false);

        var statement = SyntaxFactory.ParseStatement(EnsureStatementTerminator(statementText))
            .WithAdditionalAnnotations(Formatter.Annotation);

        MethodDeclarationSyntax updatedMethod;
        if (method.Body != null)
        {
            updatedMethod = method.WithBody(method.Body.WithStatements(method.Body.Statements.Insert(0, statement)));
        }
        else if (method.ExpressionBody != null)
        {
            var expressionStatement = SyntaxFactory.ExpressionStatement(method.ExpressionBody.Expression);
            updatedMethod = method
                .WithBody(SyntaxFactory.Block(statement, expressionStatement))
                .WithExpressionBody(null)
                .WithSemicolonToken(default);
        }
        else
        {
            return await InsertTodoCommentAsync(document, diagnostic, statementText, cancellationToken).ConfigureAwait(false);
        }

        updatedMethod = updatedMethod.WithAdditionalAnnotations(Formatter.Annotation);
        return document.WithSyntaxRoot(root.ReplaceNode(method, updatedMethod));
    }

    private static async Task<Document> InsertMemberStubAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var type = FindTargetType(root, diagnostic);
        if (type == null)
            return await InsertTodoCommentAsync(document, diagnostic, null, cancellationToken).ConfigureAwait(false);

        var memberTexts = BuildMemberStubTexts(type, diagnostic).ToArray();
        if (memberTexts.Length == 0)
            return await InsertTodoCommentAsync(document, diagnostic, null, cancellationToken).ConfigureAwait(false);

        var members = memberTexts
            .Select(text => SyntaxFactory.ParseMemberDeclaration(text))
            .Where(member => member != null)
            .Cast<MemberDeclarationSyntax>()
            .Select(member => member.WithAdditionalAnnotations(Formatter.Annotation))
            .ToArray();
        if (members.Length == 0)
            return await InsertTodoCommentAsync(document, diagnostic, null, cancellationToken).ConfigureAwait(false);

        var updatedType = type
            .WithMembers(type.Members.AddRange(members))
            .WithAdditionalAnnotations(Formatter.Annotation);
        return document.WithSyntaxRoot(root.ReplaceNode(type, updatedType));
    }

    private static IEnumerable<string> BuildMemberStubTexts(TypeDeclarationSyntax type, Diagnostic diagnostic)
    {
        if (!TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.StubKind, out var stubKind))
            yield break;

        TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.MethodName, out var methodName);
        var typeName = type.Identifier.ValueText;

        switch (stubKind)
        {
            case "SettingsCallback":
                if (!IsSafeIdentifier(methodName) || TypeHasMethod(type, methodName!))
                    yield break;

                yield return $$"""
                    private static void {{methodName}}()
                    {
                        // TODO: Match the RitsuLib settings callback signature expected by this attribute.
                    }
                    """;
                yield break;

            case "PatchMethod":
                if (!TypeHasProperty(type, "PatchId"))
                    yield return $$"""
                        public static string PatchId => nameof({{typeName}});
                        """;

                if (!TypeHasMethod(type, "GetTargets"))
                    yield return """
                        public static STS2RitsuLib.Patching.Models.ModPatchTarget[] GetTargets()
                        {
                            return System.Array.Empty<STS2RitsuLib.Patching.Models.ModPatchTarget>();
                        }
                        """;
                yield break;

            case "ModPatches":
                if (!TypeHasMethod(type, "AddTo"))
                    yield return """
                        public static void AddTo(STS2RitsuLib.Patching.Core.ModPatcher patcher)
                        {
                            // TODO: Register patch groups on the RitsuLib patcher.
                        }
                        """;
                yield break;

            case "PatchTargetMethod":
                if (!IsSafeIdentifier(methodName) || TypeHasMethod(type, methodName!))
                    yield break;

                var modifier = diagnostic.GetMessage().IndexOf("static method", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               diagnostic.GetMessage().IndexOf("FromMethod", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "public static"
                    : "public";
                yield return $$"""
                    {{modifier}} void {{methodName}}()
                    {
                        // TODO: Match the target method signature expected by the patch.
                    }
                    """;
                yield break;

            case "PatchTargetProperty":
                if (!IsSafeIdentifier(methodName) || TypeHasProperty(type, methodName!))
                    yield break;

                yield return $$"""
                    public object? {{methodName}} => null;
                    """;
                yield break;
        }
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

    private static MethodDeclarationSyntax? FindInitializerMethod(SyntaxNode root, Diagnostic diagnostic)
    {
        if (diagnostic.Location.IsInSource)
        {
            var containing = FindNodeForDiagnostic(root, diagnostic).FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (containing != null)
                return containing;
        }

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        return methods.FirstOrDefault(HasModInitializerAttribute) ??
               methods.FirstOrDefault(method => method.Identifier.ValueText == "Initialize") ??
               methods.FirstOrDefault(method => method.Modifiers.Any(SyntaxKind.StaticKeyword)) ??
               methods.FirstOrDefault();
    }

    private static TypeDeclarationSyntax? FindTargetType(SyntaxNode root, Diagnostic diagnostic)
    {
        if (diagnostic.Location.IsInSource)
        {
            var containing = FindNodeForDiagnostic(root, diagnostic).FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (containing != null)
            {
                if (!TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.TypeName, out var requestedName) ||
                    string.Equals(containing.Identifier.ValueText, requestedName, StringComparison.Ordinal))
                {
                    return containing;
                }
            }
        }

        if (!TryGetProperty(diagnostic, RitsuLibDiagnosticProperties.TypeName, out var typeName))
            return null;

        return root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(type => string.Equals(type.Identifier.ValueText, typeName, StringComparison.Ordinal));
    }

    private static bool HasModInitializerAttribute(MethodDeclarationSyntax method)
    {
        return method.AttributeLists
            .SelectMany(list => list.Attributes)
            .Select(attribute => attribute.Name.ToString().Split('.').Last())
            .Any(name => name == "ModInitializer" || name == "ModInitializerAttribute");
    }

    private static InvocationExpressionSyntax GetOutermostInvocationInChain(InvocationExpressionSyntax invocation)
    {
        var current = invocation;
        for (var node = invocation.Parent; node != null; node = node.Parent)
        {
            if (node is InvocationExpressionSyntax parentInvocation && parentInvocation.Span.Contains(current.Span))
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
        return invocation.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(candidate => candidate.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == methodName,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText == methodName,
                _ => false,
            });
    }

    private static bool TypeHasMethod(TypeDeclarationSyntax type, string name)
    {
        return type.Members.OfType<MethodDeclarationSyntax>()
            .Any(member => member.Identifier.ValueText == name);
    }

    private static bool TypeHasProperty(TypeDeclarationSyntax type, string name)
    {
        return type.Members.OfType<PropertyDeclarationSyntax>()
            .Any(member => member.Identifier.ValueText == name);
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

    private static bool IsSafeIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && SyntaxFacts.IsValidIdentifier(value!);
    }

    private static string EnsureStatementTerminator(string statementText)
    {
        var trimmed = statementText.Trim();
        return trimmed.EndsWith(";", StringComparison.Ordinal) ? trimmed : trimmed + ";";
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
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation == null)
            return ImmutableArray<LocalizationFixRequest>.Empty;

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

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        return diagnostics
            .Where(diagnostic => diagnostic.Id == RitsuLibDiagnostics.MissingLocalizationId)
            .Select(ReadRequest)
            .Where(request => request != null)
            .Cast<LocalizationFixRequest>()
            .Distinct()
            .ToImmutableArray();
    }

    private static AnalyzerConfigOptionsProvider CreateAnalyzerConfigOptionsProvider(Project project)
    {
        var projectDirectory = project.FilePath == null ? null : Path.GetDirectoryName(project.FilePath);
        return new ProjectAnalyzerConfigOptionsProvider(projectDirectory);
    }

    private static async Task<bool> CanApplyJsonFixAsync(
        Project project,
        ImmutableArray<LocalizationFixRequest> requests,
        CancellationToken cancellationToken)
    {
        foreach (var request in requests)
        {
            var document = FindAdditionalDocument(project, request.TargetPath);
            if (document == null)
            {
                var diskContent = ReadLocalizationFile(request.TargetPath, project);
                if (diskContent != null && !CanPatchTopLevelObject(diskContent))
                    return false;
                continue;
            }

            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (!CanPatchTopLevelObject(text.ToString()))
                return false;
        }

        return true;
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

            WriteLocalizationFile(targetPath, project, updatedContent);

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

    private static void WriteLocalizationFile(string targetPath, Project project, string content)
    {
        try
        {
            var fullPath = ResolveFullPath(targetPath, project);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, content, new UTF8Encoding(false));
        }
        catch
        {
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
