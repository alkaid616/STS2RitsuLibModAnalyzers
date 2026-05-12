namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu012DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU012");

        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsPatchTargetMissingMember()
    {
        var diagnostics = await AnalyzeAsync(
            Source("""
                public sealed class TargetType { }

                public static void Build()
                {
                    DynamicPatchBuilder.FromMethod(typeof(PatchOwner), "MissingPatchMethod");
                    new DynamicPatchBuilder().AddMethod(typeof(TargetType), "MissingTargetMethod");
                    new DynamicPatchBuilder().AddPropertyGetter(typeof(TargetType), "MissingProperty");
                }
                """));

        Assert.Contains(diagnostics, d => d.Id == "RITSU012");
    }

    [Fact]
    public async Task PatchTargetCodeFixAddsTargetStub()
    {
        using var culture = UseCulture("en-US");
        var project = CreateProject(
            Source("""
                public sealed class TargetType { }

                public static void Build()
                {
                    new DynamicPatchBuilder().AddPropertyGetter(typeof(TargetType), "MissingProperty");
                }
                """));

        var diagnostic = Assert.Single((await AnalyzeProjectAsync(project)).Where(d => d.Id == "RITSU012"));
        var changed = await ApplyCodeFixAsync(project, diagnostic, "Generate required patch");
        var text = await GetDocumentTextAsync(changed);

        Assert.Contains("public object? MissingProperty => null;", text);
    }
}
