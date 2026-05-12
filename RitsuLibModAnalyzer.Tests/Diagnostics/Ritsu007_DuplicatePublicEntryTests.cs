namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu007DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU007");

        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsDuplicatePublicEntry()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class CardPool { }

                [RegisterCard(typeof(CardPool), FullPublicEntry = "same_entry")]
                public sealed class FirstCard { }

                [RegisterCard(typeof(CardPool), FullPublicEntry = "same_entry")]
                public sealed class SecondCard { }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU007");
    }
}
