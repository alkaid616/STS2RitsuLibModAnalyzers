namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu008DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU008");

        Assert.Equal(DiagnosticSeverity.Info, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsInvalidIdShape()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void RegisterIds()
                {
                    ModKeywordRegistry.For(MainFile.ModId).RegisterOwned("Bad Stem");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU008" && d.GetMessage().Contains("Bad Stem"));
    }
}
