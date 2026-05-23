using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

internal static class RitsuLibSyntaxFacts
{
    /// <summary>
    ///     Collapses non-alphanumeric runs into a single underscore.
    ///     e.g. <c>"my-mod"</c> → <c>"my_mod"</c>.
    /// </summary>
    private static readonly Regex NonAlphaNumericRegex = new("[^A-Za-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    ///     Splits a leading acronym (<c>XML</c>) from a following title-case word (<c>Reader</c>).
    ///     e.g. <c>"XMLReader"</c> → <c>"XML_Reader"</c>, <c>"HTTP" + "Se"</c> → <c>"HTTP_Server"</c>.
    /// </summary>
    private static readonly Regex AcronymBoundaryRegex = new("([A-Z]+)([A-Z][a-z])", RegexOptions.Compiled);

    /// <summary>
    ///     Splits a lower-case letter or digit from a following upper-case letter.
    ///     e.g. <c>"MyMod"</c> → <c>"My_Mod"</c>, <c>"Server2Card"</c> → <c>"Server2_Card"</c>.
    /// </summary>
    private static readonly Regex CamelBoundaryRegex = new("([a-z0-9])([A-Z])", RegexOptions.Compiled);

    /// <summary>
    ///     Collapses consecutive underscores into one.
    /// </summary>
    private static readonly Regex RepeatedUnderscoreRegex = new("_+", RegexOptions.Compiled);

    private static readonly Regex RecommendedIdRegex = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.Compiled);

    public static string GetCompoundId(string modId, string typeStem, string localStem)
    {
        return $"{NormalizePublicStem(modId)}_{typeStem.Trim().ToUpperInvariant()}_{NormalizePublicStem(localStem)}";
    }

    /// <summary>
    ///     Normalizes a public stem segment using the same 4-step pipeline as
    ///     <c>STS2RitsuLib.Content.ModContentRegistry.NormalizePublicStem</c>:
    ///     non-alphanumeric → underscore, acronym boundary split, camelCase boundary split,
    ///     repeated underscore collapse, then trim and upper-case.
    /// </summary>
    public static string NormalizePublicStem(string value)
    {
        var normalized = NonAlphaNumericRegex.Replace(value.Trim(), "_");
        normalized = AcronymBoundaryRegex.Replace(normalized, "$1_$2");
        normalized = CamelBoundaryRegex.Replace(normalized, "$1_$2");
        normalized = RepeatedUnderscoreRegex.Replace(normalized, "_");
        return normalized.Trim('_').ToUpperInvariant();
    }

    public static string NormalizeFullPublicEntry(string value)
    {
        var normalized = NonAlphaNumericRegex.Replace(value.Trim(), "_");
        normalized = AcronymBoundaryRegex.Replace(normalized, "$1_$2");
        normalized = CamelBoundaryRegex.Replace(normalized, "$1_$2");
        normalized = RepeatedUnderscoreRegex.Replace(normalized, "_");
        return normalized.Trim('_').ToUpperInvariant();
    }

    public static bool HasRecommendedIdShape(string value)
    {
        return RecommendedIdRegex.IsMatch(value);
    }

    public static bool IsRegisterModAssembly(string methodName, IMethodSymbol? method)
    {
        return methodName == "RegisterModAssembly" &&
               (method?.ContainingType?.Name == "ModTypeDiscoveryHub" || method == null);
    }

    public static bool IsEnsureGodotScriptsRegistered(string methodName, IMethodSymbol? method)
    {
        return methodName == "EnsureGodotScriptsRegistered" &&
               (method?.ContainingType?.Name == "RitsuLibFramework" || method == null);
    }

    public static string? ResolveReceiverModId(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var receiver = GetInvocationReceiver(invocation);
        return receiver == null ? null : ResolveFactoryModId(receiver, semanticModel, cancellationToken, 0);
    }

    public static string? ResolveFactoryModId(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 7)
            return null;

        expression = Unwrap(expression);

