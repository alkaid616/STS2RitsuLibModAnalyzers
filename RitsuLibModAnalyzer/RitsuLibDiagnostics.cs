using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

internal static class RitsuLibDiagnostics
{
    public const string Category = "RitsuLib";

    public const string MissingLocalizationId = "RITSU001";
    public const string AncientDialogueRepeatMixedId = "RITSU002";
    public const string UnknownLocalizationTableId = "RITSU003";
    public const string ResourcePathId = "RITSU013";

    public static DiagnosticDescriptor MissingLocalizationRule => new(
        MissingLocalizationId,
        RitsuLibUiText.MissingLocalizationTitle,
        RitsuLibUiText.MissingLocalizationMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.MissingLocalizationDescription);

    public static DiagnosticDescriptor AncientDialogueRepeatMixedRule => new(
        AncientDialogueRepeatMixedId,
        RitsuLibUiText.AncientDialogueRepeatMixedTitle,
        RitsuLibUiText.AncientDialogueRepeatMixedMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.AncientDialogueRepeatMixedDescription);

    public static DiagnosticDescriptor UnknownLocalizationTableRule => new(
        UnknownLocalizationTableId,
        RitsuLibUiText.UnknownLocalizationTableTitle,
        RitsuLibUiText.UnknownLocalizationTableMessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: RitsuLibUiText.UnknownLocalizationTableDescription);

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
            AncientDialogueRepeatMixedRule,
            UnknownLocalizationTableRule,
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
