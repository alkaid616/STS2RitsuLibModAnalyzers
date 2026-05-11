using System;
using System.Globalization;

namespace Nothing.STS2RitsuLib.ModAnalyzers;

internal static class RitsuLibUiText
{
    private static bool IsChinese =>
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    public static string MissingLocalizationTitle =>
        IsChinese ? "缺少 RitsuLib 本地化" : "Missing RitsuLib localization";

    public static string MissingLocalizationMessageFormat =>
        IsChinese
            ? "缺少 RitsuLib 本地化键: {3}: {4}"
            : "Missing RitsuLib localization keys: {3}: {4}";

    public static string MissingLocalizationDescription =>
        IsChinese
            ? "RitsuLib 引用的本地化键应存在于对应语言 JSON 中。"
            : "RitsuLib localization keys should exist in the matching language JSON.";

    public static string ManifestDependencyTitle =>
        IsChinese ? "缺少 RitsuLib manifest 依赖" : "Missing RitsuLib manifest dependency";

    public static string ManifestDependencyMessageFormat =>
        IsChinese
            ? "mod_manifest.json 缺少 dependencies 中的 STS2-RitsuLib。"
            : "mod_manifest.json is missing STS2-RitsuLib in dependencies.";

    public static string ManifestDependencyDescription =>
        IsChinese
            ? "使用 RitsuLib public API 的 mod 应在 manifest 里声明 STS2-RitsuLib 依赖。"
            : "Mods that use the RitsuLib public API should declare the STS2-RitsuLib manifest dependency.";

    public static string ModIdMismatchTitle =>
        IsChinese ? "RitsuLib mod id 不一致" : "RitsuLib mod id mismatch";

    public static string ModIdMismatchMessageFormat =>
        IsChinese
            ? "RitsuLib 调用使用的 mod id '{0}' 与 manifest id '{1}' 不一致。"
            : "RitsuLib call uses mod id '{0}', which does not match manifest id '{1}'.";

    public static string ModIdMismatchDescription =>
        IsChinese
            ? "RitsuLib 注册、设置和持久化 key 应使用 manifest 中的同一个 mod id。"
            : "RitsuLib registration, settings, and persistence keys should use the same mod id as the manifest.";

    public static string MissingRegistrationTitle =>
        IsChinese ? "缺少 RitsuLib 类型发现注册" : "Missing RitsuLib type-discovery registration";

    public static string MissingRegistrationMessageFormat =>
        IsChinese
            ? "当前程序集使用 RitsuLib 自动注册属性，但没有调用 ModTypeDiscoveryHub.RegisterModAssembly。"
            : "This assembly uses RitsuLib auto-registration attributes but does not call ModTypeDiscoveryHub.RegisterModAssembly.";

    public static string MissingRegistrationDescription =>
        IsChinese
            ? "自动注册属性需要在 mod 初始化器中注册当前程序集，RitsuLib 才能扫描这些类型。"
            : "Auto-registration attributes require the mod initializer to register the current assembly so RitsuLib can scan its types.";

    public static string MissingGodotScriptsTitle =>
        IsChinese ? "可能缺少 Godot 脚本注册" : "Possible missing Godot script registration";

    public static string MissingGodotScriptsMessageFormat =>
        IsChinese
            ? "检测到 Godot 资源或 Godot 脚本类型，但没有调用 RitsuLibFramework.EnsureGodotScriptsRegistered。"
            : "Godot resources or Godot script types were found, but RitsuLibFramework.EnsureGodotScriptsRegistered was not called.";

    public static string MissingGodotScriptsDescription =>
        IsChinese
            ? "含 Godot C# 脚本的 mod 通常需要在初始化器中登记脚本程序集。"
            : "Mods with Godot C# scripts usually need to register the script assembly in the initializer.";

    public static string ContentPackNotAppliedTitle =>
        IsChinese ? "Content pack 未 Apply" : "Content pack is not applied";

