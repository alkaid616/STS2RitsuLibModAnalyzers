namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu005DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU005");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsMissingGodotScriptsRegistration()
    {
        var diagnostics = await AnalyzeAsync(
            SourceWithoutRegistration("""
                public sealed class SceneNode : Godot.Node { }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU005");
    }

    [Fact]
    public async Task GodotScriptsCodeFixInsertsInitializerStatement()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            SourceWithoutRegistration("""
                public sealed class SceneNode : Godot.Node { }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU005"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Insert EnsureGodotScriptsRegistered");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered", text);
    }
}
