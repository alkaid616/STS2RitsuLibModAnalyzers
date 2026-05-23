namespace RitsuLibModAnalyzer.Tests;

public sealed partial class RitsuLibModAnalyzerTests
{
    [Fact]
    public void Ritsu003DescriptorHasExpectedSeverity()
    {
        var descriptor = GetSupportedDiagnostic("RITSU003");

        Assert.Equal(DiagnosticSeverity.Info, descriptor.DefaultSeverity);
    }

    [Fact]
    public async Task ReportsUnknownLocalizationTableName()
    {
        // "cardz" is a likely typo of "cards" — game LocManager will silently skip the file.
        var diagnostics = await AnalyzeAsync(
            Source(""),
            AdditionalJson(@"C:\mod\localization\eng\cardz.json", "{}"));

        var unknown = Assert.Single(diagnostics.Where(d => d.Id == "RITSU003"));
        Assert.Equal(DiagnosticSeverity.Info, unknown.Severity);
        Assert.Contains("cardz", unknown.GetMessage());
        Assert.Contains("cardz.json", unknown.GetMessage());
    }

    [Theory]
    [InlineData("cards")]
    [InlineData("relics")]
    [InlineData("potions")]
    [InlineData("powers")]
    [InlineData("characters")]
    [InlineData("events")]
    [InlineData("ancients")]
    [InlineData("encounters")]
    [InlineData("acts")]
    [InlineData("monsters")]
    [InlineData("orbs")]
    [InlineData("enchantments")]
    [InlineData("afflictions")]
    [InlineData("card_keywords")]
    [InlineData("static_hover_tips")]
    [InlineData("epochs")]
    [InlineData("achievements")]
    [InlineData("stories")]
    public async Task DoesNotReportKnownLocalizationTableName(string tableName)
    {
        var diagnostics = await AnalyzeAsync(
            Source(""),
            AdditionalJson($@"C:\mod\localization\eng\{tableName}.json", "{}"));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU003");
    }

    [Fact]
    public async Task DoesNotReportI18NFileLayout()
    {
        // localization/<lang>.json is the I18N (one-segment) layout, not a LocTable.
        var diagnostics = await AnalyzeAsync(
            Source(""),
            AdditionalJson(@"C:\mod\localization\eng.json", "{}"));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU003");
    }

    [Fact]
    public async Task DoesNotReportI18NBridgeFeatureSubdirectoryLayout()
    {
        // localization/<feature>/<lang>.json with feature being NOT a language code is the I18N bridge layout.
        var diagnostics = await AnalyzeAsync(
            Source(""),
            AdditionalJson(@"C:\mod\localization\settings\eng.json", "{}"));

        Assert.DoesNotContain(diagnostics, d => d.Id == "RITSU003");
    }
}