    public static string ContentPackNotAppliedMessageFormat =>
        IsChinese
            ? "CreateContentPack 链式注册没有调用 .Apply()，内容不会进入 RitsuLib 注册窗口。"
            : "CreateContentPack registration chain does not call .Apply(), so the content will not enter the RitsuLib registration window.";

    public static string ContentPackNotAppliedDescription =>
        IsChinese
            ? "RitsuLib content pack builder 只有调用 Apply() 才会调度注册步骤。"
            : "RitsuLib content pack builders schedule their registration steps only when Apply() is called.";

    public static string DuplicatePublicEntryTitle =>
        IsChinese ? "重复的 RitsuLib public entry" : "Duplicate RitsuLib public entry";

    public static string DuplicatePublicEntryMessageFormat =>
        IsChinese
            ? "public entry '{0}' 被多个 RitsuLib 内容注册使用。"
            : "Public entry '{0}' is used by multiple RitsuLib content registrations.";

    public static string DuplicatePublicEntryDescription =>
        IsChinese
            ? "重复 fixed public entry 会导致 ModelDb 身份冲突或运行时注册异常。"
            : "Duplicate fixed public entries can cause ModelDb identity conflicts or runtime registration failures.";

    public static string IdShapeTitle =>
        IsChinese ? "RitsuLib id 形状建议" : "RitsuLib id shape suggestion";

    public static string IdShapeMessageFormat =>
        IsChinese
            ? "{0} '{1}' 含有不推荐的字符；建议使用小写字母、数字、点、短横线或下划线。"
            : "{0} '{1}' contains discouraged characters; prefer lowercase letters, digits, dots, hyphens, or underscores.";

    public static string IdShapeDescription =>
        IsChinese
            ? "稳定 id、stem、source id 和 hotkey 字符串应保持可移植、可读、可序列化。"
            : "Stable ids, stems, source ids, and hotkey strings should stay portable, readable, and serializable.";

    public static string SettingsContractTitle =>
        IsChinese ? "RitsuLib settings 契约问题" : "RitsuLib settings contract issue";

    public static string SettingsContractMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string SettingsContractDescription =>
        IsChinese
            ? "检查 settings builder 与 reflection settings attribute 的 page、section、entry、callback 和参数约束。"
            : "Checks settings builder and reflection settings attribute page, section, entry, callback, and parameter contracts.";

    public static string DataStoreContractTitle =>
        IsChinese ? "RitsuLib ModDataStore 契约问题" : "RitsuLib ModDataStore contract issue";

    public static string DataStoreContractMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string DataStoreContractDescription =>
        IsChinese
            ? "检查 ModDataStore.Register<T> 的 key、文件名、scope 和 migration 基础约束。"
            : "Checks basic ModDataStore.Register<T> key, file name, scope, and migration constraints.";

    public static string PatchContractTitle =>
        IsChinese ? "RitsuLib patch 静态契约问题" : "RitsuLib patch static contract issue";

    public static string PatchContractMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string PatchContractDescription =>
        IsChinese
            ? "检查 IPatchMethod、IModPatches 与 DynamicPatchBuilder.FromMethod 所需的静态成员和方法引用。"
            : "Checks required static members and method references for IPatchMethod, IModPatches, and DynamicPatchBuilder.FromMethod.";

    public static string PatchTargetTitle =>
        IsChinese ? "RitsuLib patch target 问题" : "RitsuLib patch target issue";

    public static string PatchTargetMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string PatchTargetDescription =>
        IsChinese
            ? "检查 ModPatchTarget 或 DynamicPatchBuilder 中可静态验证的目标方法名和参数。"
            : "Checks statically verifiable ModPatchTarget and DynamicPatchBuilder target method names and arguments.";

    public static string ResourcePathTitle =>
        IsChinese ? "RitsuLib 资源路径问题" : "RitsuLib resource path issue";

