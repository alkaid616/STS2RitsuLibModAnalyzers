namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu002DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU002");

        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsAncientDialogueRMixedWithinSameSequence()
    {
        var diagnostics = await AnalyzeAsync(
            Source(""),
            AdditionalJson(@"C:\mod\localization\eng\ancients.json", """
            {
              "THE_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-0.ancient": "first",
              "THE_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-1r.char": "second"
            }
            """));

        var mixed = Assert.Single(diagnostics.Where(d => d.Id == "RITSU002"));
        Assert.Equal(DiagnosticSeverity.Warning, mixed.Severity);
        Assert.Contains("THE_ARCHITECT.talk.MANOSABA_LIN_CHARACTER_HERO.0-", mixed.GetMessage());
    }

    [Fact]
    public async Task DoesNotReportAncientDialogueWhenUniformlyUsingR()
    {
        var diagnostics = await AnalyzeAsync(
            Source(""),
            AdditionalJson(@"C:\mod\localization\eng\ancients.json", """
            {
              "X.talk.Y.0-0r.ancient": "a",
              "X.talk.Y.0-1r.char": "b",
              "X.talk.Y.0-2r.ancient": "c"
            }
            """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU002");
    }

    [Fact]
    public async Task DoesNotReportAncientDialogueWhenUniformlyOmittingR()
    {
        var diagnostics = await AnalyzeAsync(
            Source(""),
            AdditionalJson(@"C:\mod\localization\eng\ancients.json", """
            {
              "X.talk.Y.0-0.ancient": "a",
              "X.talk.Y.0-1.char": "b",
              "X.talk.Y.0-2.ancient": "c"
            }
            """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU002");
    }

    [Fact]
    public async Task AllowsDifferentSegmentsToUseDifferentRStyleIndependently()
    {
        // Segment 0 uniformly uses r, segment 1 uniformly omits r; both legal in isolation.
        var diagnostics = await AnalyzeAsync(
            Source(""),
            AdditionalJson(@"C:\mod\localization\eng\ancients.json", """
            {
              "X.talk.Y.0-0r.ancient": "a",
              "X.talk.Y.0-1r.char": "b",
              "X.talk.Y.1-0.ancient": "c",
              "X.talk.Y.1-1.char": "d"
            }
            """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU002");
    }

    [Fact]
    public async Task IgnoresAncientDialogueMetadataKeysWhenJudgingRMix()
    {
        // .sfx, .next, -attack, -visit are siblings, not part of the r-uniformity rule.
        var diagnostics = await AnalyzeAsync(
            Source(""),
            AdditionalJson(@"C:\mod\localization\eng\ancients.json", """
            {
              "X.talk.Y.0-0r.ancient": "a",
              "X.talk.Y.0-0r.ancient.sfx": "event:/sfx/ui",
              "X.talk.Y.0-0r.next": "Continue",
              "X.talk.Y.0-attack": "Both",
              "X.talk.Y.0-visit": "3",
              "X.talk.Y.0-1r.char": "b"
            }
            """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU002");
    }
}
