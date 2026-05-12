namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu009DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU009");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsSettingsBindingMissingReadMethod()
    {
        using var culture = UseCulture("en-US");
        var diagnostics = await AnalyzeAsync(
            Source("""
                [ModSettingsBinding(ReadUsing = "MissingRead")]
                public sealed class SettingsModel { }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU009" && d.GetMessage().Contains("MissingRead"));
    }

    [Fact]
    public async Task ReportsSettingsSliderInvalidRange()
    {
        using var culture = UseCulture("en-US");
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.RegisterModSettings(MainFile.ModId, "Settings", "main")
                        .AddSection("general")
                        .AddSlider("volume", "Volume", "", 10, 1, 0);
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU009" && d.GetMessage().Contains("min must be less"));
    }

    [Fact]
    public async Task ReportsSettingsChoiceWithoutOptions()
    {
        using var culture = UseCulture("en-US");
        var diagnostics = await AnalyzeAsync(
            Source("""
                public static void Build()
                {
                    RitsuLibFramework.RegisterModSettings(MainFile.ModId, "Settings", "main")
                        .AddSection("general")
                        .AddChoice("mode", "Mode", "", new string[] { });
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU009" && d.GetMessage().Contains("at least one option"));
    }

    [Fact]
    public async Task SettingsCodeFixAddsCallbackStub()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                [ModSettingsBinding(ReadUsing = "MissingRead")]
                public sealed class SettingsModel { }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU009" && d.GetMessage().Contains("MissingRead")));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Generate settings");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("private static void MissingRead()", text);
    }
}