    public static string ResourcePathMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string ResourcePathDescription =>
        IsChinese
            ? "检查 Godot 资源路径前缀、常见资源是否存在以及资源路径形状。"
            : "Checks Godot resource path prefixes, common resource existence, and resource path shape.";

    public static string AudioStringTitle =>
        IsChinese ? "RitsuLib FMOD 字符串问题" : "RitsuLib FMOD string issue";

    public static string AudioStringMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string AudioStringDescription =>
        IsChinese
            ? "检查 FMOD event、bus、GUID、bank 和 guids.txt 路径字符串形状。"
            : "Checks FMOD event, bus, GUID, bank, and guids.txt path string shape.";

    public static string RuntimeHelperTitle =>
        IsChinese ? "RitsuLib runtime helper 契约问题" : "RitsuLib runtime helper contract issue";

    public static string RuntimeHelperMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string RuntimeHelperDescription =>
        IsChinese
            ? "检查 runtime hotkey、healthbar forecast/graft、free-play binding 等 helper 的字面量约束。"
            : "Checks literal constraints for runtime hotkeys, healthbar forecast/graft, free-play bindings, and similar helpers.";

    public static string LegacyPoolHookTitle =>
        IsChinese ? "旧式 pool hook 覆写" : "Legacy pool hook override";

    public static string LegacyPoolHookMessageFormat =>
        IsChinese
            ? "不建议覆写 {0}；请通过 CreateContentPack、registry 或 manifest 注册池内容。"
            : "Avoid overriding {0}; register pool content through CreateContentPack, registries, or manifests.";

    public static string LegacyPoolHookDescription =>
        IsChinese
            ? "RitsuLib 文档要求新 mod 不再覆写 TypeList*PoolModel 的旧式集合属性。"
            : "RitsuLib docs ask new mods to avoid overriding legacy TypeList*PoolModel collection properties.";

    public static string AddMissingKeysTitle(string targetLabel)
    {
        return IsChinese
            ? $"添加缺失的本地化键到 {targetLabel}"
            : $"Add missing localization keys to {targetLabel}";
    }

    public static string InsertSnippetTitle =>
        IsChinese ? "插入本地化 JSON 片段" : "Insert localization JSON snippet";

    public static string MissingLocalizationSnippetHeader =>
        IsChinese ? "缺失的 RitsuLib 本地化:" : "Missing RitsuLib localization:";

    public static string InsertRegisterModAssemblyTitle =>
        IsChinese ? "插入 RegisterModAssembly 样板" : "Insert RegisterModAssembly boilerplate";

    public static string InsertEnsureGodotScriptsTitle =>
        IsChinese ? "插入 EnsureGodotScriptsRegistered 样板" : "Insert EnsureGodotScriptsRegistered boilerplate";

    public static string AddApplyTitle =>
        IsChinese ? "为 content pack 添加 .Apply()" : "Add .Apply() to content pack";

    public static string InsertSettingsStubTitle =>
        IsChinese ? "生成 settings callback/provider stub" : "Generate settings callback/provider stub";

    public static string InsertPatchStubTitle =>
        IsChinese ? "生成 patch 必要成员 stub" : "Generate required patch members stub";

    public static string InsertTodoFixTitle =>
        IsChinese ? "插入 RitsuLib TODO 修复片段" : "Insert RitsuLib TODO fix snippet";

    // RITSU017: Disposable handle not disposed
    public static string DisposableNotDisposedTitle =>
        IsChinese ? "RitsuLib 可释放句柄未释放" : "RitsuLib disposable handle not disposed";

    public static string DisposableNotDisposedMessageFormat =>
        IsChinese
            ? "{0} 返回的 {1} 未被释放，可能导致资源泄漏。"
            : "{0} returns {1} which is not disposed; this may cause a resource leak.";

