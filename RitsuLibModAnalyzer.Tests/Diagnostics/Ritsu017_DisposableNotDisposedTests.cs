namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu017DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU017");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsUndisposedAudioHandle()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void PlayAudio(IGameAudio audio)
                {
                    audio.PlayLoop(AudioSource.Event("event:/sfx/test"));
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU017" && d.GetMessage().Contains("PlayLoop"));
    }

    [Fact]
    public async Task DoesNotReportWhenHandleIsAssigned()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void PlayAudio(IGameAudio audio)
                {
                    var handle = audio.PlayLoop(AudioSource.Event("event:/sfx/test"));
                }
                """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU017");
    }

    [Fact]
    public async Task ReportsUndisposedSubscribeLifecycle()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void Subscribe()
                {
                    RitsuLibFramework.SubscribeLifecycle<int>(e => { });
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU017" && d.GetMessage().Contains("SubscribeLifecycle"));
    }

    // RITSU018: ContentPackBuilder.For() not applied
    [Fact]
    public async Task DisposableCodeFixWrapsInUsing()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public static void PlayAudio(IGameAudio audio)
                {
                    audio.PlayLoop(AudioSource.Event("event:/sfx/test"));
                }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU017"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Wrap in using");
        var text = await GetDocumentTextAsync(changed);
        Assert.Contains("using", text);
        Assert.Contains("_playLoop", text);
    }
}
