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
    public void SupportsFullDiagnosticSuite()
    {
        using var culture = UseCulture("en-US");
        var analyzer = new AnalyzerUnderTest();
        var descriptors = analyzer.SupportedDiagnostics.ToDictionary(descriptor => descriptor.Id, StringComparer.Ordinal);
        Assert.Equal(25, descriptors.Count);

        var descriptor = descriptors[AnalyzerUnderTest.MissingLocalizationId];
        Assert.Equal("Missing RitsuLib localization", descriptor.Title.ToString(CultureInfo.CurrentUICulture));
        Assert.Equal("RitsuLib localization keys should exist in the matching language JSON.", descriptor.Description.ToString(CultureInfo.CurrentUICulture));
        Assert.DoesNotContain(WellKnownDiagnosticTags.CompilationEnd, descriptor.CustomTags);

        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU002"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Error, descriptors["RITSU003"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Error, descriptors["RITSU004"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU005"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Error, descriptors["RITSU006"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Error, descriptors["RITSU007"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Info, descriptors["RITSU008"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU009"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU010"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Error, descriptors["RITSU011"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Error, descriptors["RITSU012"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU013"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU014"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU015"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Info, descriptors["RITSU016"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU017"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Error, descriptors["RITSU018"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU019"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU020"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Info, descriptors["RITSU021"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU022"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU023"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU024"].DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, descriptors["RITSU025"].DefaultSeverity);
    }

    [Fact]
    public void LocalizesDescriptorTextForChineseCulture()
    {
        using var culture = UseCulture("zh-CN");
        var descriptor = new AnalyzerUnderTest().SupportedDiagnostics.Single(diagnostic => diagnostic.Id == AnalyzerUnderTest.MissingLocalizationId);

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
    public async Task InvalidJsonStillOffersJsonAndSnippetFixes()
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

        Assert.Contains(actions, action => action.Title.StartsWith("Add missing", StringComparison.Ordinal));
        Assert.Contains(actions, action => action.Title.StartsWith("Insert localization", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsManifestDependencyAndModIdMismatch()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void BuildPack()
                {
                    RitsuLibFramework.CreateContentPack("OtherMod").Apply();
                }
                """),
            AdditionalJson(@"C:\mod\mod_manifest.json", """
            {
              "id": "ManosabaLin",
              "dependencies": []
            }
            """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU002");
        Assert.Contains(diagnostics, d => d.Id == "RITSU003" && d.GetMessage().Contains("OtherMod"));
    }

    [Fact]
    public async Task ReportsMissingRegistrationAndGodotScripts()
    {
        var diagnostics = await AnalyzeAsync(
            SourceWithoutRegistration("""
                public sealed class CardPool { }

                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }

                public sealed class SceneNode : Godot.Node { }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU004");
        Assert.Contains(diagnostics, d => d.Id == "RITSU005");
    }

    [Fact]
    public async Task ReportsContentIdentityIdShapeAndLegacyPoolDiagnostics()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class CardPool { }

                [RegisterCard(typeof(CardPool), FullPublicEntry = "same_entry")]
                public sealed class FirstCard { }

                [RegisterCard(typeof(CardPool), FullPublicEntry = "same_entry")]
                public sealed class SecondCard { }

                public abstract class LegacyPoolBase
                {
                    public virtual System.Collections.Generic.IEnumerable<Type> CardTypes => Array.Empty<Type>();
                }

                public sealed class LegacyPool : LegacyPoolBase
                {
                    public override System.Collections.Generic.IEnumerable<Type> CardTypes => Array.Empty<Type>();
                }

                public static void RegisterIds()
                {
                    ModKeywordRegistry.For(MainFile.ModId).RegisterOwned("Bad Stem");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU007");
        Assert.Contains(diagnostics, d => d.Id == "RITSU008" && d.GetMessage().Contains("Bad Stem"));
        Assert.Contains(diagnostics, d => d.Id == "RITSU016");
    }

    [Fact]
    public async Task ReportsContentPackApplySettingsAndDataStoreDiagnostics()
    {
        using var culture = UseCulture("en-US");
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class CardPool { }
                public sealed class MissingApplyCard { }

                [ModSettingsBinding(ReadUsing = "MissingRead")]
                public sealed class SettingsModel { }

                public static void Build()
                {
                    RitsuLibFramework.CreateContentPack(MainFile.ModId)
                        .Card<CardPool, MissingApplyCard>();

                    RitsuLibFramework.RegisterModSettings(MainFile.ModId, "Settings", "main")
                        .AddSection("general")
                        .AddSlider("volume", "Volume", "", 10, 1, 0);

                    RitsuLibFramework.RegisterModSettings(MainFile.ModId, "Settings", "main")
                        .AddSection("general")
                        .AddChoice("mode", "Mode", "", new string[] { });

                    ModDataStore.Register<int>("bad key", "save.txt");
                    ModDataStore.Register<int>("bad key", "save.txt");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU006");
        Assert.Contains(diagnostics, d => d.Id == "RITSU009" && d.GetMessage().Contains("MissingRead"));
        Assert.Contains(diagnostics, d => d.Id == "RITSU009" && d.GetMessage().Contains("min must be less"));
        Assert.Contains(diagnostics, d => d.Id == "RITSU009" && d.GetMessage().Contains("at least one option"));
        Assert.Contains(diagnostics, d => d.Id == "RITSU010" && d.GetMessage().Contains("save.txt"));
        Assert.Contains(diagnostics, d => d.Id == "RITSU010" && d.GetMessage().Contains("Duplicate ModDataStore key"));
    }

    [Fact]
    public async Task ReportsPatchResourceAudioAndRuntimeDiagnostics()
    {
        using var culture = UseCulture("en-US");
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class PatchOwner : IPatchMethod { }
                public sealed class PatchGroup : IModPatches { }
                public sealed class TargetType { }

                public static void Build()
                {
                    DynamicPatchBuilder.FromMethod(typeof(PatchOwner), "MissingPatchMethod");
                    new DynamicPatchBuilder().AddMethod(typeof(TargetType), "MissingTargetMethod");
                    new DynamicPatchBuilder().AddPropertyGetter(typeof(TargetType), "MissingProperty");

                    RitsuLibFramework.LoadIcon("assets/icon.png");
                    RitsuLibFramework.RegisterBank("soundtrack");
                    RitsuLibFramework.PlayEvent("foo:/bad_event");
                    RitsuLibFramework.SetBus("foo:/bad_bus");
                    RitsuLibFramework.SetGuid("not-a-guid");

                    RitsuLibFramework.RegisterFreePlayBinding("");
                    RuntimeHotkeyService.Register("Ctrl++");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU011");
        Assert.Contains(diagnostics, d => d.Id == "RITSU012");
        Assert.Contains(diagnostics, d => d.Id == "RITSU013");
        Assert.Contains(diagnostics, d => d.Id == "RITSU014");
        Assert.Contains(diagnostics, d => d.Id == "RITSU015");
    }

    [Fact]
    public async Task ResPathAtProjectRootIsFoundInAdditionalFiles()
    {
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://icon.svg");
                }
                """),
            AdditionalFile(@"C:\mod\icon.svg", "<svg/>"));

        var diagnostics = await AnalyzeProjectAsync(project);
        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU013");
    }

    [Fact]
    public async Task ResPathUsesProjectRootRelativeSubpath()
    {
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://Resources/sub/icon.png");
                }
                """),
            AdditionalFile(@"C:\mod\Resources\sub\icon.png", ""));

        var diagnostics = await AnalyzeProjectAsync(project);
        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU013");
    }

    [Fact]
    public async Task ResPathDoesNotMatchResourceMarkerRelativeSubpath()
    {
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://sub/icon.png");
                }
                """),
            AdditionalFile(@"C:\mod\Resources\sub\icon.png", ""));

        var diagnostics = await AnalyzeProjectAsync(project);
        Assert.Contains(diagnostics, d => d.Id == "RITSU013");
    }

    [Fact]
    public async Task ResPathDoesNotMatchByFileNameOnly()
    {
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://icon.svg");
                }
                """),
            AdditionalFile(@"C:\mod\assets\icon.svg", "<svg/>"));

        var diagnostics = await AnalyzeProjectAsync(project);
        Assert.Contains(diagnostics, d => d.Id == "RITSU013");
    }

    [Fact]
    public async Task ApplyCodeFixAddsApplyToContentPackChain()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public sealed class CardPool { }
                public sealed class MissingApplyCard { }

                public static void Build()
                {
                    RitsuLibFramework.CreateContentPack(MainFile.ModId)
                        .Card<CardPool, MissingApplyCard>();
                }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU006"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add .Apply()");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains(".Apply()", text);
    }

    [Fact]
    public async Task RegistrationCodeFixesInsertInitializerStatements()
    {
        using var culture = UseCulture("en-US");
        var registrationProject = CreateProject(
            SourceWithoutRegistration("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """));
        var registrationDiagnostic = Assert.Single((await AnalyzeProjectAsync(registrationProject)).Where(d => d.Id == "RITSU004"));
        var registrationChanged = await ApplyCodeFixAsync(registrationProject, registrationDiagnostic, "Insert RegisterModAssembly");
        var registrationText = await GetDocumentTextAsync(registrationChanged);
        Assert.Contains("STS2RitsuLib.Interop.ModTypeDiscoveryHub.RegisterModAssembly", registrationText);

        var godotProject = CreateProject(
            SourceWithoutRegistration("""
                public sealed class SceneNode : Godot.Node { }
                """));
        var godotDiagnostic = Assert.Single((await AnalyzeProjectAsync(godotProject)).Where(d => d.Id == "RITSU005"));
        var godotChanged = await ApplyCodeFixAsync(godotProject, godotDiagnostic, "Insert EnsureGodotScriptsRegistered");
        var godotText = await GetDocumentTextAsync(godotChanged);
        Assert.Contains("STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered", godotText);
    }

    [Fact]
    public async Task SettingsCodeFixAddsCallbackStub()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [ModSettingsBinding(ReadUsing = "MissingRead")]
                public sealed class SettingsModel { }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU009" && d.GetMessage().Contains("MissingRead")));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Generate settings");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("private static void MissingRead()", text);
    }

    [Fact]
    public async Task PatchCodeFixesAddRequiredMembersAndTargetStub()
    {
        using var culture = UseCulture("en-US");
        var patchProject = CreateProject(
            Source("""
                public sealed class PatchOwner : IPatchMethod { }
                """));
        var patchDiagnostic = (await AnalyzeProjectAsync(patchProject)).First(d => d.Id == "RITSU011");
        var patchChanged = await ApplyCodeFixAsync(patchProject, patchDiagnostic, "Generate required patch");
        var patchText = await GetDocumentTextAsync(patchChanged);
        Assert.Contains("public static string PatchId", patchText);
        Assert.Contains("public static STS2RitsuLib.Patching.Models.ModPatchTarget[] GetTargets()", patchText);

        var targetProject = CreateProject(
            Source("""
                public sealed class TargetType { }

                public static void Build()
                {
                    new DynamicPatchBuilder().AddPropertyGetter(typeof(TargetType), "MissingProperty");
                }
                """));
        var targetDiagnostic = Assert.Single((await AnalyzeProjectAsync(targetProject)).Where(d => d.Id == "RITSU012"));
        var targetChanged = await ApplyCodeFixAsync(targetProject, targetDiagnostic, "Generate required patch");
        var targetText = await GetDocumentTextAsync(targetChanged);
        Assert.Contains("public object? MissingProperty => null;", targetText);
    }

    [Fact]
    public async Task TodoFallbackCodeFixInsertsDiagnosticSnippet()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public static void UseRuntime()
                {
                    RuntimeHotkeyService.Register("Ctrl++");
                }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU015"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Insert RitsuLib TODO");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("TODO RitsuLib analyzer:", text);
    }

    [Fact]
    public async Task HotkeyArrayBindingDoesNotTriggerRitsu015()
    {
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RuntimeHotkeyService.Register(
                        ["F5", "Ctrl+Shift+R"],
                        () => { },
                        new RuntimeHotkeyOptions { Id = "my_mod_refresh" });
                }
                """));

        var diagnostics = await AnalyzeProjectAsync(project);
        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU015");
    }

    [Fact]
    public async Task HotkeyArrayWithInvalidElementTriggersRitsu015()
    {
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RuntimeHotkeyService.Register(
                        ["F5", "Ctrl++"],
                        () => { },
                        new RuntimeHotkeyOptions { Id = "my_mod_refresh" });
                }
                """));

        var diagnostics = await AnalyzeProjectAsync(project);
        var ritsu015 = Assert.Single(diagnostics.Where(d => d.Id == "RITSU015"));
        Assert.Contains("Ctrl++", ritsu015.GetMessage());
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

    // RITSU017: Disposable handle not disposed
    [Fact]
    public async Task ReportsUndisposedAudioHandle()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void PlayAudio(IGameAudio audio)
                {
                    audio.PlayLoop(AudioSource.Event("event:/sfx/test"));
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU017" && d.GetMessage().Contains("PlayLoop"));
    }

    [Fact]
    public async Task DoesNotReportWhenHandleIsAssigned()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void PlayAudio(IGameAudio audio)
                {
                    var handle = audio.PlayLoop(AudioSource.Event("event:/sfx/test"));
                }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU017");
    }

    [Fact]
    public async Task ReportsUndisposedSubscribeLifecycle()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void Subscribe()
                {
                    RitsuLibFramework.SubscribeLifecycle<int>(e => { });
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU017" && d.GetMessage().Contains("SubscribeLifecycle"));
    }

    // RITSU018: ContentPackBuilder.For() not applied
    [Fact]
    public async Task ReportsContentPackBuilderForNotApplied()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void RegisterContent()
                {
                    ModContentPackBuilder.For(MainFile.ModId)
                        .Card<CardPool, MyCard>()
                        .Power<MyPower>();
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU018");
    }

    [Fact]
    public async Task DoesNotReportContentPackBuilderForWithApply()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void RegisterContent()
                {
                    ModContentPackBuilder.For(MainFile.ModId)
                        .Card<CardPool, MyCard>()
                        .Apply();
                }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU018");
    }

    // RITSU019: AudioSource path shape
    [Fact]
    public async Task ReportsAudioSourceEventMissingPrefix()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void PlayAudio()
                {
                    AudioSource.Event("sfx/block");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU019" && d.GetMessage().Contains("event:/"));
    }

    [Fact]
    public async Task DoesNotReportAudioSourceEventWithPrefix()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void PlayAudio()
                {
                    AudioSource.Event("event:/sfx/block");
                }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU019");
    }

    [Fact]
    public async Task ReportsAudioSourceSnapshotMissingPrefix()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void PlaySnapshot()
                {
                    AudioSource.Snapshot("snapshot:/music/pause");
                }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU019");
    }

    // RITSU020: ModInterop attribute shape
    [Fact]
    public async Task ReportsModInteropEmptyModId()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [ModInterop("")]
                public sealed class MyInterop { }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU020");
    }

    [Fact]
    public async Task DoesNotReportModInteropWithValidId()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [ModInterop("com.example.other-mod")]
                public sealed class MyInterop { }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU020");
    }

    // RITSU021: Character template legacy override
    [Fact]
    public async Task ReportsCharacterTemplateLegacyOverride()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public class MyCharacter : ModCharacterTemplate
                {
                    public override System.Collections.Generic.IEnumerable<System.Type> StartingDeckTypes => System.Array.Empty<System.Type>();
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU021");
    }

    // RITSU022: Settings subpage reference
    [Fact]
    public async Task ReportsSettingsSubpageReferenceToMissingPage()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void BuildSettings()
                {
                    RitsuLibFramework.RegisterModSettings(MainFile.ModId, "My Settings", page =>
                    {
                        page.AddSection("general", section =>
                        {
                            section.AddSubpage("link", "Link", "nonexistent_page");
                        });
                    });
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU022" && d.GetMessage().Contains("nonexistent_page"));
    }

    // RITSU025: Lifecycle event type constraint
    [Fact]
    public async Task ReportsLifecycleEventNonSealedType()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public class MyEvent { }
                public static void Subscribe()
                {
                    RitsuLibFramework.SubscribeLifecycleOnce<MyEvent>(e => { });
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU025");
    }

    [Fact]
    public async Task DoesNotReportLifecycleEventSealedType()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class MyEvent { }
                public static void Subscribe()
                {
                    RitsuLibFramework.SubscribeLifecycleOnce<MyEvent>(e => { });
                }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU025");
    }

    // Code fix tests for new diagnostics
    [Fact]
    public async Task DisposableCodeFixWrapsInUsing()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public static void PlayAudio(IGameAudio audio)
                {
                    audio.PlayLoop(AudioSource.Event("event:/sfx/test"));
                }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU017"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Wrap in using");
        var text = await GetDocumentTextAsync(changed);
        Assert.Contains("using", text);
        Assert.Contains("_playLoop", text);
    }

    [Fact]
    public async Task ContentPackBuilderForCodeFixAddsApply()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public static void RegisterContent()
                {
                    ModContentPackBuilder.For(MainFile.ModId)
                        .Card<CardPool, MyCard>();
                }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU018"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add .Apply()");
        var text = await GetDocumentTextAsync(changed);
        Assert.Contains(".Apply()", text);
    }

    [Fact]
    public async Task AudioSourcePathCodeFixAddsPrefix()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public static void PlayAudio()
                {
                    AudioSource.Event("sfx/block");
                }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU019"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add event:/");
        var text = await GetDocumentTextAsync(changed);
        Assert.Contains("event:/sfx/block", text);
    }

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
            using STS2RitsuLib;
            using STS2RitsuLib.Audio;
            using STS2RitsuLib.CardPiles;
            using STS2RitsuLib.Scaffolding.Characters;
            using STS2RitsuLib.Content;
            using STS2RitsuLib.Interop;
            using STS2RitsuLib.Interop.AutoRegistration;
            using STS2RitsuLib.Keywords;
            using STS2RitsuLib.Localization;
            using STS2RitsuLib.Data;
            using STS2RitsuLib.Patching.Builders;
            using STS2RitsuLib.Patching.Core;
            using STS2RitsuLib.Patching.Models;
            using STS2RitsuLib.RuntimeInput;
            using STS2RitsuLib.Scaffolding.Content;
            using STS2RitsuLib.Settings;
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
                    public static ModContentPackBuilder For(string modId) => new();
                    public ModContentPackBuilder Card<TPool, TCard>() => this;
                    public ModContentPackBuilder Card<TPool, TCard>(ModelPublicEntryOptions publicEntry) => this;
                    public ModContentPackBuilder Power<TPower>() => this;
                    public ModContentPackBuilder Character<TCharacter>() => this;
                    public ModContentPackBuilder SharedAncient<TAncient>() => this;
                    public ModContentPackBuilder CardKeywordOwnedByLocNamespace(string localKeywordStem, string? iconPath = null) => this;
                    public ModContentPackBuilder KeywordOwned(string localKeywordStem, string titleTable = "card_keywords", string? titleKey = null, string? descriptionTable = null, string? descriptionKey = null) => this;
                    public void Apply() { }
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
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);

        var additionalTexts = project.AdditionalDocuments
            .Select(document => new AdditionalDocumentText(document))
            .Cast<AdditionalText>()
            .ToImmutableArray();

        return await AnalyzeCompilationAsync(
            compilation!,
            additionalTexts,
            new InMemoryAnalyzerConfigOptionsProvider(@"C:\mod"));
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

    [Fact]
    public async Task JsonCodeFixAddsKeysToTargetLanguageFile()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\cards.json", "{}"),
            AdditionalFile(@"C:\mod\localization\zhs\cards.json", "{}"));

        var diagnostics = (await AnalyzeProjectAsync(project))
            .Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId)
            .ToImmutableArray();

        Assert.True(diagnostics.Length >= 2);

        var engDiagnostic = diagnostics.First(d => d.GetMessage().Contains("eng/"));
        var changed = await ApplyCodeFixAsync(project, engDiagnostic, "Add missing");

        var engText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");

        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engText);
    }

    [Fact]
    public async Task JsonCodeFixAddsKeysToAllLanguagesFromSingleDiagnostic()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\cards.json", "{}"),
            AdditionalFile(@"C:\mod\localization\zhs\cards.json", "{}"));

        var diagnostic = (await AnalyzeProjectAsync(project))
            .First(d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("eng/"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing");

        var engText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");
        var zhsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\zhs\cards.json");

        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsText);
    }

    [Fact]
    public async Task JsonCodeFixAddsKeysToMultipleTargetFilesFromSingleDiagnostic()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public static void Register()
                {
                    ModKeywordRegistry.For(MainFile.ModId).RegisterOwned("hiro");
                }

                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\cards.json", "{}"),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", "{}"));

        var diagnostic = (await AnalyzeProjectAsync(project))
            .First(d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("cards.json"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing");

        var cardsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");
        var keywordsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", cardsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", cardsText);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", keywordsText);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", keywordsText);
    }

    [Fact]
    public async Task JsonCodeFixSkipsExistingKeysWhenAddingMissingKeys()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\cards.json", """
            {
              "MANOSABA_LIN_CARD_MY_CARD.title": "Existing"
            }
            """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");

        Assert.Equal(1, CountOccurrences(text, "\"MANOSABA_LIN_CARD_MY_CARD.title\""));
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"Existing\"", text);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", text);
    }

    [Fact]
    public async Task JsonCodeFixSkipsInvalidJsonButUpdatesOtherTargets()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public static void Register()
                {
                    ModKeywordRegistry.For(MainFile.ModId).RegisterOwned("hiro");
                }

                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\cards.json", "{ invalid"),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", "{}"));

        var diagnostic = (await AnalyzeProjectAsync(project))
            .First(d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("card_keywords.json"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing");

        var cardsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");
        var keywordsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.Equal("{ invalid", cardsText);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", keywordsText);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", keywordsText);
    }

    [Fact]
    public async Task FixAllProviderReturnsActionForMultipleDiagnostics()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }

                [RegisterCard(typeof(CardPool))]
                public sealed class MyStrike { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\cards.json", "{}"),
            AdditionalFile(@"C:\mod\localization\zhs\cards.json", "{}"));

        var diagnostics = (await AnalyzeProjectAsync(project))
            .Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId)
            .ToImmutableArray();

        Assert.True(diagnostics.Length >= 2);

        var document = Assert.Single(project.Documents);
        var provider = new RitsuLibLocalizationCodeFixProvider();
        var fixAllProvider = provider.GetFixAllProvider();

        var diagnosticIds = diagnostics.Select(d => d.Id).Distinct().ToImmutableArray();
        var fixAllDiagnosticProvider = new FixAllDiagnosticProvider(diagnostics);
        var fixAllContext = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            diagnosticIds[0],
            diagnosticIds,
            fixAllDiagnosticProvider,
            CancellationToken.None);

        var fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext);
        Assert.NotNull(fixAllAction);
        Assert.Contains("2 files", fixAllAction.Title);
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
}
