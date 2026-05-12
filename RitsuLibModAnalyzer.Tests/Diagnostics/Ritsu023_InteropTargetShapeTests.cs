namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu023DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU023");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }
}
