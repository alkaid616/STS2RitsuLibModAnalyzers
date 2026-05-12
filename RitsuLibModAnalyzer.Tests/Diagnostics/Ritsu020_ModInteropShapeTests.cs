namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu020DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU020");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsModInteropEmptyModId()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [ModInterop("")]
                public sealed class MyInterop { }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU020");
    }

    [Fact]
    public async Task DoesNotReportModInteropWithValidId()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                [ModInterop("com.example.other-mod")]
                public sealed class MyInterop { }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU020");
    }

    // RITSU021: Character template legacy override
}
