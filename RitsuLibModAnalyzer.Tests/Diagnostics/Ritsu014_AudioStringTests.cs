namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu014DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU014");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsAudioStringContracts()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.RegisterBank("soundtrack");
                    RitsuLibFramework.PlayEvent("foo:/bad_event");
                    RitsuLibFramework.SetBus("foo:/bad_bus");
                    RitsuLibFramework.SetGuid("not-a-guid");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU014");
    }
}
