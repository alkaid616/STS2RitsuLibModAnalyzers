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
using Microsoft.CodeAnalysis.Text;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RitsuLibLocalizationCodeFixProvider)), Shared]
public sealed class RitsuLibLocalizationCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(RitsuLibModAnalyzer.MissingLocalizationId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var requests = context.Diagnostics
            .Select(ReadRequest)
            .Where(request => request != null)
            .Cast<LocalizationFixRequest>()
            .ToImmutableArray();

        if (requests.Length == 0)
            return;

        if (await CanApplyJsonFixAsync(context.Document.Project, requests, context.CancellationToken).ConfigureAwait(false))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    RitsuLibUiText.AddMissingKeysTitle(GetTargetLabel(requests)),
                    cancellationToken => AddMissingKeysAsync(context.Document.Project.Solution, context.Document.Project.Id, requests, cancellationToken),
                    "AddMissingRitsuLibLocalizationKeys"),
                context.Diagnostics);
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                RitsuLibUiText.InsertSnippetTitle,
                cancellationToken => InsertSnippetAsync(context.Document, requests, context.Diagnostics[0].Location.SourceSpan, cancellationToken),
                "InsertMissingRitsuLibLocalizationSnippet"),
            context.Diagnostics);
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

    private static async Task<bool> CanApplyJsonFixAsync(
        Project project,
        ImmutableArray<LocalizationFixRequest> requests,
        CancellationToken cancellationToken)
    {
        foreach (var request in requests)
        {
            var document = FindAdditionalDocument(project, request.TargetPath);
            if (document == null)
                continue;

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
                ? string.Empty
                : (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();

            if (!CanPatchTopLevelObject(existingText))
                continue;

            var entries = group
                .SelectMany(request => request.Entries)
                .GroupBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => entry.First())
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToImmutableArray();

            var updatedText = SourceText.From(AddEntriesToJsonObject(existingText, entries), Encoding.UTF8);
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

    private static string GetTargetLabel(ImmutableArray<LocalizationFixRequest> requests)
    {
        var distinct = requests
            .Select(request => request.TargetPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinct.Length == 1
            ? Path.GetFileName(distinct[0])
            : $"{distinct.Length} files";
    }
}
