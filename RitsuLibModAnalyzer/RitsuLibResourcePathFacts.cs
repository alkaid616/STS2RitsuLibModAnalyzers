using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

internal static class RitsuLibResourcePathFacts
{
    private static readonly string[] AssetExtensions =
    {
        ".png", ".jpg", ".jpeg", ".webp", ".svg", ".ogg", ".wav", ".mp3", ".bank", ".tscn", ".tres", ".res",
        ".theme", ".json", ".txt", ".gdshader", ".material",
    };

    public static bool IsResourcePath(string value)
    {
        return value.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("user://", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeResourcePath(string resourcePath)
    {
        var text = resourcePath.Trim().Replace('\\', '/');
        if (text.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            text = text.Substring("res://".Length);
        else if (text.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
            text = text.Substring("user://".Length);

        return text.TrimStart('/');
    }

    public static bool LooksLikeFileResource(string value)
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

    public static bool IsResourceArgumentName(string? name)
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

    public static bool IsLikelyResourceMethod(string methodName)
    {
        return methodName.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Scene", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Texture", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Icon", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Bank", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Guid", StringComparison.OrdinalIgnoreCase) ||
               methodName is "Event" or "Guid" or "RegisterBank" or "RegisterStudioGuidMappings";
    }

    public static bool IsAssetProfileType(ITypeSymbol? type)
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

    public static bool IsStringType(ITypeSymbol? type)
    {
        return type?.SpecialType == SpecialType.System_String;
    }

    public static bool TryResolveStringExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out string? result)
    {
        result = ResolveStringExpression(expression, semanticModel, cancellationToken, allowNonStringConstant: false)?.Trim();
        return !string.IsNullOrWhiteSpace(result);
    }

    public static string? ResolveStringExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool allowNonStringConstant)
    {
        expression = Unwrap(expression);

        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constant.HasValue)
        {
            if (constant.Value is string text)
                return text;

            return allowNonStringConstant ? constant.Value?.ToString() : null;
        }

        if (expression is InterpolatedStringExpressionSyntax interpolated &&
            TryResolveInterpolatedString(interpolated, semanticModel, cancellationToken, out var interpolatedValue))
            return interpolatedValue;

        if (expression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.AddExpression))
        {
            var left = ResolveStringExpression(binary.Left, semanticModel, cancellationToken, allowNonStringConstant: false);
            var right = ResolveStringExpression(binary.Right, semanticModel, cancellationToken, allowNonStringConstant: false);
            return left != null && right != null ? left + right : null;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (IsCurrentInstanceGetTypeDotName(memberAccess))
                return GetEnclosingTypeName(memberAccess, semanticModel, cancellationToken);

            if (memberAccess.Name.Identifier.ValueText == "Name" &&
                memberAccess.Expression is TypeOfExpressionSyntax typeOf)
            {
                var type = semanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type;
                return type?.Name ?? typeOf.Type.ToString().Split('.').Last();
            }
        }

        return ResolveSymbolBackedStringExpression(expression, semanticModel, cancellationToken);
    }

    public static bool IsResourcePathContext(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var argument = expression.FirstAncestorOrSelf<ArgumentSyntax>();
        if (argument != null)
            return IsResourceArgumentContext(argument, semanticModel, cancellationToken);

        var attributeArgument = expression.FirstAncestorOrSelf<AttributeArgumentSyntax>();
        if (attributeArgument != null)
            return IsResourceAttributeArgumentContext(attributeArgument, semanticModel, cancellationToken);

        var assignment = expression.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
        if (assignment != null && IsResourceAssignmentContext(assignment, semanticModel, cancellationToken))
            return true;

        var property = expression.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property != null &&
            property.Modifiers.Any(SyntaxKind.OverrideKeyword) &&
            IsResourceArgumentName(property.Identifier.ValueText))
            return true;

