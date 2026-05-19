namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    private static AdditionalText CompleteCardKeywordJson(string language, string id)
    {
        return AdditionalJson($@"C:\mod\localization\{language}\card_keywords.json", $$"""
        {
          "{{id}}.title": "Title",
          "{{id}}.description": "Description"
        }
        """);
    }

    // RITSU017: Disposable handle not disposed

    private static string Source(string body)
    {
        return BuildSource(body, includeRegistration: true);
    }

    private static string SourceWithoutRegistration(string body)
    {
        return BuildSource(body, includeRegistration: false);
    }

    private static string BuildSource(string body, bool includeRegistration)
    {
        return $$"""
            using System;
            using System.Reflection;
            using MegaCrit.Sts2.Core.Localization.DynamicVars;
            using MegaCrit.Sts2.Core.Models;
            using MegaCrit.Sts2.Core.Models.Powers;
            using MegaCrit.Sts2.Core.Timeline;
            using STS2RitsuLib;
            using STS2RitsuLib.Audio;
            using STS2RitsuLib.CardPiles;
            using STS2RitsuLib.Cards.DynamicVars;
            using STS2RitsuLib.Scaffolding.Characters;
            using STS2RitsuLib.Content;
            using STS2RitsuLib.Interop;
            using STS2RitsuLib.Interop.AutoRegistration;
            using STS2RitsuLib.Keywords;
            using STS2RitsuLib.Localization;
            using STS2RitsuLib.Data;
            using STS2RitsuLib.Combat.Powers;
            using STS2RitsuLib.Patching.Builders;
            using STS2RitsuLib.Patching.Core;
            using STS2RitsuLib.Patching.Models;
            using STS2RitsuLib.RuntimeInput;
            using STS2RitsuLib.Scaffolding.Content;
            using STS2RitsuLib.Settings;
            using STS2RitsuLib.Timeline;
            using STS2RitsuLib.TopBar;
            using STS2RitsuLib.Utils;

            namespace ManosabaLin
            {
                public static class MainFile
                {
                    public const string ModId = "ManosabaLin";

                public static void Initialize()
                {
            {{(includeRegistration ? "                        ModTypeDiscoveryHub.RegisterModAssembly(ModId, Assembly.GetExecutingAssembly());" : string.Empty)}}
                }
            }

                internal static class TestContent
                {
            {{Indent(body, 8)}}
                }
            }

            namespace STS2RitsuLib
            {
                public static class RitsuLibFramework
                {
                    public static ModContentPackBuilder CreateContentPack(string modId) => new();
                    public static I18N CreateModLocalization(string modId, string instanceName) => new();
                    public static ModKeywordRegistry GetKeywordRegistry(string modId) => new();
                    public static ModContentRegistry GetContentRegistry(string modId) => new();
                    public static ModTimelineRegistry GetTimelineRegistry(string modId) => new();
                    public static void EnsureGodotScriptsRegistered(Assembly assembly) { }
                    public static ModSettingsPageBuilder RegisterModSettings(string modId, string title, string? pageId = null) => new();
                    public static void RegisterHealthBarForecast(string modId, string sourceId) { }
                    public static void RegisterHealthBarVisualGraft(string modId, string sourceId) { }
                    public static void RegisterFreePlayBinding(string bindingId) { }
                    public static void LoadIcon(string resourcePath) { }
                    public static void RegisterBank(string bankPath) { }
                    public static void PlayEvent(string eventPath) { }
                    public static void SetBus(string busPath) { }
                    public static void SetGuid(string guid) { }
                    public static IDisposable SubscribeLifecycle<TEvent>(Action<TEvent> handler) => null!;
                    public static IDisposable SubscribeLifecycleOnce<TEvent>(Action<TEvent> handler) => null!;
                }
            }

            namespace STS2RitsuLib.Interop
            {
                public static class ModTypeDiscoveryHub
                {
                    public static void RegisterModAssembly(string modId, Assembly assembly) { }
                }
            }

            namespace STS2RitsuLib.Interop.AutoRegistration
            {
                public abstract class AutoRegistrationAttribute : Attribute { }
                public abstract class ContentRegistrationAttribute : AutoRegistrationAttribute { }

                public sealed class RegisterOwnedCardKeywordAttribute : AutoRegistrationAttribute
                {
                    public RegisterOwnedCardKeywordAttribute(string localKeywordStem) { }
                    public string? IconPath { get; set; }
                    public string? IconOutlinePath { get; set; }
                    public string? BigIconPath { get; set; }
                }

                public sealed class RegisterOwnedKeywordAttribute : AutoRegistrationAttribute
                {
                    public RegisterOwnedKeywordAttribute(string localKeywordStem) { }
                    public string TitleTable { get; set; } = "card_keywords";
                    public string? TitleKey { get; set; }
                    public string? DescriptionTable { get; set; }
                    public string? DescriptionKey { get; set; }
                }

                public sealed class RegisterOwnedCardPileAttribute : AutoRegistrationAttribute
                {
                    public RegisterOwnedCardPileAttribute(string localPileStem) { }
                }

                public sealed class RegisterOwnedTopBarButtonAttribute : AutoRegistrationAttribute
                {
                    public RegisterOwnedTopBarButtonAttribute(string localButtonStem) { }
                }

                public sealed class RegisterCardAttribute : ContentRegistrationAttribute
                {
                    public RegisterCardAttribute(Type poolType) { }
                    public string? StableEntryStem { get; set; }
                    public string? FullPublicEntry { get; set; }
                }

                public sealed class RegisterPowerAttribute : ContentRegistrationAttribute { }
                public sealed class RegisterOrbAttribute : ContentRegistrationAttribute { }
                public sealed class RegisterCharacterAttribute : ContentRegistrationAttribute { }
                public sealed class RegisterSharedAncientAttribute : ContentRegistrationAttribute { }
                public sealed class RegisterEpochAttribute : AutoRegistrationAttribute { }
                public sealed class RegisterStoryAttribute : AutoRegistrationAttribute { }
                public sealed class RegisterStoryEpochAttribute : AutoRegistrationAttribute
                {
                    public RegisterStoryEpochAttribute(Type storyType) { }
                }

                public sealed class RitsuLibOwnedByAttribute : Attribute
                {
                    public RitsuLibOwnedByAttribute(string modId) { }
                }
            }

            namespace STS2RitsuLib.Content
            {
                public sealed class ModContentRegistry
                {
                    public static ModContentRegistry For(string modId) => new();
                    public void RegisterCard<TPool, TCard>() { }
                    public void RegisterPower<TPower>() { }
                    public void RegisterOrb<TOrb>() { }
                }

                public readonly struct ModelPublicEntryOptions
                {
                    public static ModelPublicEntryOptions FromStem(string entryStem) => new();
                    public static ModelPublicEntryOptions FromFullPublicEntry(string fullPublicEntry) => new();
                }
            }

            namespace STS2RitsuLib.Scaffolding.Content
            {
                public sealed class ModContentPackBuilder
                {
                    public static ModContentPackBuilder For(string modId) => new();
                    public ModContentPackBuilder Card<TPool, TCard>() => this;
                    public ModContentPackBuilder Card<TPool, TCard>(ModelPublicEntryOptions publicEntry) => this;
                    public ModContentPackBuilder Power<TPower>() => this;
                    public ModContentPackBuilder Orb<TOrb>() => this;
                    public ModContentPackBuilder Character<TCharacter>() => this;
                    public ModContentPackBuilder SharedAncient<TAncient>() => this;
                    public ModContentPackBuilder Epoch<TEpoch>() => this;
                    public ModContentPackBuilder Story<TStory>() => this;
                    public ModContentPackBuilder StoryEpoch<TStory, TEpoch>() => this;
                    public ModContentPackBuilder CardKeywordOwnedByLocNamespace(string localKeywordStem, string? iconPath = null) => this;
                    public ModContentPackBuilder KeywordOwned(string localKeywordStem, string titleTable = "card_keywords", string? titleKey = null, string? descriptionTable = null, string? descriptionKey = null) => this;
                    public void Apply() { }
                }
            }

            namespace MegaCrit.Sts2.Core.Models
            {
                public abstract class AbstractModel { }
                public abstract class PowerModel : AbstractModel { }
                public abstract class OrbModel : AbstractModel { }

                public static class ModelDb
                {
                    public static T Power<T>() where T : PowerModel => null!;
                }
            }

            namespace MegaCrit.Sts2.Core.Models.Powers
            {
                public sealed class StrengthPower : PowerModel { }
            }

            namespace MegaCrit.Sts2.Core.Timeline
            {
                public abstract class EpochModel
                {
                    public abstract string Id { get; }
                }

                public abstract class StoryModel
                {
                    protected abstract string Id { get; }
                    public abstract EpochModel[] Epochs { get; }
                }
            }

            namespace STS2RitsuLib.Timeline
            {
                public sealed class ModTimelineRegistry
                {
                    public static ModTimelineRegistry For(string modId) => new();
                    public void RegisterEpoch<TEpoch>() where TEpoch : EpochModel => throw new NotImplementedException();
                    public void RegisterEpoch(Type epochType) { }
                    public void RegisterStory<TStory>() where TStory : StoryModel => throw new NotImplementedException();
                    public void RegisterStory(Type storyType) { }
                    public void RegisterStoryEpoch<TStory, TEpoch>() where TStory : StoryModel where TEpoch : EpochModel => throw new NotImplementedException();
                    public void RegisterStoryEpoch(Type storyType, Type epochType) { }
                }
            }

            namespace MegaCrit.Sts2.Core.Localization.DynamicVars
            {
                public class DynamicVar { }
                public sealed class IntVar : DynamicVar
                {
                    public IntVar(string name, int value) { }
                }
            }

            namespace STS2RitsuLib.Cards.DynamicVars
            {
                public static class DynamicVarExtensions
                {
                    public static DynamicVar WithSharedTooltip(this DynamicVar dynamicVar, string entryPrefix, string? iconPath = null) => dynamicVar;
                    public static DynamicVar WithTooltip(this DynamicVar dynamicVar, string titleTable, string titleKey, string? descriptionTable = null, string? descriptionKey = null, string? iconPath = null) => dynamicVar;
                }
            }

            namespace STS2RitsuLib.Combat.Powers
            {
                public abstract class ModTemporaryPowerTemplate : ModPowerTemplate
                {
                    public abstract AbstractModel OriginModel { get; }
                    public abstract PowerModel InternallyAppliedPower { get; }
                }
            }

            namespace STS2RitsuLib.Keywords
            {
                public sealed class ModKeywordRegistry
                {
                    public static ModKeywordRegistry For(string modId) => new();
                    public void RegisterOwned(string localKeywordStem, string titleTable = "card_keywords", string? titleKey = null, string? descriptionTable = null, string? descriptionKey = null) { }
                    public void RegisterCardKeywordOwnedByLocNamespace(string localKeywordStem, string? iconPath = null) { }
                }
            }

            namespace STS2RitsuLib.CardPiles
            {
                public sealed class ModCardPileRegistry
                {
                    public static ModCardPileRegistry For(string modId) => new();
                    public void RegisterOwned(string localStem, ModCardPileSpec spec) { }
                }

                public sealed class ModCardPileSpec { }
            }

            namespace STS2RitsuLib.TopBar
            {
                public sealed class ModTopBarButtonRegistry
                {
                    public static ModTopBarButtonRegistry For(string modId) => new();
                    public void RegisterOwned(string localStem, ModTopBarButtonSpec spec) { }
                }

                public sealed class ModTopBarButtonSpec { }
            }

            namespace STS2RitsuLib.Utils
            {
                public sealed class I18N
                {
                    public string Get(string key, string fallback) => fallback;
                    public bool TryGet(string key, out string value) { value = ""; return false; }
                    public bool ContainsKey(string key) => false;
                }
            }

            namespace STS2RitsuLib.Localization
            {
                public static class AncientDialogueLocalization
                {
                    public static void GetDialoguesForKey(string locTable, string baseKey) { }
                    public static void BuildDialogueSetForModAncient(string ancientEntry) { }
                }
            }

            namespace STS2RitsuLib.Settings
            {
                public abstract class ModSettingsText
                {
                    public static ModSettingsText I18N(I18N localization, string key, string fallback) => null!;
                    public static ModSettingsText LocString(string table, string key, string fallback) => null!;
                }

                public sealed class ModSettingsPageBuilder
                {
                    public ModSettingsSectionBuilder AddSection(string id, string? title = null) => new();
                }

                public sealed class ModSettingsSectionBuilder
                {
                    public ModSettingsSectionBuilder AddToggle(string id, bool defaultValue = false) => this;
                    public ModSettingsSectionBuilder AddSlider(string id, string title, string description, double minValue, double maxValue, double step = 1) => this;
                    public ModSettingsSectionBuilder AddChoice(string id, string title, string description, string[] options) => this;
                    public ModSettingsSectionBuilder AddString(string id, string title = "") => this;
                    public ModSettingsSectionBuilder AddButton(string id, string title = "") => this;
                    public ModSettingsSectionBuilder AddSubpage(string id, string title, string targetPageId) => this;
                }

                public sealed class ModSettingsBindingAttribute : Attribute
                {
                    public string? ReadUsing { get; set; }
                    public string? WriteUsing { get; set; }
                    public string? SaveUsing { get; set; }
                }

                public sealed class ModSettingsButtonAttribute : Attribute
                {
                    public ModSettingsButtonAttribute(string id, string sectionId) { }
                    public bool UseHostContext { get; set; }
                }
            }

            namespace STS2RitsuLib.Data
            {
                public sealed class ModDataStore
                {
                    public static void Register<T>(string key, string fileName, object? scope = null, object? defaults = null, object? serializer = null, object? migrationConfig = null, object? migrations = null) { }
                }
            }

            namespace STS2RitsuLib.Patching.Core
            {
                public sealed class ModPatcher { }
            }

            namespace STS2RitsuLib.Patching.Models
            {
                public interface IPatchMethod { }
                public interface IModPatches { }
                public sealed record ModPatchTarget(Type TargetType, string MethodName);
            }

            namespace STS2RitsuLib.Patching.Builders
            {
                public sealed class DynamicPatchBuilder
                {
                    public static void FromMethod(Type patchType, string methodName) { }
                    public DynamicPatchBuilder AddMethod(Type targetType, string methodName) => this;
                    public DynamicPatchBuilder AddPropertyGetter(Type targetType, string propertyName) => this;
                }
            }

            namespace STS2RitsuLib.RuntimeInput
            {
                public sealed class RuntimeHotkeyOptions
                {
                    public string Id { get; set; } = "";
                }

                public static class RuntimeHotkeyService
                {
                    public static void Register(string bindingText) { }
                    public static void Register(string[] bindings, Action callback, RuntimeHotkeyOptions? options = null) { }
                }
            }

            namespace STS2RitsuLib.Audio
            {
                public interface IGameAudio
                {
                    AudioLoopHandle? PlayLoop(AudioSource source);
                    AudioMusicHandle? PlayMusic(AudioSource source);
                    AudioScopeToken CreateManualScope(string name);
                    AudioAdaptiveMusicHandle FollowAdaptiveMusic(object plan);
                }

                public abstract class AudioSource
                {
                    public static StudioEventSource Event(string path) => new();
                    public static SnapshotSource Snapshot(string path) => new();
                    public static StudioGuidSource Guid(string guid) => new();
                }

                public class StudioEventSource : AudioSource { }
                public class SnapshotSource : AudioSource { }
                public class StudioGuidSource : AudioSource { }

                public class AudioLoopHandle : IDisposable
                {
                    public void Dispose() { }
                }

                public class AudioMusicHandle : IDisposable
                {
                    public void Dispose() { }
                }

                public class AudioScopeToken : IDisposable
                {
                    public void Dispose() { }
                }

                public class AudioAdaptiveMusicHandle : IDisposable
                {
                    public void Dispose() { }
                }
            }

            namespace STS2RitsuLib.Interop
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class ModInteropAttribute : Attribute
                {
                    public ModInteropAttribute(string targetModId) { }
                }

                [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
                public sealed class InteropTargetAttribute : Attribute
                {
                    public InteropTargetAttribute() { }
                    public InteropTargetAttribute(string name) { }
                }
            }

            namespace STS2RitsuLib.Scaffolding.Characters
            {
                public abstract class ModCharacterTemplate
                {
                    public virtual System.Collections.Generic.IEnumerable<System.Type> StartingDeckTypes => System.Array.Empty<System.Type>();
                    public virtual System.Collections.Generic.IEnumerable<System.Type> StartingRelicTypes => System.Array.Empty<System.Type>();
                    public virtual System.Collections.Generic.IEnumerable<System.Type> StartingPotionTypes => System.Array.Empty<System.Type>();
                }
            }

            namespace ManosabaLin
            {
                public class CardPool { }
                public class MyCard { }
                public class MyPower { }
            }

            namespace Godot
            {
                public class GodotObject { }
                public class Node : GodotObject { }
                public class Control : Node { }
            }
            """;
    }

    private static string Indent(string text, int spaces)
    {
        var prefix = new string(' ', spaces);
        return string.Join(Environment.NewLine, text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Select(line => prefix + line));
    }

    private static IDisposable UseCulture(string cultureName)
    {
        return new CultureScope(cultureName);
    }

    private static AdditionalText AdditionalJson(string path, string json)
    {
        return new InMemoryAdditionalText(path, json);
    }

    private static AdditionalFileSpec AdditionalFile(string path, string text)
    {
        return new(path, text);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, params AdditionalText[] additionalTexts)
    {
        var compilation = CreateCompilation(source);
        return await AnalyzeCompilationAsync(compilation, additionalTexts.ToImmutableArray());
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeProjectAsync(Project project)
    {
        var projectDirectory = project.FilePath == null ? @"C:\mod" : Path.GetDirectoryName(project.FilePath) ?? @"C:\mod";
        return await AnalyzeProjectAsync(project, projectDirectory);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeProjectAsync(Project project, string projectDirectory)
    {
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);

        var additionalTexts = project.AdditionalDocuments
            .Select(document => new AdditionalDocumentText(document))
            .Cast<AdditionalText>()
            .ToImmutableArray();

        return await AnalyzeCompilationAsync(
            compilation!,
            additionalTexts,
            new InMemoryAnalyzerConfigOptionsProvider(projectDirectory));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeCompilationAsync(
        Compilation compilation,
        ImmutableArray<AdditionalText> additionalTexts,
        AnalyzerConfigOptionsProvider? analyzerConfigOptionsProvider = null)
    {
        var analyzer = new AnalyzerUnderTest();
        var options = new AnalyzerOptions(
            additionalTexts,
            analyzerConfigOptionsProvider ?? new InMemoryAnalyzerConfigOptionsProvider(null));
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
            new CompilationWithAnalyzersOptions(options, onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        return CSharpCompilation.Create(
            "AnalyzerTests",
            new[] { syntaxTree },
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Project CreateProject(string source, params AdditionalFileSpec[] additionalFiles)
    {
        return CreateProject(source, CreateTemporaryProjectDirectory(), additionalFiles);
    }

    private static Project CreateProject(string source, string projectDirectory, params AdditionalFileSpec[] additionalFiles)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "AnalyzerTests", "AnalyzerTests", LanguageNames.CSharp)
            .WithProjectFilePath(projectId, Path.Combine(projectDirectory, "AnalyzerTests.csproj"))
            .WithProjectParseOptions(projectId, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        foreach (var reference in References())
            solution = solution.AddMetadataReference(projectId, reference);

        solution = solution.AddDocument(documentId, "Test.cs", SourceText.From(source, Encoding.UTF8), filePath: Path.Combine(projectDirectory, "Test.cs"));

        foreach (var additionalFile in additionalFiles)
        {
            var additionalDocumentId = DocumentId.CreateNewId(projectId, Path.GetFileName(additionalFile.Path));
            solution = solution.AddAdditionalDocument(
                additionalDocumentId,
                Path.GetFileName(additionalFile.Path),
                SourceText.From(additionalFile.Text, Encoding.UTF8),
                filePath: additionalFile.Path);
        }

        Assert.True(workspace.TryApplyChanges(solution));
        return workspace.CurrentSolution.GetProject(projectId)!;
    }

    private static IEnumerable<MetadataReference> References()
    {
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    private static async Task<Solution> ApplyCodeFixAsync(Project project, Diagnostic diagnostic, string titlePrefix)
    {
        var actions = await GetCodeActionsAsync(project, diagnostic);
        var exactAction = actions.SingleOrDefault(action => string.Equals(action.Title, titlePrefix, StringComparison.Ordinal));
        var action = exactAction ?? Assert.Single(actions, action => action.Title.StartsWith(titlePrefix, StringComparison.Ordinal));
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var applyChanges = Assert.Single(operations.OfType<ApplyChangesOperation>());
        return applyChanges.ChangedSolution;
    }

    private static async Task<List<CodeAction>> GetCodeActionsAsync(Project project, Diagnostic diagnostic)
    {
        var document = GetDocumentForDiagnostic(project, diagnostic);
        List<CodeAction> actions = new();
        var provider = new RitsuLibLocalizationCodeFixProvider();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await provider.RegisterCodeFixesAsync(context);
        return actions;
    }

    private static Document GetDocumentForDiagnostic(Project project, Diagnostic diagnostic)
    {
        if (diagnostic.Location.IsInSource &&
            !string.IsNullOrWhiteSpace(diagnostic.Location.SourceTree?.FilePath))
        {
            var document = project.Documents.SingleOrDefault(candidate =>
                string.Equals(candidate.FilePath, diagnostic.Location.SourceTree!.FilePath, StringComparison.OrdinalIgnoreCase));
            if (document != null)
                return document;
        }

        return Assert.Single(project.Documents);
    }

    private static int ExtractCaret(ref string source)
    {
        var position = source.IndexOf("$$", StringComparison.Ordinal);
        Assert.True(position >= 0);
        source = source.Remove(position, "$$".Length);
        return position;
    }

    private static async Task<(RitsuLibResourcePathCompletionProvider Provider, Document Document, ImmutableArray<CompletionItem> Items)>
        GetResourcePathCompletionItemsAsync(Project project, int position)
    {
        var document = Assert.Single(project.Documents);
        var provider = new RitsuLibResourcePathCompletionProvider();
        var items = await RitsuLibResourcePathCompletionProvider.GetCompletionItemsAsync(document, position, CancellationToken.None);
        return (provider, document, items);
    }

    private static async Task<string> GetAdditionalTextAsync(Solution solution, string path)
    {
        var document = solution.Projects
            .SelectMany(project => project.AdditionalDocuments)
            .Single(document => string.Equals(document.FilePath, path, StringComparison.OrdinalIgnoreCase));
        return (await document.GetTextAsync()).ToString();
    }

    private static async Task<string> GetDocumentTextAsync(Solution solution)
    {
        var document = solution.Projects.SelectMany(project => project.Documents).Single();
        return (await document.GetTextAsync()).ToString();
    }

    private static async Task<string> GetDocumentTextAsync(Solution solution, string fileName)
    {
        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .Single(candidate => string.Equals(candidate.Name, fileName, StringComparison.OrdinalIgnoreCase));
        return (await document.GetTextAsync()).ToString();
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string CreateTemporaryProjectDirectory(params string[] languages)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RitsuLibAnalyzerTests", Guid.NewGuid().ToString("N"));
        foreach (var language in languages)
            Directory.CreateDirectory(Path.Combine(directory, "localization", language));

        return directory;
    }

    private sealed class FixAllDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly ImmutableArray<Diagnostic> _diagnostics;

        public FixAllDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);
        }

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(
                _diagnostics.Where(d => d.Location.IsInSource && d.Location.SourceTree?.FilePath == document.FilePath));
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(
                _diagnostics.Where(d => !d.Location.IsInSource));
        }
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return _text;
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

    private sealed class InMemoryAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _globalOptions;

        public InMemoryAnalyzerConfigOptionsProvider(string? projectDirectory)
        {
            Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(projectDirectory))
                options["build_property.MSBuildProjectDirectory"] = projectDirectory!;

            _globalOptions = new InMemoryAnalyzerConfigOptions(options);
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
            new InMemoryAnalyzerConfigOptions(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class InMemoryAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _options;

        public InMemoryAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, out string value)
        {
            return _options.TryGetValue(key, out value!);
        }
    }

    private readonly record struct AdditionalFileSpec(string Path, string Text);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture;
        private readonly CultureInfo _previousUiCulture;

        public CultureScope(string cultureName)
        {
            _previousCulture = CultureInfo.CurrentCulture;
            _previousUiCulture = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }

    private static DiagnosticDescriptor GetSupportedDiagnostic(string id)
    {
        return new AnalyzerUnderTest().SupportedDiagnostics.Single(diagnostic => diagnostic.Id == id);
    }
}
