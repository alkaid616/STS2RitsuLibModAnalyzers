using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RitsuLibObsoleteApiCodeFixProvider)), Shared]
public sealed class RitsuLibObsoleteApiCodeFixProvider : CodeFixProvider
{
    private static readonly ImmutableArray<string> ObsoleteDiagnosticIds =
        ImmutableArray.Create("CS0618", "CS0619");

    public sealed override ImmutableArray<string> FixableDiagnosticIds => ObsoleteDiagnosticIds;

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.Id is not ("CS0618" or "CS0619"))
                continue;

            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node == null)
                continue;

            if (node is InvocationExpressionSyntax invocation)
            {
                RegisterInvocationMigration(context, diagnostic, invocation);
            }
            else if (node is PropertyDeclarationSyntax property)
            {
                RegisterPropertyMigration(context, diagnostic, property);
            }
        }
    }

    private static void RegisterInvocationMigration(
        CodeFixContext context,
        Diagnostic diagnostic,
        InvocationExpressionSyntax invocation)
    {
        var memberName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };

        if (memberName == null)
            return;

        var migrationTarget = memberName switch
        {
            "Register" => "RegisterOwned",
            "RegisterCardKeyword" => "RegisterCardKeywordOwnedByLocNamespace",
            "CardKeyword" => "CardKeywordOwnedByLocNamespace",
            "Keyword" => "KeywordOwned",
            _ => null,
        };

        if (migrationTarget != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    RitsuLibUiText.ObsoleteApiMigrationTitle(migrationTarget),
                    cancellationToken => ReplaceMethodNameAsync(context.Document, invocation, memberName, migrationTarget, cancellationToken),
                    $"Migrate{memberName}To{migrationTarget}"),
                diagnostic);
            return;
        }

        if (memberName == "AddSlider" && HasFloatArgument(invocation))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    RitsuLibUiText.ObsoleteApiMigrationTitle("AddSlider(double)"),
                    cancellationToken => ConvertSliderFloatToDoubleAsync(context.Document, invocation, cancellationToken),
                    "MigrateAddSliderFloatToDouble"),
                diagnostic);
        }
    }

    private static void RegisterPropertyMigration(
        CodeFixContext context,
        Diagnostic diagnostic,
        PropertyDeclarationSyntax property)
    {
        var name = property.Identifier.ValueText;
        if (name is not ("StartingDeckTypes" or "StartingRelicTypes" or "StartingPotionTypes"))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                RitsuLibUiText.ObsoleteApiMigrationTitle("CharacterRegistrationEntry"),
                cancellationToken => ReplacePropertyWithTodoAsync(context.Document, property, name, cancellationToken),
                $"Migrate{name}ToCharacterRegistrationEntry"),
            diagnostic);
    }

    private static async Task<Document> ReplaceMethodNameAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string oldName,
        string newName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess == null)
            return document;

        var newMemberAccess = memberAccess.WithName(
            SyntaxFactory.IdentifierName(newName).WithTriviaFrom(memberAccess.Name));

        return document.WithSyntaxRoot(root.ReplaceNode(memberAccess, newMemberAccess));
    }

    private static async Task<Document> ConvertSliderFloatToDoubleAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var arguments = invocation.ArgumentList.Arguments;
        var newArguments = new SyntaxNodeOrToken[arguments.Count * 2 - 1];
        for (var i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            if (arg.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.NumericLiteralExpression) &&
                literal.Token.Value is float)
            {
                var doubleValue = (double)(float)literal.Token.Value;
                var newLiteral = SyntaxFactory.LiteralExpression(
                    SyntaxKind.NumericLiteralExpression,
                    SyntaxFactory.Literal(doubleValue));
                arg = arg.WithExpression(newLiteral.WithTriviaFrom(literal));
            }

            newArguments[i * 2] = arg;
            if (i < arguments.Count - 1)
                newArguments[i * 2 + 1] = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTriviaFrom(arguments.GetSeparator(i));
        }

        var newArgumentList = invocation.ArgumentList.WithArguments(
            SyntaxFactory.SeparatedList<ArgumentSyntax>(newArguments));
        return document.WithSyntaxRoot(root.ReplaceNode(invocation.ArgumentList, newArgumentList));
    }

    private static async Task<Document> ReplacePropertyWithTodoAsync(
        Document document,
        PropertyDeclarationSyntax property,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newApi = propertyName switch
        {
            "StartingDeckTypes" => "CharacterRegistrationEntry.AddStartingCard<TCard>(count)",
            "StartingRelicTypes" => "CharacterRegistrationEntry.AddStartingRelic<TRelic>(count)",
            "StartingPotionTypes" => "CharacterRegistrationEntry.AddStartingPotion<TPotion>(count)",
            _ => "CharacterRegistrationEntry",
        };

        var comment = SyntaxFactory.Comment(
            $"// TODO: Replace {propertyName} with {newApi} in CharacterRegistrationEntry or content pack builder.");

        var trivia = property.GetLeadingTrivia().Add(comment).Add(SyntaxFactory.ElasticCarriageReturnLineFeed);
        var newProperty = property.WithLeadingTrivia(trivia)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(property, newProperty));
    }

    private static bool HasFloatArgument(InvocationExpressionSyntax invocation)
    {
        return invocation.ArgumentList.Arguments.Any(arg =>
            arg.Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.NumericLiteralExpression) &&
            literal.Token.Value is float);
    }
}
