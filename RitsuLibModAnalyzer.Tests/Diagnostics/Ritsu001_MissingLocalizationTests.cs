namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu001DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU001");

        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

    [Fact]
    public void MissingLocalizationDescriptorUsesEnglishCulture()
    {
        using var culture = UseCulture("en-US");
        var descriptor = GetSupportedDiagnostic(AnalyzerUnderTest.MissingLocalizationId);

        Assert.Equal("Missing RitsuLib localization", descriptor.Title.ToString(CultureInfo.CurrentUICulture));
        Assert.Equal("RitsuLib localization keys should exist in the matching language JSON.", descriptor.Description.ToString(CultureInfo.CurrentUICulture));
        Assert.DoesNotContain(WellKnownDiagnosticTags.CompilationEnd, descriptor.CustomTags);
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
    public async Task ReportsMissingEnglishCardKeywordLocalizationAsWarningWhenNonEnglishHasKey()
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
        Assert.Equal(DiagnosticSeverity.Warning, missing.Severity);
        Assert.StartsWith("Missing RitsuLib localization keys: eng/card_keywords.json:", missing.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("eng/card_keywords.json", missing.GetMessage());
        Assert.Contains("MANOSABA_LIN_KEYWORD_HIRO.title", missing.GetMessage());
        Assert.Contains("MANOSABA_LIN_KEYWORD_HIRO.description", missing.GetMessage());
    }

    [Fact]
    public async Task ReportsMissingEnglishCardKeywordLocalizationAsErrorWhenNoOtherLanguageHasKey()
    {
        using var culture = UseCulture("en-US");
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalJson(@"C:\mod\localization\eng\card_keywords.json", "{}"),
            AdditionalJson(@"C:\mod\localization\zhs\card_keywords.json", "{}"));

        var missing = Assert.Single(diagnostics.Where(d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("eng/card_keywords.json")));
        Assert.Equal(DiagnosticSeverity.Error, missing.Severity);
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
            d.GetMessage().Contains("MANOSABA_LIN_POWER_MY_POWER.title"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_POWER_MY_POWER.description"));
        Assert.DoesNotContain(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_POWER_MY_POWER.smartDescription"));
    }

    [Theory]
    [InlineData("HTTPServer2Card", "HTTP_SERVER2_CARD")]
    [InlineData("XML2Reader", "XML2_READER")]
    [InlineData("XMLReader", "XML_READER")]
    [InlineData("My Strike", "MY_STRIKE")]
    [InlineData("AttrFullCard", "ATTR_FULL_CARD")]
    [InlineData("my-mod", "MY_MOD")]
    [InlineData("Manosaba.Lin", "MANOSABA_LIN")]
    public void NormalizePublicStemMatchesGameSlugify(string value, string expected)
    {
        Assert.Equal(expected, RitsuLibSyntaxFacts.NormalizePublicStem(value));
    }

    [Fact]
    public async Task ReportsContentModelKeysWithGameAccurateAcronymNormalization()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class CardPool { }
                public sealed class HTTPServer2Card { }
                public sealed class XML2Reader { }
                public sealed class XMLReader { }

                public static void RegisterContent()
                {
                    RitsuLibFramework.CreateContentPack(MainFile.ModId)
                        .Card<CardPool, HTTPServer2Card>()
                        .Card<CardPool, XML2Reader>()
                        .Card<CardPool, XMLReader>();
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\cards.json", "{}"));

        var message = string.Join("\n", diagnostics
            .Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId)
            .Select(d => d.GetMessage()));

        Assert.Contains("MANOSABA_LIN_CARD_HTTP_SERVER2_CARD.title", message);
        Assert.Contains("MANOSABA_LIN_CARD_XML2_READER.description", message);
        Assert.Contains("MANOSABA_LIN_CARD_XML_READER.title", message);
        Assert.DoesNotContain("MANOSABA_LIN_CARD_H_TT_P_SERVER2_CARD", message);
        Assert.DoesNotContain("MANOSABA_LIN_CARD_X_M_L2_READER", message);
        Assert.DoesNotContain("MANOSABA_LIN_CARD_X_ML_READER", message);
    }

    [Fact]
    public async Task ReportsContentModelKeysForRegistryPowerRegistration()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void RegisterContent()
                {
                    RitsuLibFramework.GetContentRegistry(MainFile.ModId)
                        .RegisterPower<MyPower>();
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\powers.json", "{}"));

        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_POWER_MY_POWER.title"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_POWER_MY_POWER.description"));
        Assert.DoesNotContain(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_POWER_MY_POWER.smartDescription"));
    }

    [Fact]
    public async Task ReportsOnlyRequiredOrbLocStringKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class MyOrb { }

                public static void RegisterContent()
                {
                    RitsuLibFramework.GetContentRegistry(MainFile.ModId)
                        .RegisterOrb<MyOrb>();
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\orbs.json", "{}"));

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.Contains("MANOSABA_LIN_ORB_MY_ORB.title", missing.GetMessage());
        Assert.Contains("MANOSABA_LIN_ORB_MY_ORB.description", missing.GetMessage());
        Assert.DoesNotContain("MANOSABA_LIN_ORB_MY_ORB.smartDescription", missing.GetMessage());
    }

    [Fact]
    public async Task DoesNotReportModelDbPowerUsedByTemporaryPowerInternalPower()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class TempStrengthPower : ModTemporaryPowerTemplate
                {
                    public override AbstractModel OriginModel => null!;
                    public override PowerModel InternallyAppliedPower => ModelDb.Power<StrengthPower>();
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\powers.json", "{}"));

        Assert.DoesNotContain(diagnostics, d => d.Id == AnalyzerUnderTest.MissingLocalizationId);
    }

    [Fact]
    public async Task RegisteredTemporaryPowerReportsOnlyWrapperLocalizationKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterPower]
                public sealed class TempStrengthPower : ModTemporaryPowerTemplate
                {
                    public override AbstractModel OriginModel => null!;
                    public override PowerModel InternallyAppliedPower => ModelDb.Power<StrengthPower>();
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\powers.json", "{}"));

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.Contains("MANOSABA_LIN_POWER_TEMP_STRENGTH_POWER.title", missing.GetMessage());
        Assert.Contains("MANOSABA_LIN_POWER_TEMP_STRENGTH_POWER.description", missing.GetMessage());
        Assert.DoesNotContain("MANOSABA_LIN_POWER_TEMP_STRENGTH_POWER.smartDescription", missing.GetMessage());
        Assert.DoesNotContain("MANOSABA_LIN_POWER_STRENGTH_POWER", missing.GetMessage());
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
    public async Task ReportsModSettingsTextLocalizationKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void UseSettingsText()
                {
                    var i18n = RitsuLibFramework.CreateModLocalization(MainFile.ModId, "settings");
                    _ = ModSettingsText.I18N(i18n, "settings.title", "Title");
                    _ = ModSettingsText.LocString("settings_ui", "settings.description", "Description");
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng.json", "{}"),
            AdditionalJson(@"C:\mod\localization\eng\settings_ui.json", "{}"));

        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("eng.json") &&
            d.GetMessage().Contains("settings.title"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("settings_ui.json") &&
            d.GetMessage().Contains("settings.description"));
    }

    [Fact]
    public async Task ReportsDynamicVarTooltipLocalizationKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void UseDynamicVars()
                {
                    _ = new IntVar("Heat", 0)
                        .WithSharedTooltip("MY_MOD_HEAT");
                    _ = new IntVar("Frost", 0)
                        .WithTooltip(
                            "static_hover_tips",
                            "MY_MOD_FROST.title",
                            descriptionTable: "static_hover_tips",
                            descriptionKey: "MY_MOD_FROST.description");
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\static_hover_tips.json", "{}"));

        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MY_MOD_HEAT.title") &&
            d.GetMessage().Contains("MY_MOD_HEAT.description"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MY_MOD_FROST.title"));
        Assert.Contains(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MY_MOD_FROST.description"));
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
        // GetDialoguesForKey accepts any of {0-0.ancient, 0-0r.ancient, 0-0.char, 0-0r.char}; when all are
        // absent the analyzer reports only the canonical .ancient variant to keep the diagnostic concise.
        Assert.Contains("THE_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-0.ancient", missing.GetMessage());
        Assert.DoesNotContain("THE_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-0.char", missing.GetMessage());
    }

    [Fact]
    public async Task DoesNotReportAncientDialogueWhenAnyVariantPresent()
    {
        // Per AncientDialogueLocalization.DialogueExists, a dialogue is considered defined when ANY of
        // {0-0.ancient, 0-0r.ancient, 0-0.char, 0-0r.char} is present. The analyzer must honor the
        // any-of relation and suppress the missing diagnostic in that case.
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void UseAncientDialogue()
                {
                    AncientDialogueLocalization.GetDialoguesForKey("ancients", "THE_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.");
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\ancients.json", """
            {
              "THE_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-0r.char": "Hello"
            }
            """));

        Assert.DoesNotContain(diagnostics, d => d.Id == AnalyzerUnderTest.MissingLocalizationId);
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
        // Only the canonical .ancient variant is reported; the other 3 dialogue line variants
        // (0-0r.ancient, 0-0.char, 0-0r.char) are part of the same any-of group.
        Assert.DoesNotContain(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            d.GetMessage().Contains("MANOSABA_LIN_ANCIENT_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-0.char"));
    }

    [Fact]
    public async Task ReportsRegisteredAncientInheritedEventLocStringKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterSharedAncient]
                public sealed class Architect { }
                """),
            AdditionalJson(@"C:\mod\localization\eng\ancients.json", "{}"));

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.Contains("MANOSABA_LIN_ANCIENT_ARCHITECT.title", missing.GetMessage());
        Assert.Contains("MANOSABA_LIN_ANCIENT_ARCHITECT.pages.INITIAL.description", missing.GetMessage());
        // .epithet is not a real field on the ancients table (not present in RitsuLib source, docs,
        // or any shipping mod) — verify the analyzer does not invent it.
        Assert.DoesNotContain("MANOSABA_LIN_ANCIENT_ARCHITECT.epithet", missing.GetMessage());
    }

    [Fact]
    public async Task ReportsEpochLocStringKeysFromStaticId()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterEpoch]
                public sealed class CrystalEpoch : EpochModel
                {
                    public override string Id => "CRYSTAL_GATE";
                }

                [RegisterStoryEpoch(typeof(MyStory))]
                public sealed class AttributeStoryEpoch : EpochModel
                {
                    public override string Id => "ATTRIBUTE_STORY_GATE";
                }

                public static void RegisterTimeline()
                {
                    RitsuLibFramework.GetTimelineRegistry(MainFile.ModId)
                        .RegisterEpoch<SecondEpoch>();

                    RitsuLibFramework.CreateContentPack(MainFile.ModId)
                        .Epoch<PackEpoch>();

                    ModTimelineRegistry.For(MainFile.ModId)
                        .RegisterStoryEpoch<MyStory, StoryEpochModel>();
                }

                public sealed class SecondEpoch : EpochModel
                {
                    private const string Prefix = "SECOND";
                    public override string Id => Prefix + "_GATE";
                }

                public sealed class PackEpoch : EpochModel
                {
                    public override string Id => $"PACK_{"GATE"}";
                }

                public sealed class StoryEpochModel : EpochModel
                {
                    public override string Id { get; } = "STORY_GATE";
                }

                public sealed class MyStory : StoryModel
                {
                    protected override string Id => "story";
                    public override EpochModel[] Epochs => Array.Empty<EpochModel>();
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\epochs.json", "{}"));

        var messages = diagnostics
            .Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId)
            .Select(d => d.GetMessage())
            .ToArray();

        Assert.Contains(messages, message => message.Contains("CRYSTAL_GATE.title") && message.Contains("CRYSTAL_GATE.unlockText"));
        Assert.Contains(messages, message => message.Contains("SECOND_GATE.description") && message.Contains("SECOND_GATE.unlockInfo"));
        Assert.Contains(messages, message => message.Contains("PACK_GATE.title"));
        Assert.Contains(messages, message => message.Contains("STORY_GATE.unlockText"));
        Assert.Contains(messages, message => message.Contains("ATTRIBUTE_STORY_GATE.description"));
        Assert.DoesNotContain(messages, message => message.Contains("MANOSABA_LIN_EPOCH_"));
    }

    [Fact]
    public async Task DoesNotGuessEpochOrStoryLocalizationKeys()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [RegisterEpoch]
                public sealed class DynamicEpoch : EpochModel
                {
                    public override string Id => DateTime.UtcNow.ToString();
                }

                [RegisterStory]
                public sealed class MyStory : StoryModel
                {
                    protected override string Id => "story";
                    public override EpochModel[] Epochs => Array.Empty<EpochModel>();
                }

                public static void RegisterTimeline()
                {
                    RitsuLibFramework.CreateContentPack(MainFile.ModId)
                        .Story<MyStory>();
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng\epochs.json", "{}"),
            AdditionalJson(@"C:\mod\localization\eng\stories.json", "{}"));

        Assert.DoesNotContain(diagnostics, d =>
            d.Id == AnalyzerUnderTest.MissingLocalizationId &&
            (d.GetMessage().Contains("MANOSABA_LIN_EPOCH_DYNAMIC_EPOCH") ||
             d.GetMessage().Contains("MANOSABA_LIN_STORY_MY_STORY") ||
             d.GetMessage().Contains("stories.json")));
    }

    [Fact]
    public async Task DoesNotReportSameNamedNonRitsuLibLocalizationHelpers()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class LocalKeywordRegistry
                {
                    public static LocalKeywordRegistry For(string modId) => new();
                    public void RegisterOwned(string localStem) { }
                    public void RegisterCardKeywordOwnedByLocNamespace(string localStem) { }
                }

                public sealed class LocalPack
                {
                    public LocalPack CardKeywordOwnedByLocNamespace(string localStem) => this;
                    public LocalPack KeywordOwned(string localStem) => this;
                }

                public sealed class LocalI18N
                {
                    public bool ContainsKey(string key) => false;
                    public bool TryGet(string key, out string value) { value = ""; return false; }
                    public string Get(string key, string fallback) => fallback;
                }

                public static class LocalAncientDialogueLocalization
                {
                    public static void GetDialoguesForKey(string locTable, string baseKey) { }
                    public static void BuildDialogueSetForModAncient(string ancientEntry) { }
                }

                public static class LocalModSettingsText
                {
                    public static object I18N(LocalI18N localization, string key, string fallback) => new();
                    public static object LocString(string table, string key, string fallback) => new();
                }

                public sealed class LocalDynamicVar
                {
                    public LocalDynamicVar WithSharedTooltip(string entryPrefix) => this;
                    public LocalDynamicVar WithTooltip(string titleTable, string titleKey, string descriptionTable, string descriptionKey) => this;
                }

                public static void UseSameNamedHelpers()
                {
                    LocalKeywordRegistry.For(MainFile.ModId).RegisterOwned("fake");
                    LocalKeywordRegistry.For(MainFile.ModId).RegisterCardKeywordOwnedByLocNamespace("fake");
                    new LocalPack()
                        .CardKeywordOwnedByLocNamespace("fake")
                        .KeywordOwned("fake");

                    var i18n = new LocalI18N();
                    _ = i18n.ContainsKey("settings.fake");
                    _ = i18n.TryGet("settings.other", out var _);
                    _ = i18n.Get("settings.more", "");

                    LocalAncientDialogueLocalization.GetDialoguesForKey("ancients", "FAKE.");
                    LocalAncientDialogueLocalization.BuildDialogueSetForModAncient("FAKE_ANCIENT");

                    _ = LocalModSettingsText.I18N(i18n, "settings.fake", "");
                    _ = LocalModSettingsText.LocString("settings_ui", "settings.fake", "");

                    _ = new LocalDynamicVar()
                        .WithSharedTooltip("FAKE_TIP")
                        .WithTooltip("static_hover_tips", "FAKE.title", "static_hover_tips", "FAKE.description");
                }
                """),
            AdditionalJson(@"C:\mod\localization\eng.json", "{}"),
            AdditionalJson(@"C:\mod\localization\eng\ancients.json", "{}"),
            AdditionalJson(@"C:\mod\localization\eng\card_keywords.json", "{}"),
            AdditionalJson(@"C:\mod\localization\eng\settings_ui.json", "{}"),
            AdditionalJson(@"C:\mod\localization\eng\static_hover_tips.json", "{}"));

        Assert.DoesNotContain(diagnostics, d => d.Id == AnalyzerUnderTest.MissingLocalizationId);
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
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/card_keywords.json");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.Contains("\"EXISTING\": \"ok\"", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", text);
        AssertValidJson(text);
    }

    [Fact]
    public async Task JsonCodeFixPreservesNestedJsonAndStringBraces()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", """
            {
              "EXISTING": {
                "text": "value with } brace",
                "items": [1, { "inner": "}" }]
              }
            }
            """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/card_keywords.json");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.Contains("\"text\": \"value with } brace\"", text);
        Assert.Contains("\"inner\": \"}\"", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", text);
        AssertValidJson(text);
    }

    [Fact]
    public async Task JsonCodeFixHandlesBomCrlfAndTrailingWhitespace()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", "\uFEFF{\r\n  \"EXISTING\": \"ok\"\r\n}\r\n   "));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/card_keywords.json");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.StartsWith("\uFEFF", text, StringComparison.Ordinal);
        Assert.Contains("\r\n", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", text);
        AssertValidJson(text);
    }

    [Fact]
    public async Task JsonCodeFixReusesTopLevelTrailingComma()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", """
            {
              "EXISTING": "ok",
            }
            """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/card_keywords.json");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.DoesNotContain(",,", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", text);
        AssertValidJson(text);
    }

    [Fact]
    public async Task JsonCodeFixCreatesMissingTableFile()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory("eng");
        var cardsPath = Path.Combine(projectDirectory, "localization", "eng", "cards.json");
        var hoverTipsPath = Path.Combine(projectDirectory, "localization", "eng", "static_hover_tips.json");
        var project = CreateProject(
            Source("""
                public static void RegisterUi()
                {
                    ModCardPileRegistry.For(MainFile.ModId).RegisterOwned("test_pile", new ModCardPileSpec());
                }
                """),
            projectDirectory,
            AdditionalFile(cardsPath, "{}"));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        _ = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/static_hover_tips.json");
        var text = await File.ReadAllTextAsync(hoverTipsPath);

        Assert.Contains("\"MANOSABA_LIN_CARDPILE_TEST_PILE.title\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_CARDPILE_TEST_PILE.description\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_CARDPILE_TEST_PILE.empty\": \"\"", text);
        AssertValidJson(text);
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
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/card_keywords.json");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.StartsWith("{", text.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", text);
        AssertValidJson(text);
    }

    [Fact]
    public async Task JsonCodeFixReplacesBomOnlyJsonWithObject()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", "\uFEFF   "));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/card_keywords.json");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.StartsWith("\uFEFF{", text, StringComparison.Ordinal);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", text);
        AssertValidJson(text);
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
    public async Task SnippetCodeFixInsertsBeforeAttributedMemberWithoutBreakingSyntax()
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

        Assert.Contains("*/" + Environment.NewLine + "        [RegisterOwnedCardKeyword(\"hiro\")]", text);
        Assert.DoesNotContain("[/*", text);
        Assert.DoesNotContain("*/RegisterOwnedCardKeyword", text);

        var changedDocument = Assert.Single(changed.Projects.Single().Documents);
        var root = await changedDocument.GetSyntaxRootAsync();
        Assert.NotNull(root);
        Assert.Empty(root!.GetDiagnostics());
    }

    [Fact]
    public async Task SnippetCodeFixKeepsDiagnosticAndDoesNotInsertDuplicateSnippet()
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
        var changedProject = changed.Projects.Single();
        var diagnosticsAfterSnippet = (await AnalyzeProjectAsync(changedProject))
            .Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId)
            .ToArray();

        var diagnosticAfterSnippet = Assert.Single(diagnosticsAfterSnippet);
        var changedAgain = await ApplyCodeFixAsync(changedProject, diagnosticAfterSnippet, "Insert localization");
        var text = await GetDocumentTextAsync(changedAgain);

        Assert.Equal(1, CountOccurrences(text, "Missing RitsuLib localization:"));
        Assert.Equal(1, CountOccurrences(text, "\"MANOSABA_LIN_KEYWORD_HIRO.title\""));
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

        Assert.Contains(actions, action => action.Title == "添加缺失的本地化到 eng/card_keywords.json");
        Assert.Contains(actions, action => action.Title == "修复所有本地化缺失问题");
        Assert.Contains(actions, action => action.Title == "插入本地化 JSON 片段");
        Assert.DoesNotContain(actions, action => action.Title.Contains("*/", StringComparison.Ordinal));
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

        Assert.Contains(actions, action => action.Title == "Add missing localization to eng/card_keywords.json");
        Assert.Contains(actions, action => action.Title == "Fix all missing localization issues");
        Assert.Contains(actions, action => action.Title == "Insert localization JSON snippet");
        Assert.DoesNotContain(actions, action => action.Title.Contains("*/", StringComparison.Ordinal));
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

        var diagnostic = Assert.Single(diagnostics);

        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/cards.json");

        var engText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");
        var zhsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\zhs\cards.json");

        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engText);
        Assert.DoesNotContain("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsText);
        Assert.DoesNotContain("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsText);
    }

    [Fact]
    public async Task JsonCodeFixAddsGameAccurateAcronymCardKeys()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class HTTPServer2Card { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\cards.json", "{}"));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project))
            .Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));

        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/cards.json");
        var text = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");

        Assert.Contains("\"MANOSABA_LIN_CARD_HTTP_SERVER2_CARD.title\": \"\"", text);
        Assert.Contains("\"MANOSABA_LIN_CARD_HTTP_SERVER2_CARD.description\": \"\"", text);
        Assert.DoesNotContain("H_TT_P_SERVER2_CARD", text);
    }

    [Fact]
    public async Task CodeFixTitlesUseFlatTargetActionsForSameLocationEncounterDiagnostics()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterGlobalEncounter]
                public sealed class MyEncounter { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\encounters.json", "{}"),
            AdditionalFile(@"C:\mod\localization\zhs\encounters.json", "{}"));

        var encounterDiagnostics = (await AnalyzeProjectAsync(project))
            .Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("encounters.json"))
            .ToImmutableArray();

        var encounterDiagnostic = Assert.Single(encounterDiagnostics);

        var actions = await GetCodeActionsAsync(project, encounterDiagnostic);
        var titles = actions.Select(action => action.Title).ToArray();

        Assert.Equal(
            new[]
            {
                "Add missing localization to eng/encounters.json",
                "Add missing localization to zhs/encounters.json",
                "Fix all missing localization issues",
                "Insert localization JSON snippet",
            },
            titles);
        Assert.DoesNotContain(titles, title => title.Contains("*/encounters.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsOneAnalyzerDiagnosticForSameLocationMissingLocalization()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterSharedAncient]
                public sealed class MyAncient { }
                """),
            AdditionalFile(@"C:\mod\localization\eng\ancients.json", "{}"),
            AdditionalFile(@"C:\mod\localization\zhs\ancients.json", "{}"));

        var diagnostics = (await AnalyzeProjectAsync(project))
            .Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("ancients.json"))
            .ToImmutableArray();

        var diagnostic = Assert.Single(diagnostics);
        var titles = (await GetCodeActionsAsync(project, diagnostic))
            .Select(action => action.Title)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Add missing localization to eng/ancients.json",
                "Add missing localization to zhs/ancients.json",
                "Fix all missing localization issues",
                "Insert localization JSON snippet",
            },
            titles);
    }

    [Fact]
    public async Task JsonCodeFixFixesAllMissingLocalizationProblems()
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
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Fix all missing localization issues");

        var cardsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");
        var keywordsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", cardsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", cardsText);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", keywordsText);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", keywordsText);
    }

    [Fact]
    public async Task JsonCodeFixTargetFileDoesNotFixOtherTablesOrLanguages()
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
            AdditionalFile(@"C:\mod\localization\zhs\cards.json", "{}"),
            AdditionalFile(@"C:\mod\localization\eng\card_keywords.json", "{}"),
            AdditionalFile(@"C:\mod\localization\zhs\card_keywords.json", "{}"));

        var diagnostic = (await AnalyzeProjectAsync(project))
            .First(d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("eng/cards.json"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/cards.json");

        var engCardsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");
        var zhsCardsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\zhs\cards.json");
        var engKeywordsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");
        var zhsKeywordsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\zhs\card_keywords.json");

        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", engCardsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engCardsText);
        Assert.DoesNotContain("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsCardsText);
        Assert.DoesNotContain("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsCardsText);
        Assert.DoesNotContain("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", engKeywordsText);
        Assert.DoesNotContain("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", engKeywordsText);
        Assert.DoesNotContain("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", zhsKeywordsText);
        Assert.DoesNotContain("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", zhsKeywordsText);
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
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/cards.json");
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
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Fix all missing localization issues");

        var cardsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");
        var keywordsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");

        Assert.Equal("{ invalid", cardsText);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.title\": \"\"", keywordsText);
        Assert.Contains("\"MANOSABA_LIN_KEYWORD_HIRO.description\": \"\"", keywordsText);
    }

    [Fact]
    public async Task LocalizationDirectoriesWithoutJsonFilesReportMissingKeys()
    {
        var projectDirectory = CreateTemporaryProjectDirectory("eng", "zhs");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            projectDirectory);

        var diagnostics = await AnalyzeProjectAsync(project, projectDirectory);

        Assert.Contains(diagnostics, d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("eng/cards.json"));
        Assert.Contains(diagnostics, d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("zhs/cards.json"));
    }

    [Fact]
    public async Task LocalizationDirectoryLanguageWarnsWhenEngHasKeyButOtherLanguageDoesNot()
    {
        var projectDirectory = CreateTemporaryProjectDirectory("eng", "zhs");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "localization", "eng", "cards.json"), """
            {
              "MANOSABA_LIN_CARD_MY_CARD.title": "Existing",
              "MANOSABA_LIN_CARD_MY_CARD.description": "Existing"
            }
            """));

        var diagnostics = (await AnalyzeProjectAsync(project, projectDirectory))
            .Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId)
            .ToImmutableArray();

        Assert.DoesNotContain(diagnostics, d => d.GetMessage().Contains("eng/cards.json"));
        var zhsMissing = Assert.Single(diagnostics.Where(d => d.GetMessage().Contains("zhs/cards.json")));
        Assert.Equal(DiagnosticSeverity.Warning, zhsMissing.Severity);
        Assert.Contains("MANOSABA_LIN_CARD_MY_CARD.title", zhsMissing.GetMessage());
        Assert.Contains("MANOSABA_LIN_CARD_MY_CARD.description", zhsMissing.GetMessage());
    }

    [Fact]
    public async Task LocalizationDirectoryLanguageSplitsFallbackErrorsFromTranslationWarnings()
    {
        var projectDirectory = CreateTemporaryProjectDirectory("eng", "zhs");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "localization", "eng", "cards.json"), """
            {
              "MANOSABA_LIN_CARD_MY_CARD.title": "Existing"
            }
            """));

        var diagnostics = (await AnalyzeProjectAsync(project, projectDirectory))
            .Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId)
            .ToImmutableArray();

        var missing = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, missing.Severity);
        Assert.Contains("eng/cards.json", missing.GetMessage());
        Assert.Contains("zhs/cards.json", missing.GetMessage());
        Assert.Contains("MANOSABA_LIN_CARD_MY_CARD.title", missing.GetMessage());
        Assert.Contains("MANOSABA_LIN_CARD_MY_CARD.description", missing.GetMessage());
        Assert.Contains(Path.Combine(projectDirectory, "localization", "eng", "cards.json"), missing.Properties[RitsuLibDiagnosticProperties.TargetPaths]);
        Assert.Contains(Path.Combine(projectDirectory, "localization", "zhs", "cards.json"), missing.Properties[RitsuLibDiagnosticProperties.TargetPaths]);
    }

    [Fact]
    public async Task JsonCodeFixCreatesFilesForLocalizationDirectoryLanguages()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory("eng", "zhs");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            projectDirectory);

        var diagnostic = (await AnalyzeProjectAsync(project, projectDirectory))
            .First(d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("eng/cards.json"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Fix all missing localization issues");

        var engPath = Path.Combine(projectDirectory, "localization", "eng", "cards.json");
        var zhsPath = Path.Combine(projectDirectory, "localization", "zhs", "cards.json");
        var engText = await File.ReadAllTextAsync(engPath);
        var zhsText = await File.ReadAllTextAsync(zhsPath);

        Assert.True(File.Exists(engPath));
        Assert.True(File.Exists(zhsPath));
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsText);
        Assert.Empty(changed.Projects.Single().AdditionalDocuments);
        AssertValidJson(engText);
        AssertValidJson(zhsText);
    }

    [Fact]
    public async Task JsonCodeFixUpdatesExistingAdditionalDocumentWithoutPhysicalWrite()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory("eng");
        var charactersPath = Path.Combine(projectDirectory, "localization", "eng", "characters.json");
        var originalDiskText = """
        {
          "MANOSABA_LIN_CHARACTER_HERO.title": "Hero"
        }
        """;
        File.WriteAllText(charactersPath, originalDiskText);

        var project = CreateProject(
            Source("""
                [RegisterCharacter]
                public sealed class Hero { }
                """),
            projectDirectory,
            AdditionalFile(charactersPath, originalDiskText));

        var diagnostic = (await AnalyzeProjectAsync(project, projectDirectory))
            .First(d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("characters.json"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Fix all missing localization issues");

        var changedText = await GetAdditionalTextAsync(changed, charactersPath);
        var diskText = await File.ReadAllTextAsync(charactersPath);

        Assert.Contains("\"MANOSABA_LIN_CHARACTER_HERO.title\": \"Hero\"", changedText);
        Assert.Contains("\"MANOSABA_LIN_CHARACTER_HERO.description\": \"\"", changedText);
        Assert.Contains("\"MANOSABA_LIN_CHARACTER_HERO.flavor\": \"\"", changedText);
        AssertValidJson(changedText);
        Assert.Equal(originalDiskText, diskText);
    }

    [Fact]
    public async Task JsonCodeFixTargetFileCreatesMissingLanguageFile()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory("eng", "zhs");
        var engCardsPath = Path.Combine(projectDirectory, "localization", "eng", "cards.json");
        var zhsCardsPath = Path.Combine(projectDirectory, "localization", "zhs", "cards.json");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            projectDirectory,
            AdditionalFile(engCardsPath, """
            {
              "MANOSABA_LIN_CARD_MY_CARD.title": "Existing",
              "MANOSABA_LIN_CARD_MY_CARD.description": "Existing"
            }
            """));

        var diagnostic = (await AnalyzeProjectAsync(project, projectDirectory))
            .First(d => d.Id == AnalyzerUnderTest.MissingLocalizationId && d.GetMessage().Contains("zhs/cards.json"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to zhs/cards.json");

        var engText = await GetAdditionalTextAsync(changed, engCardsPath);
        var zhsText = await File.ReadAllTextAsync(zhsCardsPath);

        Assert.True(File.Exists(zhsCardsPath));
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"Existing\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsText);
        AssertValidJson(zhsText);
    }

    [Fact]
    public async Task JsonCodeFixPreservesEngExistingKeysAndCreatesOtherLanguageFile()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory("eng", "zhs");
        var engCardsPath = Path.Combine(projectDirectory, "localization", "eng", "cards.json");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            projectDirectory,
            AdditionalFile(engCardsPath, """
            {
              "MANOSABA_LIN_CARD_MY_CARD.title": "Existing"
            }
            """));

        var diagnostic = (await AnalyzeProjectAsync(project, projectDirectory))
            .First(d => d.Id == AnalyzerUnderTest.MissingLocalizationId);
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Fix all missing localization issues");

        var engText = await GetAdditionalTextAsync(changed, engCardsPath);
        var zhsPath = Path.Combine(projectDirectory, "localization", "zhs", "cards.json");
        var zhsText = await File.ReadAllTextAsync(zhsPath);

        Assert.Equal(1, CountOccurrences(engText, "\"MANOSABA_LIN_CARD_MY_CARD.title\""));
        Assert.True(File.Exists(zhsPath));
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"Existing\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsText);
        AssertValidJson(zhsText);
    }

    [Fact]
    public async Task EmptyLocalizationDirectoriesAreDetectedFromProjectDirProperty()
    {
        var projectDirectory = CreateTemporaryProjectDirectory("eng", "zhs");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            projectDirectory);

        var diagnostics = await AnalyzeProjectAsync(
            project,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProjectDir"] = projectDirectory,
            });

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.Contains("eng/cards.json", missing.GetMessage());
        Assert.Contains("zhs/cards.json", missing.GetMessage());
    }

    [Fact]
    public async Task EmptyLocalizationDirectoriesAreDetectedFromSourcePathWhenBuildPropertiesAreMissing()
    {
        var projectDirectory = CreateTemporaryProjectDirectory("eng", "zhs");
        var project = CreateProject(
            Source("""
                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """),
            projectDirectory);

        var diagnostics = await AnalyzeProjectAsync(
            project,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var missing = Assert.Single(diagnostics.Where(d => d.Id == AnalyzerUnderTest.MissingLocalizationId));
        Assert.Contains("eng/cards.json", missing.GetMessage());
        Assert.Contains("zhs/cards.json", missing.GetMessage());
    }

    [Fact]
    public void FixAllProviderIsNotExposed()
    {
        var provider = new RitsuLibLocalizationCodeFixProvider();

        Assert.Null(provider.GetFixAllProvider());
    }

    private static void AssertValidJson(string text)
    {
        using var _ = JsonDocument.Parse(text.TrimStart('\uFEFF'));
    }
}
