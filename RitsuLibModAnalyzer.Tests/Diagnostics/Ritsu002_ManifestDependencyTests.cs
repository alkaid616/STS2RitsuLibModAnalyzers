namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu002DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU002");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsMissingManifestDependency()
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

        Assert.Contains(diagnostics, d => d.Id == "RITSU002");
    }
}
