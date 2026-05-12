namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu022DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU022");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

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
}
