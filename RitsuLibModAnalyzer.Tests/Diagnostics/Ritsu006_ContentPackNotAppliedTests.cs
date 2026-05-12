namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu006DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU006");

        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsContentPackNotApplied()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class CardPool { }
                public sealed class MissingApplyCard { }

                public static void Build()
                {
                    RitsuLibFramework.CreateContentPack(MainFile.ModId)
                        .Card<CardPool, MissingApplyCard>();
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU006");
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
}
