using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

internal static class RitsuLibDiagnostics
{
    public const string Category = "RitsuLib";

    public const string MissingLocalizationId = "RITSU001";
    public const string ManifestDependencyId = "RITSU002";
    public const string ModIdMismatchId = "RITSU003";
    public const string MissingRegistrationId = "RITSU004";
    public const string MissingGodotScriptsId = "RITSU005";
    public const string ContentPackNotAppliedId = "RITSU006";
    public const string DuplicatePublicEntryId = "RITSU007";
    public const string IdShapeId = "RITSU008";
    public const string SettingsContractId = "RITSU009";
    public const string DataStoreContractId = "RITSU010";
    public const string PatchContractId = "RITSU011";
    public const string PatchTargetId = "RITSU012";
    public const string ResourcePathId = "RITSU013";
    public const string AudioStringId = "RITSU014";
    public const string RuntimeHelperId = "RITSU015";
    public const string LegacyPoolHookId = "RITSU016";
    public const string DisposableNotDisposedId = "RITSU017";
    public const string ContentPackBuilderNotAppliedId = "RITSU018";
    public const string AudioSourcePathShapeId = "RITSU019";
    public const string ModInteropShapeId = "RITSU020";
    public const string CharacterTemplateLegacyId = "RITSU021";
    public const string SettingsSubpageReferenceId = "RITSU022";
    public const string InteropTargetShapeId = "RITSU023";
    public const string SettingsDuplicateSubpageId = "RITSU024";
    public const string LifecycleEventTypeId = "RITSU025";

    public static DiagnosticDescriptor MissingLocalizationRule => new(
        MissingLocalizationId,
        RitsuLibUiText.MissingLocalizationTitle,
        RitsuLibUiText.MissingLocalizationMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: RitsuLibUiText.MissingLocalizationDescription);

    public static DiagnosticDescriptor ManifestDependencyRule => new(
        ManifestDependencyId,
        RitsuLibUiText.ManifestDependencyTitle,
        RitsuLibUiText.ManifestDependencyMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.ManifestDependencyDescription);

    public static DiagnosticDescriptor ModIdMismatchRule => new(
        ModIdMismatchId,
        RitsuLibUiText.ModIdMismatchTitle,
        RitsuLibUiText.ModIdMismatchMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: RitsuLibUiText.ModIdMismatchDescription);

    public static DiagnosticDescriptor MissingRegistrationRule => new(
        MissingRegistrationId,
        RitsuLibUiText.MissingRegistrationTitle,
        RitsuLibUiText.MissingRegistrationMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: RitsuLibUiText.MissingRegistrationDescription);

    public static DiagnosticDescriptor MissingGodotScriptsRule => new(
        MissingGodotScriptsId,
        RitsuLibUiText.MissingGodotScriptsTitle,
        RitsuLibUiText.MissingGodotScriptsMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.MissingGodotScriptsDescription);

    public static DiagnosticDescriptor ContentPackNotAppliedRule => new(
        ContentPackNotAppliedId,
        RitsuLibUiText.ContentPackNotAppliedTitle,
        RitsuLibUiText.ContentPackNotAppliedMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: RitsuLibUiText.ContentPackNotAppliedDescription);

    public static DiagnosticDescriptor DuplicatePublicEntryRule => new(
        DuplicatePublicEntryId,
        RitsuLibUiText.DuplicatePublicEntryTitle,
        RitsuLibUiText.DuplicatePublicEntryMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: RitsuLibUiText.DuplicatePublicEntryDescription);

    public static DiagnosticDescriptor IdShapeRule => new(
        IdShapeId,
        RitsuLibUiText.IdShapeTitle,
        RitsuLibUiText.IdShapeMessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: RitsuLibUiText.IdShapeDescription);

    public static DiagnosticDescriptor SettingsContractRule => new(
        SettingsContractId,
        RitsuLibUiText.SettingsContractTitle,
        RitsuLibUiText.SettingsContractMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.SettingsContractDescription);

    public static DiagnosticDescriptor DataStoreContractRule => new(
        DataStoreContractId,
        RitsuLibUiText.DataStoreContractTitle,
        RitsuLibUiText.DataStoreContractMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.DataStoreContractDescription);

    public static DiagnosticDescriptor PatchContractRule => new(
        PatchContractId,
        RitsuLibUiText.PatchContractTitle,
        RitsuLibUiText.PatchContractMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: RitsuLibUiText.PatchContractDescription);

    public static DiagnosticDescriptor PatchTargetRule => new(
        PatchTargetId,
        RitsuLibUiText.PatchTargetTitle,
        RitsuLibUiText.PatchTargetMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: RitsuLibUiText.PatchTargetDescription);

    public static DiagnosticDescriptor ResourcePathRule => new(
        ResourcePathId,
        RitsuLibUiText.ResourcePathTitle,
        RitsuLibUiText.ResourcePathMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.ResourcePathDescription);

    public static DiagnosticDescriptor AudioStringRule => new(
        AudioStringId,
        RitsuLibUiText.AudioStringTitle,
        RitsuLibUiText.AudioStringMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.AudioStringDescription);

    public static DiagnosticDescriptor RuntimeHelperRule => new(
        RuntimeHelperId,
        RitsuLibUiText.RuntimeHelperTitle,
        RitsuLibUiText.RuntimeHelperMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.RuntimeHelperDescription);