    public static string DisposableNotDisposedDescription =>
        IsChinese
            ? "PlayLoop、PlayMusic、CreateManualScope 和 SubscribeLifecycle 返回的句柄应在 using 块中使用或手动释放。"
            : "Handles returned by PlayLoop, PlayMusic, CreateManualScope, and SubscribeLifecycle should be used in a using block or disposed manually.";

    // RITSU018: ContentPackBuilder.For() not applied
    public static string ContentPackBuilderNotAppliedTitle =>
        IsChinese ? "Content pack builder 未 Apply" : "Content pack builder is not applied";

    public static string ContentPackBuilderNotAppliedMessageFormat =>
        IsChinese
            ? "ModContentPackBuilder.For() 链式注册没有调用 .Apply()，内容不会进入 RitsuLib 注册窗口。"
            : "ModContentPackBuilder.For() registration chain does not call .Apply(), so the content will not enter the RitsuLib registration window.";

    public static string ContentPackBuilderNotAppliedDescription =>
        IsChinese
            ? "ModContentPackBuilder.For() 创建的 builder 必须调用 Apply() 才会调度注册步骤。"
            : "Builders created via ModContentPackBuilder.For() must call Apply() to schedule their registration steps.";

    // RITSU019: AudioSource path shape
    public static string AudioSourcePathShapeTitle =>
        IsChinese ? "RitsuLib AudioSource 路径形状问题" : "RitsuLib AudioSource path shape issue";

    public static string AudioSourcePathShapeMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string AudioSourcePathShapeDescription =>
        IsChinese
            ? "检查 AudioSource.Event、Snapshot、Guid 工厂方法的路径前缀和格式。"
            : "Checks AudioSource.Event, Snapshot, and Guid factory method path prefixes and format.";

    // RITSU020: ModInterop attribute shape
    public static string ModInteropShapeTitle =>
        IsChinese ? "RitsuLib ModInterop 属性问题" : "RitsuLib ModInterop attribute issue";

    public static string ModInteropShapeMessageFormat =>
        IsChinese
            ? "[ModInterop] 的目标 mod id '{0}' 为空或格式不推荐。"
            : "[ModInterop] target mod id '{0}' is empty or has a discouraged format.";

    public static string ModInteropShapeDescription =>
        IsChinese
            ? "[ModInterop] 属性需要一个非空且格式规范的目标 mod id。"
            : "The [ModInterop] attribute requires a non-empty, well-formed target mod id.";

    // RITSU021: Character template legacy override
    public static string CharacterTemplateLegacyTitle =>
        IsChinese ? "旧式 character template 属性覆写" : "Legacy character template property override";

    public static string CharacterTemplateLegacyMessageFormat =>
        IsChinese
            ? "不建议覆写 {0}；请通过 CharacterRegistrationEntry 或 content pack builder 注册起始内容。"
            : "Avoid overriding {0}; register starter content through CharacterRegistrationEntry or content pack builder.";

    public static string CharacterTemplateLegacyDescription =>
        IsChinese
            ? "RitsuLib 文档要求新 mod 使用 CharacterRegistrationEntry 而非覆写旧式属性来注册起始卡牌、遗物和药水。"
            : "RitsuLib docs ask new mods to use CharacterRegistrationEntry instead of overriding legacy properties for starter cards, relics, and potions.";

    // RITSU022: Settings subpage reference
    public static string SettingsSubpageReferenceTitle =>
        IsChinese ? "RitsuLib settings subpage 引用问题" : "RitsuLib settings subpage reference issue";

    public static string SettingsSubpageReferenceMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string SettingsSubpageReferenceDescription =>
        IsChinese
            ? "检查 AddSubpage 引用的 page id 是否存在于已注册的 settings pages 中。"
            : "Checks whether the page id referenced by AddSubpage exists among registered settings pages.";

    // RITSU023: InteropTarget shape
    public static string InteropTargetShapeTitle =>
        IsChinese ? "RitsuLib InteropTarget 属性问题" : "RitsuLib InteropTarget attribute issue";

