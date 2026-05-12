namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu004DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU004");

        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsMissingRegistration()
    {
        var diagnostics = await AnalyzeAsync(
            SourceWithoutRegistration("""
                public sealed class CardPool { }

                [RegisterCard(typeof(CardPool))]
                public sealed class MyCard { }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU004");
    }

    [Fact]
    public async Task RegistrationCodeFixInsertsInitializerStatement()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            SourceWithoutRegistration("""
                [RegisterOwnedCardKeyword("hiro")]
                private sealed class KeywordMarker { }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU004"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Insert RegisterModAssembly");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("STS2RitsuLib.Interop.ModTypeDiscoveryHub.RegisterModAssembly", text);
    }
}
