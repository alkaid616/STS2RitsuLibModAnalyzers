namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu018DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU018");

        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

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
}