    public static string InteropTargetShapeMessageFormat =>
        IsChinese ? "{0}" : "{0}";

    public static string InteropTargetShapeDescription =>
        IsChinese
            ? "检查 [InteropTarget] 是否在 [ModInterop] 类中使用，以及参数是否非空。"
            : "Checks that [InteropTarget] is used within a [ModInterop] class and that its arguments are non-empty.";

    // RITSU024: Settings duplicate subpage
    public static string SettingsDuplicateSubpageTitle =>
        IsChinese ? "RitsuLib settings 重复 subpage 引用" : "RitsuLib settings duplicate subpage reference";

    public static string SettingsDuplicateSubpageMessageFormat =>
        IsChinese
            ? "同一 section 中多次引用 subpage '{0}'。"
            : "Subpage '{0}' is referenced multiple times in the same section.";

    public static string SettingsDuplicateSubpageDescription =>
        IsChinese
            ? "同一 section 中不应重复引用同一个 subpage。"
            : "The same subpage should not be referenced multiple times within the same section.";

    // RITSU025: Lifecycle event type constraint
    public static string LifecycleEventTypeTitle =>
        IsChinese ? "RitsuLib lifecycle 事件类型约束" : "RitsuLib lifecycle event type constraint";

    public static string LifecycleEventTypeMessageFormat =>
        IsChinese
            ? "SubscribeLifecycleOnce 要求事件类型为 sealed class 或 struct，当前类型 '{0}' 不满足此约束。"
            : "SubscribeLifecycleOnce requires the event type to be a sealed class or struct; '{0}' does not satisfy this constraint.";

    public static string LifecycleEventTypeDescription =>
        IsChinese
            ? "SubscribeLifecycleOnce 使用类型标识来保证一次性订阅语义，非 sealed 类型可能导致多次触发。"
            : "SubscribeLifecycleOnce uses type identity to guarantee one-shot semantics; non-sealed types may fire multiple times.";

    // New code fix titles
    public static string WrapInUsingTitle =>
        IsChinese ? "用 using 语句包装" : "Wrap in using statement";

    public static string AddPrefixTitle(string prefix)
    {
        return IsChinese ? $"添加 {prefix} 前缀" : $"Add {prefix} prefix";
    }

    public static string ObsoleteApiMigrationTitle(string newMethod)
    {
        return IsChinese ? $"迁移到 {newMethod}" : $"Migrate to {newMethod}";
    }

    // RITSU009: Settings contract messages
    public static string SettingsMaxLengthNegative =>
        IsChinese ? "ModSettingsStringAttribute.MaxLength 不能为负数。" : "ModSettingsStringAttribute.MaxLength cannot be negative.";

    public static string SettingsCallbackNotFound(string methodName, string typeName) =>
        IsChinese
            ? $"Settings 回调方法 '{methodName}' 在 '{typeName}' 上未找到。"
            : $"Settings callback method '{methodName}' was not found on '{typeName}'.";

    public static string SettingsSectionIdEmpty =>
        IsChinese ? "Settings section id 不能为空。" : "Settings section id cannot be empty.";

    public static string SettingsSectionDuplicate(string sectionId, string pageId) =>
        IsChinese
            ? $"页面 '{pageId}' 上存在重复的 section id '{sectionId}'。"
            : $"Duplicate settings section id '{sectionId}' on page '{pageId}'.";

    public static string SettingsEntryIdEmpty =>
        IsChinese ? "Settings entry id 不能为空。" : "Settings entry id cannot be empty.";

    public static string SettingsEntryNoSection(string entryId) =>
        IsChinese
            ? $"Settings entry '{entryId}' 无法匹配到任何 section。"
            : $"Settings entry '{entryId}' could not be matched to a section.";

    public static string SettingsChoiceEmpty =>
        IsChinese ? "Settings choice 应至少提供一个选项。" : "Settings choice entries should provide at least one option.";