        return false;
    }

    public static async Task<ImmutableArray<ResourceRootSymbol>> FindResourceRootSymbolsAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null)
            return ImmutableArray<ResourceRootSymbol>.Empty;

        var builder = ImmutableArray.CreateBuilder<ResourceRootSymbol>();
        foreach (var variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (variable.Initializer?.Value == null)
                continue;

            var symbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken) as IFieldSymbol;
            if (symbol == null || !symbol.IsStatic || !IsStringType(symbol.Type))
                continue;

            var value = ResolveStringExpression(variable.Initializer.Value, semanticModel, cancellationToken, allowNonStringConstant: false);
            AddRootSymbol(builder, symbol.Name, GetMemberExpression(symbol), value);
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(property, cancellationToken) as IPropertySymbol;
            if (symbol == null || !symbol.IsStatic || !IsStringType(symbol.Type))
                continue;

            var expression = property.ExpressionBody?.Expression ?? property.Initializer?.Value;
            if (expression == null)
                continue;

            var value = ResolveStringExpression(expression, semanticModel, cancellationToken, allowNonStringConstant: false);
            AddRootSymbol(builder, symbol.Name, GetMemberExpression(symbol), value);
        }

        return builder
            .GroupBy(symbol => symbol.Expression, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(symbol => symbol.Score).First())
            .OrderByDescending(symbol => symbol.Score)
            .ThenBy(symbol => symbol.Expression, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public static bool TrySelectRootSymbol(
        string targetResourcePath,
        ImmutableArray<ResourceRootSymbol> symbols,
        out ResourceRootSymbol symbol)
    {
        var target = NormalizeResourcePath(targetResourcePath);
        var matches = symbols
            .Where(candidate =>
            {
                var root = NormalizeResourcePath(candidate.Value);
                return target.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                       target.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(candidate => NormalizeResourcePath(candidate.Value).Length)
            .ThenByDescending(candidate => candidate.Score)
            .ToArray();

        if (matches.Length == 1)
        {
            symbol = matches[0];
            return true;
        }

        symbol = default;
        return false;
    }

    public static string? GetEnclosingTypeName(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetEnclosingSymbol(node.SpanStart, cancellationToken)?.ContainingType;
        if (symbol != null)
            return symbol.Name;

        return node.FirstAncestorOrSelf<TypeDeclarationSyntax>()?.Identifier.ValueText;
    }

    public static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }

    internal static bool IsAssetPath(string path)
    {
        var extension = Path.GetExtension(path);
        return AssetExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               path.EndsWith(".guids.txt", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".theme.json", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsIgnoredPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/.git/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/.godot/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool TryGetProjectRelativePath(string path, string? projectDirectory, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return false;

        var root = Path.GetFullPath(projectDirectory!);
        var fullPath = Path.GetFullPath(path);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                                root.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            return false;

        relativePath = fullPath.Substring(rootWithSeparator.Length).Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(relativePath) && !relativePath.StartsWith("../", StringComparison.Ordinal);
    }

    private static bool IsResourceArgumentContext(
        ArgumentSyntax argument,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var invocation = argument.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation != null)
        {
            var method = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            var methodName = method?.Name ?? GetInvokedMemberName(invocation);
            var parameter = GetInvocationParameter(method, argument, GetArgumentPosition(invocation.ArgumentList.Arguments, argument));
            var name = argument.NameColon?.Name.Identifier.ValueText ?? parameter?.Name;
            return IsResourceArgumentName(name) || (methodName != null && IsLikelyResourceMethod(methodName));
        }

        var creation = argument.FirstAncestorOrSelf<BaseObjectCreationExpressionSyntax>();
        if (creation != null)
        {
            var type = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
            var isAssetProfile = IsAssetProfileType(type);
            var constructor = semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol as IMethodSymbol;
            var parameter = GetInvocationParameter(constructor, argument, GetArgumentPosition(creation.ArgumentList?.Arguments ?? default, argument));
            var name = argument.NameColon?.Name.Identifier.ValueText ?? parameter?.Name;
            return isAssetProfile
                ? parameter == null || IsStringType(parameter.Type) || IsResourceArgumentName(name)
                : IsResourceArgumentName(name);
        }

        return false;
    }

    private static bool IsResourceAttributeArgumentContext(
        AttributeArgumentSyntax argument,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var attribute = argument.FirstAncestorOrSelf<AttributeSyntax>();
        if (attribute == null)
            return false;

        var constructor = semanticModel.GetSymbolInfo(attribute, cancellationToken).Symbol as IMethodSymbol;
        var parameter = GetAttributeParameter(constructor, argument, GetArgumentPosition(attribute.ArgumentList?.Arguments ?? default, argument));
        var name = argument.NameEquals?.Name.Identifier.ValueText ??
                   argument.NameColon?.Name.Identifier.ValueText ??
                   parameter?.Name;
        return IsResourceArgumentName(name);
    }

    private static bool IsResourceAssignmentContext(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
        if (symbol is IPropertySymbol property && (IsStringType(property.Type) || IsResourceArgumentName(property.Name)))
            return true;
        if (symbol is IFieldSymbol field && (IsStringType(field.Type) || IsResourceArgumentName(field.Name)))
            return true;

        if (assignment.Left is IdentifierNameSyntax identifier && IsResourceArgumentName(identifier.Identifier.ValueText))
            return true;
        if (assignment.Left is MemberAccessExpressionSyntax memberAccess && IsResourceArgumentName(memberAccess.Name.Identifier.ValueText))
            return true;

        var initializer = assignment.FirstAncestorOrSelf<InitializerExpressionSyntax>();
        var creation = initializer?.Parent as BaseObjectCreationExpressionSyntax;
        var type = creation == null ? null : semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        return IsAssetProfileType(type);
    }

    private static bool TryResolveInterpolatedString(
        InterpolatedStringExpressionSyntax interpolated,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out string? result)
    {
        var sb = new StringBuilder();
        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolatedStringTextSyntax text)
            {
                sb.Append(text.TextToken.ValueText);
                continue;
            }

            if (content is InterpolationSyntax interpolation)
            {
                var part = ResolveStringExpression(interpolation.Expression, semanticModel, cancellationToken, allowNonStringConstant: true);
                if (part == null)
                {
                    result = null;
                    return false;
                }

                sb.Append(part);
                continue;
            }

            result = null;
            return false;
        }

        result = sb.ToString();
        return true;
    }

    private static string? ResolveSymbolBackedStringExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol == null)
            return null;

        if (symbol is not IPropertySymbol and not IFieldSymbol and not ILocalSymbol)
            return null;

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax(cancellationToken);
            ExpressionSyntax? initializer = syntax switch
            {
                PropertyDeclarationSyntax prop => prop.Initializer?.Value ?? prop.ExpressionBody?.Expression,
                VariableDeclaratorSyntax var => var.Initializer?.Value,
                _ => null,
            };

            if (initializer == null)
                continue;

            var initializerModel = initializer.SyntaxTree == semanticModel.SyntaxTree
                ? semanticModel
                : semanticModel.Compilation.GetSemanticModel(initializer.SyntaxTree);
            var value = ResolveStringExpression(initializer, initializerModel, cancellationToken, allowNonStringConstant: false);
            if (value != null)
                return value;
        }

        return null;
    }

    private static bool IsCurrentInstanceGetTypeDotName(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Name.Identifier.ValueText == "Name" &&
               memberAccess.Expression is InvocationExpressionSyntax invocation &&
               IsCurrentInstanceGetTypeInvocation(invocation);
    }

    private static bool IsCurrentInstanceGetTypeInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count != 0)
            return false;

        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "GetType",
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name.Identifier.ValueText == "GetType" =>
                Unwrap(memberAccess.Expression) is ThisExpressionSyntax or BaseExpressionSyntax,
            _ => false,
        };
    }

    private static void AddRootSymbol(
        ImmutableArray<ResourceRootSymbol>.Builder builder,
        string name,
        string expression,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value!.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return;

        builder.Add(new ResourceRootSymbol(expression, value.TrimEnd('/'), GetRootSymbolScore(name)));
    }

    private static int GetRootSymbolScore(string name)
    {
        if (name.Equals("ResPath", StringComparison.OrdinalIgnoreCase))
            return 100;
        if (name.Contains("ResPath", StringComparison.OrdinalIgnoreCase))
            return 95;
        if (name.Contains("ResourceRoot", StringComparison.OrdinalIgnoreCase))
            return 90;
        if (name.Contains("ResourcePath", StringComparison.OrdinalIgnoreCase))
            return 85;
        if (name.Contains("RootPath", StringComparison.OrdinalIgnoreCase))
            return 80;
        if (name.Contains("Root", StringComparison.OrdinalIgnoreCase))
            return 60;

        return 10;
    }

    private static string GetMemberExpression(ISymbol symbol)
    {
        var containingType = symbol.ContainingType;
        if (containingType == null)
            return symbol.Name;

        return containingType.Name + "." + symbol.Name;
    }

    private static int GetArgumentPosition<TArgument>(SeparatedSyntaxList<TArgument> arguments, TArgument argument)
        where TArgument : SyntaxNode
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] == argument)
                return i;
        }

        return -1;
    }

    private static IParameterSymbol? GetInvocationParameter(IMethodSymbol? method, ArgumentSyntax argument, int position)
    {
        if (method == null)
            return null;

        var name = argument.NameColon?.Name.Identifier.ValueText;
        if (!string.IsNullOrWhiteSpace(name))
            return method.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal));

        return position >= 0 && position < method.Parameters.Length ? method.Parameters[position] : null;
    }

    private static IParameterSymbol? GetAttributeParameter(IMethodSymbol? constructor, AttributeArgumentSyntax argument, int position)
    {
        if (constructor == null)
            return null;

        var name = argument.NameColon?.Name.Identifier.ValueText;
        if (!string.IsNullOrWhiteSpace(name))
            return constructor.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal));

        return position >= 0 && position < constructor.Parameters.Length ? constructor.Parameters[position] : null;
    }

    private static string? GetInvokedMemberName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            _ => null,
        };
    }
}

