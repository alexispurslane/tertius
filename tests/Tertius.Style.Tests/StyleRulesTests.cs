using System.Collections.Immutable;
using System.Xml.Linq;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style.Tests;

/// <summary>The set of rules that the style analyzer has.</summary>
public class StyleRulesTests
{
    [Fact]
    public void TheAnalyzerHasThreeRulesThatAreWarningsOnByDefault()
    {
        ImmutableArray<DiagnosticDescriptor> rules = new StyleAnalyzer().SupportedDiagnostics;

        rules.Select(rule => rule.Id).Should().Equal(["TS0001", "TS0002", "TS0003"]);
        rules.Should().OnlyContain(rule => rule.DefaultSeverity == DiagnosticSeverity.Warning && rule.IsEnabledByDefault,
            "a rule is a warning, as a compiler warning is, and TreatWarningsAsErrors makes it an error");
    }

    [Fact]
    public async Task AHitIsAnErrorWhenWarningsAreErrorsAsInEveryProject()
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        CSharpCompilation compilation = TestCompilation.Of("""
                                                           public class C
                                                           {
                                                               public (int, double) Pair;
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
        new NameArgumentsFix().FixableDiagnosticIds.Should().Equal(["TS0002"]);
}