    public static string SettingsMinMustBeLessThanMax(string label) =>
        IsChinese
            ? $"{label} 最小值必须小于最大值。"
            : $"{label} min must be less than max.";

    public static string SettingsStepMustBePositive(string label) =>
        IsChinese
            ? $"{label} 步长必须大于零。"
            : $"{label} step must be greater than zero.";

    public static string SettingsButtonUseHostParamCount =>
        IsChinese
            ? "ModSettingsButtonAttribute.UseHostContext 要求恰好一个 host-context 参数。"
            : "ModSettingsButtonAttribute.UseHostContext expects exactly one host-context parameter.";

    public static string SettingsButtonShouldBeParameterless =>
        IsChinese
            ? "ModSettingsButtonAttribute 方法应无参数，除非 UseHostContext 为 true。"
            : "ModSettingsButtonAttribute methods should be parameterless unless UseHostContext is true.";

    // RITSU010: DataStore contract messages
    public static string DataStoreKeyEmpty =>
        IsChinese ? "ModDataStore.Register<T> 要求非空 key。" : "ModDataStore.Register<T> requires a non-empty key.";

    public static string DataStoreFileNameEmpty =>
        IsChinese ? "ModDataStore.Register<T> 要求非空 fileName。" : "ModDataStore.Register<T> requires a non-empty fileName.";

    public static string DataStoreFileNameIsPath(string fileName) =>
        IsChinese
            ? $"ModDataStore fileName '{fileName}' 应为文件名而非路径。"
            : $"ModDataStore fileName '{fileName}' should be a file name segment, not a path.";

    public static string DataStoreFileNameMissingJson(string fileName) =>
        IsChinese
            ? $"ModDataStore fileName '{fileName}' 通常应以 .json 结尾。"
            : $"ModDataStore fileName '{fileName}' should normally end with .json.";

    public static string DataStoreMigrationRequiresConfig =>
        IsChinese
            ? "ModDataStore migrations 要求提供包含当前 schema 版本的 migrationConfig。"
            : "ModDataStore migrations require a migrationConfig with the current schema version.";

    // RITSU011: Patch contract messages
    public static string PatchMethodMissingPatchId(string typeName) =>
        IsChinese
            ? $"IPatchMethod 类型 '{typeName}' 必须声明静态 PatchId。"
            : $"IPatchMethod type '{typeName}' must declare static PatchId.";

    public static string PatchMethodMissingGetTargets(string typeName) =>
        IsChinese
            ? $"IPatchMethod 类型 '{typeName}' 必须声明静态 GetTargets()。"
            : $"IPatchMethod type '{typeName}' must declare static GetTargets().";

    public static string ModPatchesMissingAddTo(string typeName) =>
        IsChinese
            ? $"IModPatches 类型 '{typeName}' 必须声明静态 AddTo(ModPatcher patcher)。"
            : $"IModPatches type '{typeName}' must declare static AddTo(ModPatcher patcher).";

    // RITSU012: Patch target messages
    public static string DynamicPatchFromMethodNotFound(string methodName, string typeName) =>
        IsChinese
            ? $"DynamicPatchBuilder.FromMethod 在 '{typeName}' 上未找到静态方法 '{methodName}'。"
            : $"DynamicPatchBuilder.FromMethod could not find static method '{methodName}' on '{typeName}'.";

    public static string DynamicPatchTargetNotFound(string methodName, string typeName, string targetName) =>
        IsChinese
            ? $"DynamicPatchBuilder.{methodName} 目标 '{typeName}.{targetName}' 未找到。"
            : $"DynamicPatchBuilder.{methodName} target '{typeName}.{targetName}' was not found.";

    public static string ModPatchTargetMethodNotFound(string typeName, string methodName) =>
        IsChinese
            ? $"ModPatchTarget 目标方法 '{typeName}.{methodName}' 未找到。"
            : $"ModPatchTarget target method '{typeName}.{methodName}' was not found.";

