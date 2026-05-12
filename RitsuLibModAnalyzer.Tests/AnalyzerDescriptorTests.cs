namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void SupportsFullDiagnosticSuite()
    {
        var diagnosticIds = new AnalyzerUnderTest().SupportedDiagnostics
            .Select(descriptor => descriptor.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            Enumerable.Range(1, 25).Select(number => $"RITSU{number:000}"),
            diagnosticIds);
    }
}
