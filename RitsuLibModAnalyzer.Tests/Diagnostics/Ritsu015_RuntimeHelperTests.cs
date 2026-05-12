namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu015DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU015");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsRuntimeHelperContracts()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.RegisterFreePlayBinding("");
                    RuntimeHotkeyService.Register("Ctrl++");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU015");
    }

    [Fact]
    public async Task TodoFallbackCodeFixInsertsDiagnosticSnippet()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public static void UseRuntime()
                {
                    RuntimeHotkeyService.Register("Ctrl++");
                }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU015"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Insert RitsuLib TODO");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("TODO RitsuLib analyzer:", text);
    }

    [Fact]
    public async Task HotkeyArrayBindingDoesNotTriggerRitsu015()
    {
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RuntimeHotkeyService.Register(
                        ["F5", "Ctrl+Shift+R"],
                        () => { },
                        new RuntimeHotkeyOptions { Id = "my_mod_refresh" });
                }
                """));

        var diagnostics = await AnalyzeProjectAsync(project);
        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU015");
    }

    [Fact]
    public async Task HotkeyArrayWithInvalidElementTriggersRitsu015()
    {
        var project = CreateProject(
            Source("""
                public static void Build()
                {
                    RuntimeHotkeyService.Register(
                        ["F5", "Ctrl++"],
                        () => { },
                        new RuntimeHotkeyOptions { Id = "my_mod_refresh" });
                }
                """));

        var diagnostics = await AnalyzeProjectAsync(project);
        var ritsu015 = Assert.Single(diagnostics.Where(d => d.Id == "RITSU015"));
        Assert.Contains("Ctrl++", ritsu015.GetMessage());
    }
}
