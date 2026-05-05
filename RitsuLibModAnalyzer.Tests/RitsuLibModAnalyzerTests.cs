using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Nothing.STS2RitsuLib.ModAnalyzers;
using Xunit;
using AnalyzerUnderTest = Nothing.STS2RitsuLib.ModAnalyzers.RitsuLibModAnalyzer;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace RitsuLibModAnalyzer.Tests;

public sealed class RitsuLibModAnalyzerTests
{
    [Fact]
    public void SupportsOnlyMissingLocalizationDiagnostic()
    {
        using var culture = UseCulture("en-US");
        var analyzer = new AnalyzerUnderTest();
        var descriptor = Assert.Single(analyzer.SupportedDiagnostics);
        Assert.Equal(AnalyzerUnderTest.MissingLocalizationId, descriptor.Id);
        Assert.Equal("Missing RitsuLib localization", descriptor.Title.ToString(CultureInfo.CurrentUICulture));
        Assert.Equal("RitsuLib localization keys should exist in the matching language JSON.", descriptor.Description.ToString(CultureInfo.CurrentUICulture));
        Assert.DoesNotContain(WellKnownDiagnosticTags.CompilationEnd, descriptor.CustomTags);
        Assert.DoesNotContain(analyzer.SupportedDiagnostics, diagnostic => diagnostic.Id is "RITSU002" or "RITSU003");
    }

    [Fact]
    public void LocalizesDescriptorTextForChineseCulture()
    {
        using var culture = UseCulture("zh-CN");
        var descriptor = Assert.Single(new AnalyzerUnderTest().SupportedDiagnostics);

        Assert.Equal("缺少 RitsuLib 本地化", descriptor.Title.ToString(CultureInfo.CurrentUICulture));
        Assert.Equal("RitsuLib 引用的本地化键应存在于对应语言 JSON 中。", descriptor.Description.ToString(CultureInfo.CurrentUICulture));
    }

    [Fact]
    public async Task ReportsMissingEnglishCardKeywordLocalizationAsError()
    {
        using var culture = UseCulture("en-US");
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalJson(@"C:\mod\localization\eng\card_keywords.json", "{}"),
            AdditionalJson(@"C:\mod\localization\zhs\card_keywords.json", """
            {
              "MANOSABA_LIN_KEYWORD_HIRO.title": "希罗",
              "MANOSABA_LIN_KEYWORD_HIRO.description": "描述"
            }
            """));

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.Equal(DiagnosticSeverity.Error, missing.Severity);
        Assert.StartsWith("Missing RitsuLib localization keys: eng/card_keywords.json:", missing.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("eng/card_keywords.json", missing.GetMessage());
        Assert.Contains("MANOSABA_LIN_KEYWORD_HIRO.title", missing.GetMessage());
        Assert.Contains("MANOSABA_LIN_KEYWORD_HIRO.description", missing.GetMessage());
    }

    [Fact]
    public async Task ReportsMissingLocalizationMessageInChineseCulture()
    {
        using var culture = UseCulture("zh-CN");
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalJson(@"C:\mod\localization\eng\card_keywords.json", "{}"));

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.StartsWith("缺少 RitsuLib 本地化键: eng/card_keywords.json:", missing.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("MANOSABA_LIN_KEYWORD_HIRO.title", missing.GetMessage());
        Assert.Contains("MANOSABA_LIN_KEYWORD_HIRO.description", missing.GetMessage());
    }

