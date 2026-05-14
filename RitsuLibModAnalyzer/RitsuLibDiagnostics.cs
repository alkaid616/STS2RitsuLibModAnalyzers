using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

internal static class RitsuLibDiagnostics
{
    public const string Category = "RitsuLib";

    public const string MissingLocalizationId = "RITSU001";
    public const string ResourcePathId = "RITSU013";

    public static DiagnosticDescriptor MissingLocalizationRule => new(
        MissingLocalizationId,
        RitsuLibUiText.MissingLocalizationTitle,
        RitsuLibUiText.MissingLocalizationMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: RitsuLibUiText.MissingLocalizationDescription);

    public static DiagnosticDescriptor ResourcePathRule => new(
        ResourcePathId,
        RitsuLibUiText.ResourcePathTitle,
        RitsuLibUiText.ResourcePathMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.ResourcePathDescription);

    public static ImmutableArray<DiagnosticDescriptor> CreateSupported()
    {
        return ImmutableArray.Create(
            MissingLocalizationRule,
            ResourcePathRule);
    }

    public static ImmutableArray<DiagnosticDescriptor> CreateContractSupported()
    {
        return ImmutableArray.Create(
            ResourcePathRule);
    }

    public static readonly ImmutableArray<string> FixableIds = ImmutableArray.Create(
        MissingLocalizationId,
        ResourcePathId);
}