    // RITSU013: Resource path messages
    public static string ResourcePathMissingPrefix(string value) =>
        IsChinese
            ? $"资源路径 '{value}' 应使用 res:// 或 user:// 前缀。"
            : $"Resource path '{value}' should use res:// or user://.";

    public static string ResourcePathNotFound(string value) =>
        IsChinese
            ? $"资源路径 '{value}' 在 analyzer AdditionalFiles 中未找到。"
            : $"Resource path '{value}' was not found in analyzer AdditionalFiles.";

    // RITSU014: Audio string messages
    public static string FmodBusPathPrefix(string value) =>
        IsChinese
            ? $"FMOD bus 路径 '{value}' 应以 bus:/ 开头。"
            : $"FMOD bus path '{value}' should start with bus:/.";

    public static string FmodEventPathPrefix(string value) =>
        IsChinese
            ? $"FMOD event 路径 '{value}' 应以 event:/ 或 snapshot:/ 开头。"
            : $"FMOD event path '{value}' should start with event:/ or snapshot:/.";

    public static string FmodGuidInvalid(string value) =>
        IsChinese
            ? $"FMOD GUID 字符串 '{value}' 不是有效的 GUID 格式。"
            : $"FMOD GUID string '{value}' is not a valid GUID shape.";

    public static string FmodBankMissingExtension(string value) =>
        IsChinese
            ? $"FMOD bank 资源 '{value}' 应以 .bank 结尾。"
            : $"FMOD bank resource '{value}' should end with .bank.";

    // RITSU015: Runtime helper messages
    public static string FreePlayBindingIdEmpty =>
        IsChinese ? "RegisterFreePlayBinding 要求稳定的非空 binding id。" : "RegisterFreePlayBinding requires a stable non-empty binding id.";

    public static string HotkeyBindingInvalid(string binding) =>
        IsChinese
            ? $"Runtime hotkey 绑定 '{binding}' 字面量格式无效。"
            : $"Runtime hotkey binding '{binding}' has an invalid literal shape.";

    public static string HotkeyOptionsIdEmpty =>
        IsChinese ? "RuntimeHotkeyOptions.Id 应为稳定的非空 id。" : "RuntimeHotkeyOptions.Id should be a stable non-empty id.";

    // RITSU019: AudioSource path shape messages
    public static string AudioSourceEventPrefix(string value) =>
        IsChinese
            ? $"AudioSource.Event 路径 '{value}' 应以 event:/ 开头。"
            : $"AudioSource.Event path '{value}' should start with event:/.";

    public static string AudioSourceSnapshotPrefix(string value) =>
        IsChinese
            ? $"AudioSource.Snapshot 路径 '{value}' 应以 snapshot:/ 开头。"
            : $"AudioSource.Snapshot path '{value}' should start with snapshot:/.";

    public static string AudioSourceGuidInvalid(string value) =>
        IsChinese
            ? $"AudioSource.Guid 字符串 '{value}' 不是有效的 GUID 格式。"
            : $"AudioSource.Guid string '{value}' is not a valid GUID format.";

    // RITSU020: ModInterop shape messages
    public static string ModInteropRequiresModId =>
        IsChinese ? "[ModInterop] 要求非空的目标 mod id。" : "[ModInterop] requires a non-empty target mod id.";

    public static string ModInteropDiscouragedFormat(string modId) =>
        IsChinese
            ? $"[ModInterop] 目标 mod id '{modId}' 格式不推荐。"
            : $"[ModInterop] target mod id '{modId}' has a discouraged format.";

    // RITSU023: InteropTarget shape messages
    public static string InteropTargetRequiresModInterop =>
        IsChinese ? "[InteropTarget] 应在 [ModInterop] 类中使用。" : "[InteropTarget] should be used within a [ModInterop] class.";
}