    public static DiagnosticDescriptor LegacyPoolHookRule => new(
        LegacyPoolHookId,
        RitsuLibUiText.LegacyPoolHookTitle,
        RitsuLibUiText.LegacyPoolHookMessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: RitsuLibUiText.LegacyPoolHookDescription);

    public static DiagnosticDescriptor DisposableNotDisposedRule => new(
        DisposableNotDisposedId,
        RitsuLibUiText.DisposableNotDisposedTitle,
        RitsuLibUiText.DisposableNotDisposedMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.DisposableNotDisposedDescription);

    public static DiagnosticDescriptor ContentPackBuilderNotAppliedRule => new(
        ContentPackBuilderNotAppliedId,
        RitsuLibUiText.ContentPackBuilderNotAppliedTitle,
        RitsuLibUiText.ContentPackBuilderNotAppliedMessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: RitsuLibUiText.ContentPackBuilderNotAppliedDescription);

    public static DiagnosticDescriptor AudioSourcePathShapeRule => new(
        AudioSourcePathShapeId,
        RitsuLibUiText.AudioSourcePathShapeTitle,
        RitsuLibUiText.AudioSourcePathShapeMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.AudioSourcePathShapeDescription);

    public static DiagnosticDescriptor ModInteropShapeRule => new(
        ModInteropShapeId,
        RitsuLibUiText.ModInteropShapeTitle,
        RitsuLibUiText.ModInteropShapeMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.ModInteropShapeDescription);

    public static DiagnosticDescriptor CharacterTemplateLegacyRule => new(
        CharacterTemplateLegacyId,
        RitsuLibUiText.CharacterTemplateLegacyTitle,
        RitsuLibUiText.CharacterTemplateLegacyMessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: RitsuLibUiText.CharacterTemplateLegacyDescription);

    public static DiagnosticDescriptor SettingsSubpageReferenceRule => new(
        SettingsSubpageReferenceId,
        RitsuLibUiText.SettingsSubpageReferenceTitle,
        RitsuLibUiText.SettingsSubpageReferenceMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.SettingsSubpageReferenceDescription);

    public static DiagnosticDescriptor InteropTargetShapeRule => new(
        InteropTargetShapeId,
        RitsuLibUiText.InteropTargetShapeTitle,
        RitsuLibUiText.InteropTargetShapeMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.InteropTargetShapeDescription);

    public static DiagnosticDescriptor SettingsDuplicateSubpageRule => new(
        SettingsDuplicateSubpageId,
        RitsuLibUiText.SettingsDuplicateSubpageTitle,
        RitsuLibUiText.SettingsDuplicateSubpageMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.SettingsDuplicateSubpageDescription);

    public static DiagnosticDescriptor LifecycleEventTypeRule => new(
        LifecycleEventTypeId,
        RitsuLibUiText.LifecycleEventTypeTitle,
        RitsuLibUiText.LifecycleEventTypeMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: RitsuLibUiText.LifecycleEventTypeDescription);

    public static ImmutableArray<DiagnosticDescriptor> CreateSupported()
    {
        return ImmutableArray.Create(
            MissingLocalizationRule,
            ManifestDependencyRule,
            ModIdMismatchRule,
            MissingRegistrationRule,
            MissingGodotScriptsRule,
            ContentPackNotAppliedRule,
            DuplicatePublicEntryRule,
            IdShapeRule,
            SettingsContractRule,
            DataStoreContractRule,
            PatchContractRule,
            PatchTargetRule,
            ResourcePathRule,
            AudioStringRule,
            RuntimeHelperRule,
            LegacyPoolHookRule,
            DisposableNotDisposedRule,
            ContentPackBuilderNotAppliedRule,
            AudioSourcePathShapeRule,
            ModInteropShapeRule,
            CharacterTemplateLegacyRule,
            SettingsSubpageReferenceRule,
            InteropTargetShapeRule,
            SettingsDuplicateSubpageRule,
            LifecycleEventTypeRule);
    }

    public static ImmutableArray<DiagnosticDescriptor> CreateContractSupported()
    {
        return ImmutableArray.Create(
            ManifestDependencyRule,
            ModIdMismatchRule,
            MissingRegistrationRule,
            MissingGodotScriptsRule,
            ContentPackNotAppliedRule,
            DuplicatePublicEntryRule,
            IdShapeRule,
            SettingsContractRule,
            DataStoreContractRule,
            PatchContractRule,
            PatchTargetRule,
            ResourcePathRule,
            AudioStringRule,
            RuntimeHelperRule,
            LegacyPoolHookRule,
            DisposableNotDisposedRule,
            ContentPackBuilderNotAppliedRule,
            AudioSourcePathShapeRule,
            ModInteropShapeRule,
            CharacterTemplateLegacyRule,
            SettingsSubpageReferenceRule,
            InteropTargetShapeRule,
            SettingsDuplicateSubpageRule,
            LifecycleEventTypeRule);
    }

    public static readonly ImmutableArray<string> FixableIds = ImmutableArray.Create(
        MissingLocalizationId,
        MissingRegistrationId,
        MissingGodotScriptsId,
        ContentPackNotAppliedId,
        SettingsContractId,
        PatchContractId,
        PatchTargetId,
        ResourcePathId,
        RuntimeHelperId,
        DisposableNotDisposedId,
        ContentPackBuilderNotAppliedId,
        AudioSourcePathShapeId);
}
