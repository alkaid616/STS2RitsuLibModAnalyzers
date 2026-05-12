namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu016DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU016");

        Assert.Equal(DiagnosticSeverity.Info, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsLegacyPoolHook()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public abstract class LegacyPoolBase
                {
                    public virtual System.Collections.Generic.IEnumerable<Type> CardTypes => Array.Empty<Type>();
                }

                public sealed class LegacyPool : LegacyPoolBase
                {
                    public override System.Collections.Generic.IEnumerable<Type> CardTypes => Array.Empty<Type>();
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU016");
    }
}
