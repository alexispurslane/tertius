using System.Text;
using Tertius.Slop.Rules;

namespace Tertius.Slop;

/// <summary>
/// The verbosity of a codebase (docs/plans/code-quality-tooling.md, decision 17), after
/// https://earendil.com/posts/measuring-code-sloppiness/: the share of its lines of code that a rule flags or that
/// jscpd finds in a clone. A line counts once, however many findings and clones cover it. A blank line never counts. A
/// comment line counts when a finding covers it, although the lines of code leave comments out.
/// </summary>
public sealed record VerbosityReport
{
    /// <summary>The number of findings in <see cref="TopFindings"/>.</summary>
    public const int TopCount = 20;

    /// <summary>The verbosity: <see cref="VerboseLines"/> divided by <see cref="Sloc"/>. 0 for a codebase without code.</summary>
    public required double Verbosity { get; init; }

    /// <summary>The number of lines that a finding or a clone covers.</summary>
    public required int VerboseLines { get; init; }

    /// <summary>The number of lines that a finding covers.</summary>
    public required int FlaggedLines { get; init; }

    /// <summary>The number of lines that a clone covers.</summary>
    public required int CloneLines { get; init; }

    /// <summary>The number of lines that hold code in every file: the lines of code of the erosion.</summary>
    public required int Sloc { get; init; }

    /// <summary>The number of files.</summary>
    public required int FileCount { get; init; }

    /// <summary>What each rule found, every rule ordered by id.</summary>
    public required IReadOnlyList<RuleCount> Rules { get; init; }

    /// <summary>
    /// The <see cref="TopCount"/> findings that cover the most lines, then ordered by file, line and rule, so that the
    /// report is the same on every run.
    /// </summary>
    public required IReadOnlyList<Finding> TopFindings { get; init; }

    /// <summary>The report of <paramref name="codebase"/>, with the clone lines of <paramref name="clones"/>.</summary>
    public static VerbosityReport Of(Codebase codebase, Clones clones)
    {
        RuleContext context = RuleContext.Of(codebase.Files);
        FileVerbosity[] files =
        [
            .. codebase.Files.AsParallel().AsOrdered().Select(file => FileVerbosity.Of(file, context, clones))
        ];
        Finding[] findings = [.. files.SelectMany(file => file.Findings)];
        int sloc = files.Sum(file => file.Sloc);
        int verboseLines = files.Sum(file => file.Flagged.Union(file.Cloned).Count());
        return new()
        {
            Verbosity = sloc > 0 ? (double)verboseLines / sloc : 0,
            VerboseLines = verboseLines,
            FlaggedLines = files.Sum(file => file.Flagged.Count),
            CloneLines = files.Sum(file => file.Cloned.Count),
            Sloc = sloc,
            FileCount = files.Length,
            Rules =
            [
                .. IRule.All.OrderBy(rule => rule.Id, StringComparer.Ordinal).Select(rule => new RuleCount
                {
                    Id = rule.Id,
                    Message = rule.Message,
                    Source = rule.Source,
                    Findings = findings.Count(finding => finding.Rule == rule.Id),
                    Lines = files.Sum(file => file.LinesOf(rule.Id).Count)
                })
            ],
            TopFindings =
            [
                .. findings.OrderByDescending(finding => finding.EndLine - finding.Line)
                    .ThenBy(finding => finding.File, StringComparer.Ordinal)
                    .ThenBy(finding => finding.Line)
                    .ThenBy(finding => finding.Rule, StringComparer.Ordinal)
                    .Take(TopCount)
            ]
        };
    }

    /// <summary>The report as a few lines of text for a person.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine(
            $"Verbosity {Verbosity:F3}: {VerboseLines} of {Sloc} lines of code are flagged ({FlaggedLines}) or " +
            $"cloned ({CloneLines}), in {FileCount} files.");
        text.AppendLine("Rules that found something:");
        foreach (RuleCount rule in Rules.Where(rule => rule.Findings > 0).OrderByDescending(rule => rule.Lines))
            text.AppendLine($"{rule.Findings,6} {rule.Lines,6} lines  {rule.Id}: {rule.Message}");
        text.AppendLine("Longest findings:");
        foreach (Finding finding in TopFindings)
            text.AppendLine(
                $"{finding.EndLine - finding.Line + 1,6} lines  {finding.Rule} ({finding.File}:{finding.Line})");
        return text.ToString();
    }

    // The lines of one file: the findings and their lines, the clone lines, and the lines of code. Every line set holds
    // only lines that are not blank.
    private sealed class FileVerbosity
    {
        public required Finding[] Findings { get; init; }
        public required IReadOnlySet<int> Text { get; init; }
        public required HashSet<int> Flagged { get; init; }
        public required HashSet<int> Cloned { get; init; }
        public required int Sloc { get; init; }

        public static FileVerbosity Of(SourceFile file, RuleContext context, Clones clones)
        {
            IReadOnlySet<int> text = file.TextLines;
            Finding[] findings = [.. file.Findings(context)];
            return new()
            {
                Findings = findings,
                Text = text,
                Flagged = [.. findings.SelectMany(Covered).Where(text.Contains)],
                Cloned = [.. clones.LinesIn(file.Path).Where(text.Contains)],
                Sloc = file.CodeLines.Count
            };
        }

        public HashSet<int> LinesOf(string rule) =>
            [.. Findings.Where(finding => finding.Rule == rule).SelectMany(Covered).Where(Text.Contains)];

        private static IEnumerable<int> Covered(Finding finding) =>
            Enumerable.Range(finding.Line, finding.EndLine - finding.Line + 1);
    }
}

/// <summary>What one rule found in a codebase.</summary>
public readonly record struct RuleCount
{
    /// <summary>The id of the rule.</summary>
    public required string Id { get; init; }

    /// <summary>What the rule flags and what to write instead.</summary>
    public required string Message { get; init; }

    /// <summary>Where the rule comes from: STYLE.md, the user, or a rule of scb-check.</summary>
    public required string Source { get; init; }

    /// <summary>The number of findings.</summary>
    public required int Findings { get; init; }

    /// <summary>The number of lines that the findings cover, blank lines left out.</summary>
    public required int Lines { get; init; }
}
