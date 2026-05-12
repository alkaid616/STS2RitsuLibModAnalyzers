using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

[ExportCompletionProvider(nameof(RitsuLibResourcePathCompletionProvider), LanguageNames.CSharp), Shared]
public sealed class RitsuLibResourcePathCompletionProvider : CompletionProvider
{
    private const string InsertionTextProperty = "InsertionText";
    private const string DescriptionProperty = "Description";
    private const string SpanStartProperty = "SpanStart";
    private const string SpanLengthProperty = "SpanLength";

    public override bool ShouldTriggerCompletion(SourceText text, int position, CompletionTrigger trigger, Microsoft.CodeAnalysis.Options.OptionSet options)
    {
        if (trigger.Kind == CompletionTriggerKind.Invoke)
            return true;

        return trigger.Character is ':' or '/' or '.' ||
               char.IsLetterOrDigit(trigger.Character);
    }

    public override async Task ProvideCompletionsAsync(CompletionContext context)
    {
        var items = await GetCompletionItemsAsync(context.Document, context.Position, context.CancellationToken).ConfigureAwait(false);
        foreach (var item in items)
            context.AddItem(item);
    }

    internal static async Task<ImmutableArray<CompletionItem>> GetCompletionItemsAsync(
        Document document,
        int position,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null)
            return ImmutableArray<CompletionItem>.Empty;

        if (!TryGetResourceStringContext(
                document,
                root,
                semanticModel,
                text,
                position,
                cancellationToken,
                out var stringContext))
        {
            return ImmutableArray<CompletionItem>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<CompletionItem>();
        var resourceIndex = await RitsuLibResourcePathIndex.CreateAsync(document.Project, cancellationToken).ConfigureAwait(false);
        foreach (var completion in resourceIndex.GetPathCompletions(stringContext.TypedValue))
            builder.Add(CreateItem(completion.DisplayText, completion.ReplacementText, completion.Description, stringContext.ReplacementSpan));

        foreach (var template in await CreateTemplateCompletionsAsync(
                     document,
                     semanticModel,
                     resourceIndex,
                     stringContext,
                     cancellationToken).ConfigureAwait(false))
        {
            builder.Add(CreateItem(template.DisplayText, template.ReplacementText, template.Description, stringContext.ReplacementSpan));
        }

        return builder.ToImmutable();
    }

    public override Task<CompletionChange> GetChangeAsync(
        Document document,
        CompletionItem item,
        char? commitKey,
        CancellationToken cancellationToken)
    {
        if (item.Properties.TryGetValue(InsertionTextProperty, out var insertionText))
        {
            var span = TryGetReplacementSpan(item, out var replacementSpan)
                ? replacementSpan
                : item.Span;
            var change = new TextChange(span, insertionText);
            return Task.FromResult(CompletionChange.Create(
                change,
                ImmutableArray.Create(change),
                span.Start + insertionText.Length,
                includesCommitCharacter: false));
        }

        return base.GetChangeAsync(document, item, commitKey, cancellationToken);
    }

    public override Task<CompletionDescription?> GetDescriptionAsync(
        Document document,
        CompletionItem item,
        CancellationToken cancellationToken)
    {
        if (item.Properties.TryGetValue(DescriptionProperty, out var description))
            return Task.FromResult<CompletionDescription?>(CompletionDescription.FromText(description));

        return base.GetDescriptionAsync(document, item, cancellationToken)!;
    }

    private static CompletionItem CreateItem(string displayText, string insertionText, string description, TextSpan span)
    {
        var properties = ImmutableDictionary.CreateBuilder<string, string>();
        properties[InsertionTextProperty] = insertionText;
        properties[DescriptionProperty] = description;
        properties[SpanStartProperty] = span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
        properties[SpanLengthProperty] = span.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return CompletionItem.Create(
            displayText,
            inlineDescription: description,
            properties: properties.ToImmutable());
    }

