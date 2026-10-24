using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style.Tests;

/// <summary>Runs the style analyzer on one source file.</summary>
internal static class Analysis
{
    /// <summary>The ids of the diagnostics that the analyzer reports on <paramref name="source"/>, in source order.</summary>
    public static async Task<IReadOnlyList<string>> Ids(string source) =>
        [.. (await Diagnostics(source)).Select(diagnostic => diagnostic.Id)];

    /// <summary>The diagnostics that the analyzer reports on <paramref name="source"/>, in source order.</summary>
    public static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        ImmutableArray<Diagnostic> found = await TestCompilation.Of(source).WithAnalyzers([new StyleAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. found.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)];
    }
}
