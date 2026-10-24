using System.Collections.Immutable;
using System.Xml.Linq;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style.Tests;

/// <summary>The set of rules that the style analyzer has.</summary>
public sealed class StyleRulesTests
{
    [Fact]
    public void TheAnalyzerHasFourteenRulesThatAreWarningsOnByDefault()
    {
        ImmutableArray<DiagnosticDescriptor> rules = new StyleAnalyzer().SupportedDiagnostics;

        rules.Select(rule => rule.Id).Should().Equal(Enumerable.Range(1, 14).Select(number => $"TS{number:D4}"));
        rules.Should().OnlyContain(
            rule => rule.DefaultSeverity == DiagnosticSeverity.Warning && rule.IsEnabledByDefault,
            "a rule is a warning, as a compiler warning is, and TreatWarningsAsErrors makes it an error");
    }

    [Fact]
    public async Task AHitIsAnErrorWhenWarningsAreErrorsAsInEveryProject()
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        CSharpCompilation compilation = TestCompilation.Of("""
                                                           public static class C
                                                           {
                                                               public static (int, double) Pair;
                                                           }
                                                           """);
        CSharpCompilation strict = compilation.WithOptions(
            compilation.Options.WithGeneralDiagnosticOption(ReportDiagnostic.Error));

        ImmutableArray<Diagnostic> found = await strict.WithAnalyzers([new StyleAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancel);

        found.Should().ContainSingle().Which.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task EveryProjectTreatsWarningsAsErrorsAndTakesTheStyleAnalyzer()
    {
        XDocument props = XDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(Repository.Root(), "Directory.Build.props"), TestContext.Current.CancellationToken));

        props.Descendants("TreatWarningsAsErrors").Should().ContainSingle()
            .Which.Value.Should().Be("true", "a warning of the style analyzer then fails the build");
        props.Descendants("TreatWarningsAsErrors").Single().Parent?.Attribute("Condition").Should()
            .BeNull("no project or configuration turns it off");
        props.Descendants("ProjectReference")
            .Where(reference => reference.Attribute("Include")?.Value.EndsWith("Tertius.Style.csproj") == true)
            .Should().ContainSingle()
            .Which.Attribute("OutputItemType")?.Value.Should().Be("Analyzer");
    }

    [Fact]
    public void TheCodeFixFixesTheRuleOfUnnamedArguments() =>
        new NameArgumentsFix().FixableDiagnosticIds.Should().Equal("TS0002");

    [Fact]
    public void ThePropertyPatternFixFixesTheRuleOfPositionalReads() =>
        new PropertyPatternFix().FixableDiagnosticIds.Should().Equal("TS0004");

    [Fact]
    public void TheInliningFixFixesTheRuleOfDefaultMethods() =>
        new InlineDefaultMethodFix().FixableDiagnosticIds.Should().Equal("TS0005");

    [Fact]
    public void TheSealingFixFixesTheRuleOfUnsealedClasses() =>
        new SealClassFix().FixableDiagnosticIds.Should().Equal("TS0006");

    [Fact]
    public void EveryMessageNamesTheSectionOfTheGuideThatItEnforces() =>
        new StyleAnalyzer().SupportedDiagnostics.Should().OnlyContain(
            rule => rule.MessageFormat.ToString().Contains("(STYLE.md, \""),
            "a reader of the build error finds the reason in the guide");
}
