namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu019DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU019");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsAudioSourceEventMissingPrefix()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void PlayAudio()
                {
                    AudioSource.Event("sfx/block");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU019" && d.GetMessage().Contains("event:/"));
    }

    [Fact]
    public async Task DoesNotReportAudioSourceEventWithPrefix()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void PlayAudio()
                {
                    AudioSource.Event("event:/sfx/block");
                }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU019");
    }

    [Fact]
    public async Task ReportsAudioSourceSnapshotMissingPrefix()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void PlaySnapshot()
                {
                    AudioSource.Snapshot("snapshot:/music/pause");
                }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU019");
    }

    // RITSU020: ModInterop attribute shape
    [Fact]
    public async Task AudioSourcePathCodeFixAddsPrefix()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public static void PlayAudio()
                {
                    AudioSource.Event("sfx/block");
                }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU019"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Add event:/");
        var text = await GetDocumentTextAsync(changed);
        Assert.Contains("event:/sfx/block", text);
    }
}
