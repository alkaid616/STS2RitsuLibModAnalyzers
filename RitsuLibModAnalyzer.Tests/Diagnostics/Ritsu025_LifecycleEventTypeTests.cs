namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu025DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU025");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsLifecycleEventNonSealedType()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public class MyEvent { }
                public static void Subscribe()
                {
                    RitsuLibFramework.SubscribeLifecycleOnce<MyEvent>(e => { });
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU025");
    }

    [Fact]
    public async Task DoesNotReportLifecycleEventSealedType()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class MyEvent { }
                public static void Subscribe()
                {
                    RitsuLibFramework.SubscribeLifecycleOnce<MyEvent>(e => { });
                }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU025");
    }

    // Code fix tests for new diagnostics
}
