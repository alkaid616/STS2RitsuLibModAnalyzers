namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu011DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU011");

        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsPatchContractMissingRequiredMembers()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class PatchOwner : IPatchMethod { }
                public sealed class PatchGroup : IModPatches { }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU011");
    }

    [Fact]
    public async Task PatchContractCodeFixAddsRequiredMembers()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public sealed class PatchOwner : IPatchMethod { }
                """));

        var diagnostic = (await AnalyzeProjectAsync(project)).First(d => d.Id == "RITSU011");
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Generate required patch");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("public static string PatchId", text);
        Assert.Contains("public static STS2RitsuLib.Patching.Models.ModPatchTarget[] GetTargets()", text);
    }
}
