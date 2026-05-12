namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu024DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU024");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }
}
