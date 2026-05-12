namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu013DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU013");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsRelativeResourcePath()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("assets/icon.png");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU013");
    }

    [Fact]
    public async Task ResPathAtProjectRootIsFoundInAdditionalFiles()
    {
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://icon.svg");
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "icon.svg"), "<svg/>"));

        var diagnostics = await AnalyzeProjectAsync(project);
        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU013");
    }

    [Fact]
    public async Task ResPathUsesProjectRootRelativeSubpath()
    {
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://Resources/sub/icon.png");
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Resources", "sub", "icon.png"), ""));

        var diagnostics = await AnalyzeProjectAsync(project);
        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU013");
    }

    [Fact]
    public async Task ResPathDoesNotMatchResourceMarkerRelativeSubpath()
    {
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://sub/icon.png");
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Resources", "sub", "icon.png"), ""));

        var diagnostics = await AnalyzeProjectAsync(project);
        Assert.Contains(diagnostics, d => d.Id == "RITSU013");
    }

    [Fact]
    public async Task ResPathDoesNotMatchByFileNameOnly()
    {
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://icon.svg");
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "assets", "icon.svg"), "<svg/>"));

        var diagnostics = await AnalyzeProjectAsync(project);
        Assert.Contains(diagnostics, d => d.Id == "RITSU013");
    }

    [Fact]
    public async Task AttributeResourcePathChecksCoverIconOutlinePathAndBigIconPath()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("test", IconOutlinePath = "images/outline.png", BigIconPath = "images/big.png")]
                public static class TestKeyword { }
                """));

        var diagnostics = (await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013").ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("outline.png"));
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("big.png"));
    }

    [Fact]
    public async Task ResourcePathCodeFixAddsResPrefix()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [RegisterOwnedCardKeyword("test", IconPath = "images/icon.png")]
                public static class TestKeyword { }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add res://");
        var text = await GetDocumentTextAsync(changed);
        Assert.Contains("res://images/icon.png", text);
    }

    [Fact]
    public async Task InterpolatedResPathResolvesToActualValueAndChecksExistence()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static class Entry { public const string ResPath = "res://Test"; }

                [RegisterOwnedCardKeyword("test", IconPath = $"{Entry.ResPath}/images/keywords/{typeof(TestKeyword).Name}.png")]
                public static class TestKeyword { }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "placeholder.png"), ""));

        var diagnostics = (await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013").ToArray();
        Assert.Single(diagnostics);
        Assert.Contains("res://Test/images/keywords/TestKeyword.png", diagnostics[0].GetMessage());

        var actions = await GetCodeActionsAsync(project, diagnostics[0]);
        Assert.Contains(actions, action => action.Title == "Insert RitsuLib TODO fix snippet");
        Assert.Contains(actions, action => action.Title == "Insert RitsuLib TODOs for all missing resource paths in current file");
    }

    [Fact]
    public async Task ResourcePathNotFoundTodoSuppressesFollowUpWarning()
    {
        using var culture = UseCulture("zh-CN");
        var projectDirectory = CreateTemporaryProjectDirectory();
        Directory.CreateDirectory(projectDirectory);
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://missing/icon.png");
                }
                """),
            projectDirectory);

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013"));
        Assert.Equal("资源路径 'res://missing/icon.png' 在项目资源索引中未找到。", diagnostic.GetMessage());

        var changed = await ApplyCodeFixAsync(project, diagnostic, "插入 RitsuLib TODO");
        var text = await GetDocumentTextAsync(changed);
        Assert.Contains("TODO RitsuLib analyzer: 资源路径 'res://missing/icon.png' 在项目资源索引中未找到。", text);

        var changedProject = Assert.Single(changed.Projects);
        var diagnosticsAfterFix = await AnalyzeProjectAsync(changedProject, projectDirectory);
        Assert.DoesNotContain(diagnosticsAfterFix, d =>
            d.Id == "RITSU013" &&
            d.GetMessage().Contains("res://missing/icon.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResourcePathNotFoundTodoSuppressesOnlyMatchingLocation()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        Directory.CreateDirectory(projectDirectory);
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://missing/one.png");
                    RitsuLibFramework.LoadIcon("res://missing/two.png");
                }
                """),
            projectDirectory);

        var diagnostics = (await AnalyzeProjectAsync(project))
            .Where(d => d.Id == "RITSU013")
            .ToArray();
        Assert.Equal(2, diagnostics.Length);

        var diagnostic = Assert.Single(diagnostics, d => d.GetMessage().Contains("res://missing/one.png", StringComparison.Ordinal));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Insert RitsuLib TODO fix snippet");
        var text = await GetDocumentTextAsync(changed);
        Assert.Contains("TODO RitsuLib analyzer: 'res://missing/one.png' was not found in the project resource index.", text);

        var changedProject = Assert.Single(changed.Projects);
        var diagnosticsAfterFix = (await AnalyzeProjectAsync(changedProject, projectDirectory))
            .Where(d => d.Id == "RITSU013")
            .ToArray();
        Assert.DoesNotContain(diagnosticsAfterFix, d => d.GetMessage().Contains("res://missing/one.png", StringComparison.Ordinal));
        Assert.Contains(diagnosticsAfterFix, d => d.GetMessage().Contains("res://missing/two.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResourcePathTodoCodeFixInsertsTodosForCurrentFileMissingPaths()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        Directory.CreateDirectory(projectDirectory);
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://missing/one.png");
                    RitsuLibFramework.LoadIcon("res://missing/two.png");
                }
                """),
            projectDirectory);

        var diagnostics = (await AnalyzeProjectAsync(project))
            .Where(d => d.Id == "RITSU013")
            .ToArray();
        Assert.Equal(2, diagnostics.Length);

        var changed = await ApplyCodeFixAsync(
            project,
            diagnostics[0],
            "Insert RitsuLib TODOs for all missing resource paths in current file");
        var text = await GetDocumentTextAsync(changed);
        Assert.Equal(2, CountOccurrences(text, "TODO RitsuLib analyzer:"));
        Assert.Contains("res://missing/one.png", text);
        Assert.Contains("res://missing/two.png", text);

        var changedProject = Assert.Single(changed.Projects);
        var diagnosticsAfterFix = (await AnalyzeProjectAsync(changedProject, projectDirectory))
            .Where(d => d.Id == "RITSU013")
            .ToArray();
        Assert.Empty(diagnosticsAfterFix);
    }

    [Fact]
    public async Task ResourcePathTodoCodeFixDoesNotInsertTodosInOtherFiles()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        Directory.CreateDirectory(projectDirectory);
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://missing/current.png");
                }
                """),
            projectDirectory);
        var otherDocumentId = DocumentId.CreateNewId(project.Id);
        project = project.Solution.AddDocument(
                otherDocumentId,
                "Other.cs",
                SourceText.From("""
                    namespace ManosabaLin
                    {
                        internal static class OtherContent
                        {
                            public static void BuildOther()
                            {
                                STS2RitsuLib.RitsuLibFramework.LoadIcon("res://missing/other.png");
                            }
                        }
                    }
                    """, Encoding.UTF8),
                filePath: Path.Combine(projectDirectory, "Other.cs"))
            .GetProject(project.Id)!;

        var diagnostics = (await AnalyzeProjectAsync(project))
            .Where(d => d.Id == "RITSU013")
            .ToArray();
        Assert.Equal(2, diagnostics.Length);

        var currentDiagnostic = Assert.Single(diagnostics, d => d.GetMessage().Contains("res://missing/current.png", StringComparison.Ordinal));
        var changed = await ApplyCodeFixAsync(
            project,
            currentDiagnostic,
            "Insert RitsuLib TODOs for all missing resource paths in current file");
        var currentText = await GetDocumentTextAsync(changed, "Test.cs");
        var otherText = await GetDocumentTextAsync(changed, "Other.cs");
        Assert.Contains("TODO RitsuLib analyzer:", currentText);
        Assert.DoesNotContain("TODO RitsuLib analyzer:", otherText);

        var changedProject = Assert.Single(changed.Projects);
        var diagnosticsAfterFix = (await AnalyzeProjectAsync(changedProject, projectDirectory))
            .Where(d => d.Id == "RITSU013")
            .ToArray();
        Assert.DoesNotContain(diagnosticsAfterFix, d => d.GetMessage().Contains("res://missing/current.png", StringComparison.Ordinal));
        Assert.Contains(diagnosticsAfterFix, d => d.GetMessage().Contains("res://missing/other.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ObjectCreationResPathChecksAssetProfileProperties()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static class Entry { public const string ResPath = "res://Test"; }
                public class ModRelicTemplate { public virtual AssetProfile AssetProfile => null; }
                public class AssetProfile { public AssetProfile(string IconPath = null, string IconOutlinePath = null, string BigIconPath = null) {} }

                public class TestRelic : ModRelicTemplate
                {
                    public override AssetProfile AssetProfile => new AssetProfile(
                        IconPath: $"res://Test/images/relics/{this.GetType().Name}.png",
                        IconOutlinePath: $"res://Test/images/relics/{this.GetType().Name}.png",
                        BigIconPath: $"res://Test/images/relics/{this.GetType().Name}.png");
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "placeholder.png"), ""));

        var diagnostics = (await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013").ToArray();
        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Contains("res://Test/images/relics/TestRelic.png", d.GetMessage()));
    }

    [Fact]
    public async Task AssetProfileInterpolatedResPathUsesEntryAndGetTypeNameFromProjectRoot()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static class Entry { public const string ResPath = "res://Test"; }
                public class ModRelicTemplate { public virtual AssetProfile AssetProfile => null; }
                public class AssetProfile { public AssetProfile(string IconPath = null) {} }

                public class TestRelicTutorial : ModRelicTemplate
                {
                    public override AssetProfile AssetProfile => new AssetProfile(
                        IconPath: $"{Entry.ResPath}/images/relics/{GetType().Name}.png   ");
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "images", "relics", "TestRelicTutorial.png"), ""));

        var diagnostics = (await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013").ToArray();
        Assert.DoesNotContain(diagnostics, d => d.GetMessage().Contains("TestRelicTutorial", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AssetProfileInitializerChecksAllStringProperties()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static class Entry { public const string ResPath = "res://Test"; }
                public class ModRelicTemplate { public virtual AssetProfile AssetProfile => null; }
                public class AssetProfile
                {
                    public string IconPath { get; set; }
                    public string MainArt { get; set; }
                }

                public class TestRelicTutorial : ModRelicTemplate
                {
                    public override AssetProfile AssetProfile => new AssetProfile
                    {
                        IconPath = $"{Entry.ResPath}/images/relics/{GetType().Name}.png",
                        MainArt = $"{Entry.ResPath}/images/relics/{GetType().Name}.png",
                    };
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "placeholder.png"), ""));

        var diagnostics = (await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013").ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Contains("res://Test/images/relics/TestRelicTutorial.png", d.GetMessage()));
    }

    [Fact]
    public async Task SuffixAssetProfileTypesCheckAllStringConstructorPaths()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static class Entry { public const string ResPath = "res://Test"; }
                public class RelicAssetProfile { public RelicAssetProfile(string IconPath = null) {} }
                public class CardAssetProfile { public CardAssetProfile(string PortraitPath = null) {} }
                public class CharacterAssetProfile { public CharacterAssetProfile(string CharacterSelectIconPath = null) {} }

                public class TestRelic { public RelicAssetProfile AssetProfile => new(IconPath: $"{Entry.ResPath}/images/relics/{GetType().Name}.png"); }
                public class TestCard { public CardAssetProfile AssetProfile => new(PortraitPath: $"{Entry.ResPath}/images/cards/{GetType().Name}.png"); }
                public class TestCharacter { public CharacterAssetProfile AssetProfile => new(CharacterSelectIconPath: $"{Entry.ResPath}/images/characters/{GetType().Name}.png"); }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "placeholder.png"), ""));

        var diagnostics = (await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013").ToArray();
        Assert.Equal(3, diagnostics.Length);
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("res://Test/images/relics/TestRelic.png"));
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("res://Test/images/cards/TestCard.png"));
        Assert.Contains(diagnostics, d => d.GetMessage().Contains("res://Test/images/characters/TestCharacter.png"));
    }

    [Fact]
    public async Task OverrideResourcePathPropertiesAreCheckedAndResolveConstChains()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static class Entry
                {
                    public const string ModId = "Test";
                    public const string ResPath = $"res://{ModId}";
                }

                public class CharacterTemplate
                {
                    public virtual string? BigEnergyIconPath => null;
                    public virtual string? CustomVisualPath => null;
                }

                public class TestCharacter : CharacterTemplate
                {
                    private const string SceneRoot = $"{Entry.ResPath}/scenes/characters";
                    private const string CharacterScenePath = $"{SceneRoot}/Test_character.tscn";

                    public override string? BigEnergyIconPath => $"{Entry.ResPath}/images/characters/energy_big.png";
                    public override string? CustomVisualPath => CharacterScenePath;
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "scenes", "characters", "Test_character.tscn"), ""));

        var diagnostics = (await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013").ToArray();
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("res://Test/images/characters/energy_big.png", diagnostic.GetMessage());
    }

    [Fact]
    public async Task ProjectRootResourceScanFindsPhysicalAssetsWithoutAdditionalFiles()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var assetDirectory = Path.Combine(projectDirectory, "Test", "images", "relics");
        Directory.CreateDirectory(assetDirectory);
        File.WriteAllText(Path.Combine(assetDirectory, "TestRelicTutorial.png"), "");

        var project = CreateProject(
            Source("""
                public static class Entry { public const string ResPath = "res://Test"; }
                public class RelicAssetProfile { public RelicAssetProfile(string IconPath = null) {} }

                public class TestRelicTutorial
                {
                    public RelicAssetProfile AssetProfile => new(
                        IconPath: $"{Entry.ResPath}/images/relics/{this.GetType().Name}.png");
                }
                """),
            projectDirectory);

        var diagnostics = (await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013").ToArray();
        Assert.DoesNotContain(diagnostics, d => d.GetMessage().Contains("TestRelicTutorial", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResourcePathCompletionSuggestsProjectRoots()
    {
        var projectDirectory = CreateTemporaryProjectDirectory();
        var source = Source("""
            public static void Build()
            {
                RitsuLibFramework.LoadIcon("res://$$");
            }
            """);
        var position = ExtractCaret(ref source);
        var project = CreateProject(
            source,
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "images", "relics", "TestRelic.png"), ""));

        var (provider, document, items) = await GetResourcePathCompletionItemsAsync(project, position);
        var item = Assert.Single(items, item => item.DisplayText == "Test/");
        var change = await provider.GetChangeAsync(document, item, null, CancellationToken.None);
        var updatedText = (await document.GetTextAsync()).WithChanges(change.TextChange).ToString();

        Assert.Contains("\"res://Test/\"", updatedText);
    }

    [Fact]
    public async Task ResourcePathCompletionSuggestsDirectoriesAndFiles()
    {
        var projectDirectory = CreateTemporaryProjectDirectory();
        var source = Source("""
            public static void Build()
            {
                RitsuLibFramework.LoadIcon("res://Test/$$");
            }
            """);
        var position = ExtractCaret(ref source);
        var project = CreateProject(
            source,
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "images", "relics", "TestRelic.png"), ""));

        var (_, _, directoryItems) = await GetResourcePathCompletionItemsAsync(project, position);
        Assert.Contains(directoryItems, item => item.DisplayText == "images/");

        source = Source("""
            public static void Build()
            {
                RitsuLibFramework.LoadIcon("res://Test/images/relics/$$");
            }
            """);
        position = ExtractCaret(ref source);
        project = CreateProject(
            source,
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "images", "relics", "TestRelic.png"), ""));

        var (_, _, fileItems) = await GetResourcePathCompletionItemsAsync(project, position);
        Assert.Contains(fileItems, item => item.DisplayText == "TestRelic.png");
    }

    [Fact]
    public async Task ResourcePathCompletionDoesNotTriggerForOrdinaryStrings()
    {
        var projectDirectory = CreateTemporaryProjectDirectory();
        var source = Source("""
            public static void Build()
            {
                var text = "res://$$";
            }
            """);
        var position = ExtractCaret(ref source);
        var project = CreateProject(
            source,
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "images", "relics", "TestRelic.png"), ""));

        var (_, _, items) = await GetResourcePathCompletionItemsAsync(project, position);

        Assert.Empty(items);
    }

    [Fact]
    public async Task ResourcePathCompletionOffersInterpolatedTemplatesAndRootSymbols()
    {
        var projectDirectory = CreateTemporaryProjectDirectory();
        var source = Source("""
            public static class ModEntry { public const string ResourceRoot = "res://Test"; }

            public static void Build()
            {
                RitsuLibFramework.LoadIcon($"res://$$");
            }
            """);
        var position = ExtractCaret(ref source);
        var project = CreateProject(
            source,
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "images", "relics", "TestContent.png"), ""));

        var (_, _, items) = await GetResourcePathCompletionItemsAsync(project, position);

        Assert.Contains(items, item => item.DisplayText == "res://Test/images/relics/{GetType().Name}.png");
        Assert.Contains(items, item => item.DisplayText == "{ModEntry.ResourceRoot}/images/relics/{GetType().Name}.png");
    }

    [Fact]
    public async Task ResourcePathCodeFixUsesCustomRootSymbolForInterpolatedPaths()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static class ModEntry { public const string ResourceRoot = "res://Test"; }
                public class RelicAssetProfile { public RelicAssetProfile(string IconPath = null) {} }

                public class TestRelic
                {
                    public RelicAssetProfile AssetProfile => new(
                        IconPath: $"images/relics/{GetType().Name}.png");
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "images", "relics", "TestRelic.png"), ""));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add res://");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("$\"{ModEntry.ResourceRoot}/images/relics/{GetType().Name}.png\"", text);
    }

    [Fact]
    public async Task ResourcePathCodeFixFallsBackToFullResPathWithoutRootSymbol()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public class RelicAssetProfile { public RelicAssetProfile(string IconPath = null) {} }

                public class TestRelic
                {
                    public RelicAssetProfile AssetProfile => new(
                        IconPath: $"images/relics/{GetType().Name}.png");
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "images", "relics", "TestRelic.png"), ""));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add res://");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("$\"res://Test/images/relics/{GetType().Name}.png\"", text);
    }

    [Fact]
    public async Task ResourcePathCodeFixAvoidsAmbiguousRootSymbols()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static class FirstRoot { public const string ResPath = "res://Test"; }
                public static class SecondRoot { public const string ResourceRoot = "res://Test"; }
                public class RelicAssetProfile { public RelicAssetProfile(string IconPath = null) {} }

                public class TestRelic
                {
                    public RelicAssetProfile AssetProfile => new(
                        IconPath: $"images/relics/{GetType().Name}.png");
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "images", "relics", "TestRelic.png"), ""));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add res://");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("$\"res://Test/images/relics/{GetType().Name}.png\"", text);
        Assert.DoesNotContain("FirstRoot.ResPath", text);
        Assert.DoesNotContain("SecondRoot.ResourceRoot", text);
    }

    [Fact]
    public async Task ResourcePathCodeFixCompletesMissingRootInExistingResPath()
    {
        using var culture = UseCulture("en-US");
        var projectDirectory = CreateTemporaryProjectDirectory();
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.LoadIcon("res://images/relics/TestRelic.png");
                }
                """),
            projectDirectory,
            AdditionalFile(Path.Combine(projectDirectory, "Test", "images", "relics", "TestRelic.png"), ""));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU013"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add res://");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("\"res://Test/images/relics/TestRelic.png\"", text);
    }
}