    [Fact]
    public async Task DoesNotReportWhenEveryLanguageHasOwnedKeywordKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            CompleteCardKeywordJson("eng", "MANOSABA_LIN_KEYWORD_HIRO"),
            CompleteCardKeywordJson("zhs", "MANOSABA_LIN_KEYWORD_HIRO"));

        Assert.DoesNotContain(diagnostics, d => d.Id == AnalyzerUnderTest.MissingLocalizationId);
    }

    [Fact]
    public async Task DoesNotReportWithoutDiscoveredLocalizationLanguages()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == AnalyzerUnderTest.MissingLocalizationId);
    }

    [Fact]
    public async Task ReportsCustomKeywordTablesAndKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterOwnedKeyword(
                    "brew",
                    TitleTable = "keyword_names",
                    TitleKey = "{id}.name",
                    DescriptionTable = "keyword_text",
                    DescriptionKey = "{id}.body")]
                private sealed class AttributeKeywordMarker { }

                public static void RegisterKeywords()
                {
                    ModKeywordRegistry.For(MainFile.ModId).RegisterOwned(
                        "spark",
                        titleTable: "keyword_names",
                        titleKey: "{id}.label",
                        descriptionTable: "keyword_text",
                        descriptionKey: "{id}.details");
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\keyword_names.json", "{}"),
            AdditionalJson(@"C:\mod\localization\eng\keyword_text.json", "{}"));

        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("keyword_names.json") &&
            d.GetMessage().Contains("MANOSABA_LIN_KEYWORD_BREW.name"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("keyword_text.json") &&
            d.GetMessage().Contains("MANOSABA_LIN_KEYWORD_SPARK.details"));
    }

    [Fact]
    public async Task RitsuLibOwnedByAttributeOverridesFallbackOwner()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RitsuLibOwnedBy("OtherMod")]
                [RegisterOwnedCardKeyword("brew")]
                private sealed class KeywordMarker { }
                """),
            AdditionalJson(@"C:\mod\localization\eng\card_keywords.json", "{}"));

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.Contains("OTHER_MOD_KEYWORD_BREW.title", missing.GetMessage());
        Assert.DoesNotContain("MANOSABA_LIN_KEYWORD_BREW", missing.GetMessage());
    }

    [Fact]
    public async Task NormalizesAdditionalFileLanguageCodes()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalJson(@"C:\mod\localization\en_us\card_keywords.json", "{}"));

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.Equal("eng", missing.Properties["Language"]);
        Assert.Contains("eng/card_keywords.json", missing.GetMessage());
    }

    [Fact]
    public async Task ReportsCardPileAndTopBarStaticHoverTipKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void RegisterUi()
                {
                    ModCardPileRegistry.For(MainFile.ModId).RegisterOwned("test_pile", new ModCardPileSpec());
                    ModTopBarButtonRegistry.For(MainFile.ModId).RegisterOwned("test_button", new ModTopBarButtonSpec());
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\static_hover_tips.json", "{}"));

        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_CARDPILE_TEST_PILE.empty"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_TOPBARBUTTON_TEST_BUTTON.description"));
    }

    [Fact]
    public async Task ReportsContentModelLocStringKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class CardPool { }
                public sealed class MyStrike { }
                public sealed class MyPower { }

                public static void RegisterContent()
                {
                    RitsuLibFramework.CreateContentPack(MainFile.ModId)
                        .Card<CardPool, MyStrike>()
                        .Power<MyPower>();
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\cards.json", """
            {
              "MANOSABA_LIN_CARD_MY_STRIKE.title": "Strike"
            }
            """),
            AdditionalJson(@"C:\mod\localization\eng\powers.json", "{}"));

        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_CARD_MY_STRIKE.description"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_POWER_MY_POWER.smartDescription"));
    }

    [Fact]
    public async Task ReportsContentModelPublicEntryOverrides()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class CardPool { }
                public sealed class StemCard { }
                public sealed class FullCard { }

                [RegisterCard(typeof(CardPool), StableEntryStem = "attr_stable")]
                public sealed class AttributeStemCard { }

                [RegisterCard(typeof(CardPool), FullPublicEntry = "AttrFullCard")]
                public sealed class AttributeFullCard { }

                public static void RegisterContent()
                {
                    RitsuLibFramework.CreateContentPack(MainFile.ModId)
                        .Card<CardPool, StemCard>(ModelPublicEntryOptions.FromStem("stable_strike"))
                        .Card<CardPool, FullCard>(ModelPublicEntryOptions.FromFullPublicEntry("ExternalCardId"));
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\cards.json", "{}"));

        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_CARD_STABLE_STRIKE.title"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("EXTERNAL_CARD_ID.description"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_CARD_ATTR_STABLE.title"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("ATTR_FULL_CARD.description"));
    }

    [Fact]
    public async Task ReportsI18NHelperKeysAgainstLanguageJson()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void UseI18N()
                {
                    var i18n = RitsuLibFramework.CreateModLocalization(MainFile.ModId, "test");
                    i18n.TryGet("settings.title", out var _);
                    i18n.ContainsKey("settings.description");
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng.json", """
            {
              "settings.title": "Settings"
            }
            """));

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.Contains("eng.json", missing.GetMessage());
        Assert.Contains("settings.description", missing.GetMessage());
    }

    [Fact]
    public async Task ReportsAncientDialogueKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void UseAncientDialogue()
                {
                    AncientDialogueLocalization.GetDialoguesForKey("ancients", "THE_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.");
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\ancients.json", "{}"));

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.Contains("THE_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-0.ancient", missing.GetMessage());
        Assert.Contains("THE_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-0.char", missing.GetMessage());
    }

    [Fact]
    public async Task ReportsRegisteredAncientCharacterDialogueCombination()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterCharacter]
                public sealed class Hero { }

                [RegisterSharedAncient]
                public sealed class Architect { }
                """),
            AdditionalJson(@"C:\mod\localization\eng\ancients.json", "{}"),
            AdditionalJson(@"C:\mod\localization\eng\characters.json", "{}"));

        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_ANCIENT_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-0.ancient"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_ANCIENT_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-0.char"));
    }

    [Fact]
    public async Task JsonCodeFixAddsMissingKeysToExistingAdditionalFile()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", """
            {
              "EXISTING": "ok"
            }
            """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.Contains("\"EXISTING\": \"ok\"", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", text);
    }

    [Fact]
    public async Task JsonCodeFixCreatesMissingTableFile()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public static void RegisterUi()
                {
                    ModCardPileRegistry.For(MainFile.ModId).RegisterOwned("test_pile", new ModCardPileSpec());
                }
                """),
            AdditionalFile(@"C:\mod\localization\eng\cards.json", "{}"));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\static_hover_tips.json");

        Assert.Contains("\"MANOSABA_LIN_CARDPILE_TEST_PILE.title\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_CARDPILE_TEST_PILE.description\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_CARDPILE_TEST_PILE.empty\": \"\"", text);
    }

    [Fact]
    public async Task JsonCodeFixReplacesEmptyJsonWithObject()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", "   "));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.StartsWith("{", text.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", text);
    }

    [Fact]
    public async Task SnippetCodeFixInsertsCopyableJsonComment()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", "{}"));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Insert localization");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("Missing RitsuLib localization:", text);
        Assert.Contains(@"C:\mod\localization\eng\card_keywords.json", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", text);
    }

    [Fact]
    public async Task CodeFixTitlesUseChineseCulture()
    {
        using var culture = UseCulture("zh-CN");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", "{}"));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var actions = await GetCodeActionsAsync(project, diagnostic);

        Assert.Contains(actions, action => action.Title == "添加缺失的本地化键到 card_keywords.json");
        Assert.Contains(actions, action => action.Title == "插入本地化 JSON 片段");
    }

    [Fact]
    public async Task CodeFixTitlesUseEnglishCulture()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", "{}"));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var actions = await GetCodeActionsAsync(project, diagnostic);

        Assert.Contains(actions, action => action.Title == "Add missing localization keys to card_keywords.json");
        Assert.Contains(actions, action => action.Title == "Insert localization JSON snippet");
    }

    [Fact]
    public async Task InvalidJsonOnlyOffersSnippetFix()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", "{ invalid"));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var actions = await GetCodeActionsAsync(project, diagnostic);

        Assert.DoesNotContain(actions, action => action.Title.StartsWith("Add missing", StringComparison.Ordinal));
        Assert.Contains(actions, action => action.Title.StartsWith("Insert localization", StringComparison.Ordinal));
    }

    private static AdditionalText CompleteCardKeywordJson(string language, string id)
    {
        return AdditionalJson($@"C:\mod\localization\{language}\card_keywords.json", $$"""
        {
          "{{id}}.title": "Title",
          "{{id}}.description": "Description"
        }
        """);
    }

    private static string Source(string body)
    {
        return $$"""
            using System;
            using System.Reflection;
            using STS2RitsuLib;
            using STS2RitsuLib.CardPiles;
            using STS2RitsuLib.Content;
            using STS2RitsuLib.Interop;
            using STS2RitsuLib.Interop.AutoRegistration;
            using STS2RitsuLib.Keywords;
            using STS2RitsuLib.Localization;
            using STS2RitsuLib.Scaffolding.Content;
            using STS2RitsuLib.TopBar;
            using STS2RitsuLib.Utils;

            namespace ManosabaLin
            {
                public static class MainFile
                {
                    public const string ModId = "ManosabaLin";

                    public static void Initialize()
                    {
                        ModTypeDiscoveryHub.RegisterModAssembly(ModId, Assembly.GetExecutingAssembly());
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
                public sealed class RegisterCharacterAttribute : ContentRegistrationAttribute { }
                public sealed class RegisterSharedAncientAttribute : ContentRegistrationAttribute { }

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
                    public ModContentPackBuilder Card<TPool, TCard>() => this;
                    public ModContentPackBuilder Card<TPool, TCard>(ModelPublicEntryOptions publicEntry) => this;
                    public ModContentPackBuilder Power<TPower>() => this;
                    public ModContentPackBuilder Character<TCharacter>() => this;
                    public ModContentPackBuilder SharedAncient<TAncient>() => this;
                    public ModContentPackBuilder CardKeywordOwnedByLocNamespace(string localKeywordStem, string? iconPath = null) => this;
                    public ModContentPackBuilder KeywordOwned(string localKeywordStem, string titleTable = "card_keywords", string? titleKey = null, string? descriptionTable = null, string? descriptionKey = null) => this;
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
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);

        var additionalTexts = project.AdditionalDocuments
            .Select(document => new AdditionalDocumentText(document))
            .Cast<AdditionalText>()
            .ToImmutableArray();

        return await AnalyzeCompilationAsync(compilation!, additionalTexts);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeCompilationAsync(
        Compilation compilation,
        ImmutableArray<AdditionalText> additionalTexts)
    {
        var analyzer = new AnalyzerUnderTest();
        var options = new AnalyzerOptions(additionalTexts);
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
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "AnalyzerTests", "AnalyzerTests", LanguageNames.CSharp)
            .WithProjectParseOptions(projectId, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        foreach (var reference in References())
            solution = solution.AddMetadataReference(projectId, reference);

        solution = solution.AddDocument(documentId, "Test.cs", SourceText.From(source, Encoding.UTF8), filePath: @"C:\mod\Test.cs");

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
        var action = Assert.Single(actions, action => action.Title.StartsWith(titlePrefix, StringComparison.Ordinal));
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var applyChanges = Assert.Single(operations.OfType<ApplyChangesOperation>());
        return applyChanges.ChangedSolution;
    }

    private static async Task<List<CodeAction>> GetCodeActionsAsync(Project project, Diagnostic diagnostic)
    {
        var document = Assert.Single(project.Documents);
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
}
