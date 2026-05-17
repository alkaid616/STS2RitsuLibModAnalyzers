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
            d.GetMessage().Contains("MANOSABA_LIN_POWER_MY_POWER.smartDescription"));
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
            d.GetMessage().Contains("MANOSABA_LIN_POWER_MY_POWER.smartDescription"));
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
        Assert.Contains("MANOSABA_LIN_POWER_TEMP_STRENGTH_POWER.smartDescription", missing.GetMessage());
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
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/static_hover_tips.json");
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
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to eng/card_keywords.json");
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

        Assert.Contains(actions, action => action.Title == "添加缺失的本地化到 eng/card_keywords.json");
        Assert.Contains(actions, action => action.Title == "添加缺失的本地化到 */card_keywords.json");
        Assert.Contains(actions, action => action.Title == "修复所有本地化缺失问题");
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

        Assert.Contains(actions, action => action.Title == "Add missing localization to eng/card_keywords.json");
        Assert.Contains(actions, action => action.Title == "Add missing localization to */card_keywords.json");
        Assert.Contains(actions, action => action.Title == "Fix all missing localization issues");
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
        var changed = await ApplyCodeFixAsync(project, engDiagnostic, "Add missing localization to eng/cards.json");

        var engText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");
        var zhsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\zhs\cards.json");

        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engText);
        Assert.DoesNotContain("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsText);
        Assert.DoesNotContain("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsText);
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
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to */cards.json");

        var engText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");
        var zhsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\zhs\cards.json");

        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsText);
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
    public async Task JsonCodeFixAllLanguagesSameTableDoesNotFixOtherTables()
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
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add missing localization to */cards.json");

        var engCardsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\cards.json");
        var zhsCardsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\zhs\cards.json");
        var engKeywordsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\eng\card_keywords.json");
        var zhsKeywordsText = await GetAdditionalTextAsync(changed, @"C:\mod\localization\zhs\card_keywords.json");

        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", engCardsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engCardsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsCardsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsCardsText);
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

        var engMissing = Assert.Single(diagnostics.Where(d => d.GetMessage().Contains("eng/cards.json")));
        Assert.Equal(DiagnosticSeverity.Error, engMissing.Severity);
        Assert.Contains("MANOSABA_LIN_CARD_MY_CARD.description", engMissing.GetMessage());
        Assert.DoesNotContain("MANOSABA_LIN_CARD_MY_CARD.title", engMissing.GetMessage());

        var zhsError = Assert.Single(diagnostics.Where(d =>
            d.GetMessage().Contains("zhs/cards.json") &&
            d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("MANOSABA_LIN_CARD_MY_CARD.description", zhsError.GetMessage());
        Assert.DoesNotContain("MANOSABA_LIN_CARD_MY_CARD.title", zhsError.GetMessage());

        var zhsWarning = Assert.Single(diagnostics.Where(d =>
            d.GetMessage().Contains("zhs/cards.json") &&
            d.Severity == DiagnosticSeverity.Warning));
        Assert.Contains("MANOSABA_LIN_CARD_MY_CARD.title", zhsWarning.GetMessage());
        Assert.DoesNotContain("MANOSABA_LIN_CARD_MY_CARD.description", zhsWarning.GetMessage());
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
        var engText = await GetAdditionalTextAsync(changed, engPath);
        var zhsText = await GetAdditionalTextAsync(changed, zhsPath);

        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsText);
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
        var zhsText = await GetAdditionalTextAsync(changed, Path.Combine(projectDirectory, "localization", "zhs", "cards.json"));

        Assert.Equal(1, CountOccurrences(engText, "\"MANOSABA_LIN_CARD_MY_CARD.title\""));
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"Existing\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", engText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.title\": \"\"", zhsText);
        Assert.Contains("\"MANOSABA_LIN_CARD_MY_CARD.description\": \"\"", zhsText);
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
        Assert.Equal("Fix all missing localization issues", fixAllAction.Title);
    }
}
