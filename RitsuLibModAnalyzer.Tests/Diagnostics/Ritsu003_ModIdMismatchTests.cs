namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu003DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU003");

        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsModIdMismatch()
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

        Assert.Contains(diagnostics, d => d.Id == "RITSU003" && d.GetMessage().Contains("OtherMod"));
    }
}