        if (expression is InvocationExpressionSyntax invocation)
        {
            var method = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            var methodName = method?.Name ?? GetInvokedMemberName(invocation);
            if (methodName is
                "GetKeywordRegistry" or
                "GetContentRegistry" or
                "GetCardTagRegistry" or
                "GetTimelineRegistry" or
                "GetUnlockRegistry" or
                "GetDataStore" or
                "CreateContentPack" or
                "CreateModLocalization" or
                "CreatePatcher" or
                "RegisterModSettings" or
                "RegisterModSettingsSidebarOrder" or
                "RegisterModSettingsPageOrder" or
                "TryRegisterModSettingsPageOrderAfter" or
                "TryRegisterModSettingsPageOrderBefore" or
                "RegisterHealthBarForecast" or
                "RegisterHealthBarVisualGraft" or
                "For")
            {
                var modId = GetInvocationStringArgument(invocation, method, "modId", 0, semanticModel, cancellationToken)
                            ?? GetInvocationStringArgument(invocation, method, "ownerModId", 0, semanticModel, cancellationToken);
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
                    FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Initializer?.Value,
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

    public static ExpressionSyntax? GetInvocationReceiver(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            MemberBindingExpressionSyntax => null,
            _ => null,
        };
    }

    public static string? GetInvokedMemberName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };
    }

    public static string? GetAttributeShortName(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString().Split('.').Last();
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name : name + "Attribute";
    }

    public static string? GetAttributeStringArgument(
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

    public static ExpressionSyntax? GetAttributeExpressionArgument(AttributeSyntax attribute, int index)
    {
        var args = attribute.ArgumentList?.Arguments;
        if (args == null)
            return null;

        var positional = args.Value
            .Where(arg => arg.NameEquals == null && arg.NameColon == null)
            .ToArray();
        return index >= positional.Length ? null : positional[index].Expression;
    }

    public static string? GetAttributeNamedString(
        AttributeSyntax attribute,
        SemanticModel semanticModel,
        string name,
        System.Threading.CancellationToken cancellationToken)
    {
        var argument = attribute.ArgumentList?.Arguments
            .FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.ValueText == name);
        return argument == null ? null : GetConstantString(argument.Expression, semanticModel, cancellationToken);
    }

    public static ExpressionSyntax? GetAttributeNamedExpression(AttributeSyntax attribute, string name)
    {
        return attribute.ArgumentList?.Arguments
            .FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.ValueText == name)
            ?.Expression;
    }

    public static string? GetInvocationStringArgument(
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

    public static int? GetInvocationIntArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method,
        string parameterName,
        int position,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var argument = FindInvocationArgument(invocation, method, parameterName, position);
        if (argument == null)
            return null;

        var constant = semanticModel.GetConstantValue(argument.Expression, cancellationToken);
        if (!constant.HasValue || constant.Value == null)
            return null;

        return constant.Value switch
        {
            int value => value,
            short value => value,
            long value when value is <= int.MaxValue and >= int.MinValue => (int)value,
            byte value => value,
            _ => null,
        };
    }

    public static double? GetInvocationDoubleArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method,
        string parameterName,
        int position,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var argument = FindInvocationArgument(invocation, method, parameterName, position);
        if (argument == null)
            return null;

        var constant = semanticModel.GetConstantValue(argument.Expression, cancellationToken);
        if (!constant.HasValue || constant.Value == null)
            return null;

        return constant.Value switch
        {
            double value => value,
            float value => value,
            int value => value,
            long value => value,
            decimal value => (double)value,
            _ => null,
        };
    }

    public static ArgumentSyntax? FindInvocationArgument(
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

    public static string? GetConstantString(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        return constant.HasValue ? constant.Value as string : null;
    }

    public static string? GetTypeNameFromTypeOf(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (expression is not TypeOfExpressionSyntax typeOf)
            return null;

        var type = semanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type;
        return type?.Name ?? typeOf.Type.ToString().Split('.').Last();
    }

    public static INamedTypeSymbol? GetTypeSymbolFromTypeOf(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (expression is not TypeOfExpressionSyntax typeOf)
            return null;

        return semanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type as INamedTypeSymbol;
    }

    public static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        return expression;
    }

    public static bool IsNamedType(ITypeSymbol? symbol, string name)
    {
        return symbol?.Name == name;
    }

    public static bool IsKnownDisposableType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        var name = type.Name;
        if (name is "AudioLoopHandle" or "AudioMusicHandle" or "AudioScopeToken" or "AudioAdaptiveMusicHandle" or "IDisposable")
            return true;

        if (type.AllInterfaces.Any(i => i.Name == "IDisposable"))
            return true;

        return false;
    }

    public static bool IsAudioSourceType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        var name = type.Name;
        return name is "AudioSource" or "StudioEventSource" or "StudioGuidSource" or "SoundFileSource" or "StreamingMusicSource" or "SnapshotSource";
    }

    public static bool IsModContentPackBuilderType(ITypeSymbol? type)
    {
        return type?.Name == "ModContentPackBuilder";
    }

    public static bool IsInteropAttribute(string attributeName)
    {
        return attributeName is "ModInteropAttribute" or "InteropTargetAttribute";
    }

    public static bool IsDisposableReturningMethod(string methodName, IMethodSymbol? method)
    {
        if (methodName is not ("PlayLoop" or "PlayMusic" or "CreateManualScope" or "FollowAdaptiveMusic" or "SubscribeLifecycle" or "SubscribeLifecycleOnce"))
            return false;

        if (method == null)
            return true;

        return IsKnownDisposableType(method.ReturnType);
    }
}