internal readonly struct ResourceRootSymbol
{
    public ResourceRootSymbol(string expression, string value, int score)
    {
        Expression = expression;
        Value = value;
        Score = score;
    }

    public string Expression { get; }
    public string Value { get; }
    public int Score { get; }
}

internal sealed class RitsuLibResourcePathIndex
{
    private readonly ImmutableArray<string> _assetRelativePaths;

    private RitsuLibResourcePathIndex(
        ImmutableArray<string> assetRelativePaths,
        ImmutableArray<string> resourceRoots,
        string? manifestModId)
    {
        _assetRelativePaths = assetRelativePaths;
        ResourceRoots = resourceRoots;
        ManifestModId = manifestModId;
    }

    public ImmutableArray<string> AssetRelativePaths => _assetRelativePaths;
    public ImmutableArray<string> ResourceRoots { get; }
    public string? ManifestModId { get; }

    public static async Task<RitsuLibResourcePathIndex> CreateAsync(Project project, CancellationToken cancellationToken)
    {
        var projectDirectory = project.FilePath == null ? null : Path.GetDirectoryName(project.FilePath);
        HashSet<string> assets = new(StringComparer.OrdinalIgnoreCase);
        string? manifestModId = null;

        AddProjectDirectoryAssets(projectDirectory, assets);
        foreach (var document in project.AdditionalDocuments)
        {
            var path = document.FilePath;
            if (string.IsNullOrWhiteSpace(path) || RitsuLibResourcePathFacts.IsIgnoredPath(path!))
                continue;

            AddAssetPath(path!, projectDirectory, assets);
            if (Path.GetFileName(path).Equals("mod_manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                manifestModId = RitsuLibAdditionalFileIndex.JsonTopLevelKeyScanner.ReadStringProperty(text.ToString(), "id");
            }
        }

        var assetArray = assets
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        var roots = assetArray
            .Select(GetFirstSegment)
            .Concat(string.IsNullOrWhiteSpace(manifestModId) ? Array.Empty<string>() : new[] { manifestModId! })
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => root!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        return new RitsuLibResourcePathIndex(assetArray, roots, manifestModId);
    }

    public string? TryFindExistingResourcePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var relative = RitsuLibResourcePathFacts.NormalizeResourcePath(value);
        if (string.IsNullOrWhiteSpace(relative))
            return null;

        if (_assetRelativePaths.Contains(relative, StringComparer.OrdinalIgnoreCase))
            return "res://" + relative;

        var matches = _assetRelativePaths
            .Where(path => path.EndsWith("/" + relative, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length == 1 ? "res://" + matches[0] : null;
    }

    public string GetFallbackResourcePath(string value)
    {
        if (RitsuLibResourcePathFacts.IsResourcePath(value))
            return value;

        var relative = RitsuLibResourcePathFacts.NormalizeResourcePath(value);
        if (string.IsNullOrWhiteSpace(relative))
            return value;

        var existing = TryFindExistingResourcePath(value);
        if (existing != null)
            return existing;

        if (!string.IsNullOrWhiteSpace(ManifestModId))
            return "res://" + ManifestModId!.Trim('/') + "/" + relative;

        if (ResourceRoots.Length == 1 && !relative.StartsWith(ResourceRoots[0] + "/", StringComparison.OrdinalIgnoreCase))
            return "res://" + ResourceRoots[0] + "/" + relative;

        return "res://" + relative;
    }

    public ImmutableArray<ResourcePathCompletion> GetPathCompletions(string typedValue)
    {
        if (!typedValue.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return ImmutableArray<ResourcePathCompletion>.Empty;

        var relative = RitsuLibResourcePathFacts.NormalizeResourcePath(typedValue);
        var builder = ImmutableArray.CreateBuilder<ResourcePathCompletion>();
        if (string.IsNullOrEmpty(relative))
        {
            foreach (var root in ResourceRoots)
                builder.Add(new ResourcePathCompletion(root + "/", "res://" + root + "/", "resource root"));

            foreach (var file in GetProjectRootFiles())
                builder.Add(new ResourcePathCompletion(file, "res://" + file, "resource file"));
            return DistinctCompletions(builder);
        }

        var slash = relative.LastIndexOf('/');
        var directoryPrefix = slash < 0 ? string.Empty : relative.Substring(0, slash + 1);
        var segmentPrefix = slash < 0 ? relative : relative.Substring(slash + 1);
        foreach (var asset in _assetRelativePaths)
        {
            if (!asset.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = asset.Substring(directoryPrefix.Length);
            if (!remainder.StartsWith(segmentPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var nextSlash = remainder.IndexOf('/');
            var segment = nextSlash < 0 ? remainder : remainder.Substring(0, nextSlash + 1);
            var replacement = "res://" + directoryPrefix + segment;
            builder.Add(new ResourcePathCompletion(segment, replacement, nextSlash < 0 ? "resource file" : "resource directory"));
        }

        return DistinctCompletions(builder);
    }

    private static ImmutableArray<ResourcePathCompletion> DistinctCompletions(ImmutableArray<ResourcePathCompletion>.Builder builder)
    {
        return builder
            .GroupBy(completion => completion.ReplacementText, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(completion => completion.ReplacementText, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private IEnumerable<string> GetProjectRootFiles()
    {
        foreach (var asset in _assetRelativePaths)
        {
            if (asset.IndexOf('/') < 0)
                yield return asset;
        }
    }

    private static void AddProjectDirectoryAssets(string? projectDirectory, HashSet<string> assets)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(projectDirectory!, "*", SearchOption.AllDirectories))
            {
                if (RitsuLibResourcePathFacts.IsIgnoredPath(file))
                    continue;

                AddAssetPath(file, projectDirectory, assets);
            }
        }
        catch
        {
        }
    }

    private static void AddAssetPath(string path, string? projectDirectory, HashSet<string> assets)
    {
        if (!RitsuLibResourcePathFacts.IsAssetPath(path))
            return;

        if (RitsuLibResourcePathFacts.TryGetProjectRelativePath(path, projectDirectory, out var relativePath))
            assets.Add(relativePath);
    }

    private static string? GetFirstSegment(string path)
    {
        var slash = path.IndexOf('/');
        return slash < 0 ? null : path.Substring(0, slash);
    }
}

internal readonly struct ResourcePathCompletion
{
    public ResourcePathCompletion(string displayText, string replacementText, string description)
    {
        DisplayText = displayText;
        ReplacementText = replacementText;
        Description = description;
    }

    public string DisplayText { get; }
    public string ReplacementText { get; }
    public string Description { get; }
}