    private static bool TryGetReplacementSpan(CompletionItem item, out TextSpan span)
    {
        span = default;
        if (!item.Properties.TryGetValue(SpanStartProperty, out var startText) ||
            !item.Properties.TryGetValue(SpanLengthProperty, out var lengthText) ||
            !int.TryParse(startText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(lengthText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var length))
        {
            return false;
        }

        span = new TextSpan(start, length);
        return true;
    }

    private static bool TryGetResourceStringContext(
        Document document,
        SyntaxNode root,
        SemanticModel semanticModel,
        SourceText text,
        int position,
        CancellationToken cancellationToken,
        out ResourceStringContext context)
    {
        context = default;
        if (position <= 0 || position > text.Length)
            return false;

        var token = root.FindToken(Math.Max(0, position - 1), findInsideTrivia: true);
        var expression = token.Parent?.AncestorsAndSelf()
            .OfType<ExpressionSyntax>()
            .FirstOrDefault(node => node is LiteralExpressionSyntax literal &&
                                        literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression) ||
                                    node is InterpolatedStringExpressionSyntax);
        if (expression == null || !expression.Span.Contains(position - 1))
            return false;

        if (!RitsuLibResourcePathFacts.IsResourcePathContext(expression, semanticModel, cancellationToken))
            return false;

        if (!TryGetContentSpan(expression, text, out var contentSpan))
            return false;

        if (position < contentSpan.Start || position > contentSpan.End)
            return false;

        var typedValue = text.ToString(TextSpan.FromBounds(contentSpan.Start, position));
        if (!typedValue.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return false;

        context = new ResourceStringContext(
            expression,
            typedValue,
            TextSpan.FromBounds(contentSpan.Start, position),
            expression is InterpolatedStringExpressionSyntax);
        return true;
    }

    private static bool TryGetContentSpan(ExpressionSyntax expression, SourceText text, out TextSpan contentSpan)
    {
        contentSpan = default;
        var source = text.ToString(expression.Span);
        var firstQuote = source.IndexOf('"');
        var lastQuote = source.LastIndexOf('"');
        if (firstQuote < 0 || lastQuote <= firstQuote)
            return false;

        contentSpan = TextSpan.FromBounds(
            expression.Span.Start + firstQuote + 1,
            expression.Span.Start + lastQuote);
        return true;
    }

    private static async Task<ImmutableArray<ResourcePathCompletion>> CreateTemplateCompletionsAsync(
        Document document,
        SemanticModel semanticModel,
        RitsuLibResourcePathIndex resourceIndex,
        ResourceStringContext context,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<ResourcePathCompletion>();
        var typeName = RitsuLibResourcePathFacts.GetEnclosingTypeName(context.Expression, semanticModel, cancellationToken) ?? "TypeName";
        var fileStem = context.IsInterpolated ? "{GetType().Name}" : typeName;
        var roots = resourceIndex.ResourceRoots;
        if (roots.Length == 0 && !string.IsNullOrWhiteSpace(resourceIndex.ManifestModId))
            roots = ImmutableArray.Create(resourceIndex.ManifestModId!);

        foreach (var root in roots)
        {
            AddTemplateSet(builder, "res://" + root, fileStem);
        }

        if (context.IsInterpolated)
        {
            var symbols = await RitsuLibResourcePathFacts.FindResourceRootSymbolsAsync(document, cancellationToken).ConfigureAwait(false);
            foreach (var symbol in symbols)
                AddTemplateSet(builder, "{" + symbol.Expression + "}", "{GetType().Name}", symbol.Expression + " => " + symbol.Value);
        }

        return builder
            .GroupBy(completion => completion.ReplacementText, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(completion => completion.ReplacementText, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private static void AddTemplateSet(
        ImmutableArray<ResourcePathCompletion>.Builder builder,
        string root,
        string fileStem,
        string description = "RitsuLib resource template")
    {
        AddTemplate(builder, root, "images/relics", fileStem, description);
        AddTemplate(builder, root, "images/cards", fileStem, description);
        AddTemplate(builder, root, "images/characters", fileStem, description);
        AddTemplate(builder, root, "images/keywords", fileStem, description);
    }

    private static void AddTemplate(
        ImmutableArray<ResourcePathCompletion>.Builder builder,
        string root,
        string directory,
        string fileStem,
        string description)
    {
        var replacement = root.TrimEnd('/') + "/" + directory + "/" + fileStem + ".png";
        builder.Add(new ResourcePathCompletion(replacement, replacement, description));
    }

    private readonly struct ResourceStringContext
    {
        public ResourceStringContext(
            ExpressionSyntax expression,
            string typedValue,
            TextSpan replacementSpan,
            bool isInterpolated)
        {
            Expression = expression;
            TypedValue = typedValue;
            ReplacementSpan = replacementSpan;
            IsInterpolated = isInterpolated;
        }

        public ExpressionSyntax Expression { get; }
        public string TypedValue { get; }
        public TextSpan ReplacementSpan { get; }
        public bool IsInterpolated { get; }
    }
}
