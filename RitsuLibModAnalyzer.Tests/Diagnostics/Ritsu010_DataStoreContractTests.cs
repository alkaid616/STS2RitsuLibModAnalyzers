namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu010DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU010");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsDataStoreFilePathContract()
    {
        using var culture = UseCulture("en-US");
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void Build()
                {
                    ModDataStore.Register<int>("bad key", "save.txt");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU010" && d.GetMessage().Contains("save.txt"));
    }

    [Fact]
    public async Task ReportsDuplicateDataStoreKey()
    {
        using var culture = UseCulture("en-US");
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void Build()
                {
                    ModDataStore.Register<int>("bad key", "save.txt");
                    ModDataStore.Register<int>("bad key", "save.txt");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU010" && d.GetMessage().Contains("Duplicate ModDataStore key"));
    }
}
