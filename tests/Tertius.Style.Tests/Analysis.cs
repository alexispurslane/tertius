using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style.Tests;

/// <summary>
/// Runs the style analyzer on one source file, and keeps the diagnostics of the one rule that a test pins, so that a
/// test source need not follow every other rule.
/// </summary>
internal static class Analysis
{
    /// <summary>
    /// The ids of the diagnostics of <paramref name="rule"/> that the analyzer reports on <paramref name="source"/>, in
    /// source order.
    /// </summary>
    public static async Task<IReadOnlyList<string>> Ids(string rule, string source) =>
        [.. (await Diagnostics(rule, source)).Select(diagnostic => diagnostic.Id)];

    /// <summary>
    /// The diagnostics of <paramref name="rule"/> that the analyzer reports on <paramref name="source"/>, in source
    /// order.
    /// </summary>
    public static Task<ImmutableArray<Diagnostic>> Diagnostics(string rule, string source) =>
        Diagnostics(rule, TestCompilation.Of(source));

    /// <summary>
    /// The diagnostics of <paramref name="rule"/> that the analyzer reports on <paramref name="compilation"/>.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> Diagnostics(string rule, CSharpCompilation compilation)
    {
        ImmutableArray<Diagnostic> found = await compilation
            .WithAnalyzers([new StyleAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        found
            .Where(diagnostic => diagnostic.Id == "AD0001")
            .Select(diagnostic => diagnostic.GetMessage())
            .Should()
            .BeEmpty("an analyzer that throws reports nothing for the code that it fails on");
        return
        [
            .. found
                .Where(diagnostic => diagnostic.Id == rule)
                .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start),
        ];
    }
}
