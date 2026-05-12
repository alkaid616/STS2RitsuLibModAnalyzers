namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu021DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU021");

        Assert.Equal(DiagnosticSeverity.Info, descriptor.DefaultSeverity);
    }

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
}
