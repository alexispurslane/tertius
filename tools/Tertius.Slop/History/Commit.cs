using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tertius.Slop.History;

/// <summary>A commit as the slop history reads it from <c>git log</c>, with its note if it has one.</summary>
public sealed partial record Commit
{
    /// <summary>The full hash.</summary>
    public required string Sha { get; init; }

    /// <summary>The full hash of the first parent, or null for a commit without parents.</summary>
    public required string? FirstParent { get; init; }

    /// <summary>The committer date.</summary>
    public required DateTimeOffset Date { get; init; }

    /// <summary>The whole message.</summary>
    public required string Message { get; init; }

    /// <summary>The slop note, or null for a commit that no hook measured.</summary>
    public required SlopNote? Note { get; init; }

    /// <summary>The ticket ids that the message names, such as <c>ter-2x99</c>, in order and without repeats.</summary>
    public IReadOnlyList<string> Tickets => [.. TicketId().Matches(Message).Select(match => match.Value).Distinct()];

    /// <summary>The first line of the message.</summary>
    public string Subject => Message.Split('\n')[0].TrimEnd();

    /// <summary>The hash in its short form of 7 characters, as git prints it.</summary>
    public string ShortSha => Sha[..Math.Min(7, Sha.Length)];

    // A ticket id of the tk tickets: "ter-" and 4 letters or digits, not part of a longer word.
    [GeneratedRegex("(?<![a-z0-9-])ter-[a-z0-9]{4}(?![a-z0-9])")]
    private static partial Regex TicketId();
}

/// <summary>Commits with their slop notes, newest first, as <c>git log</c> gives them.</summary>
public sealed record SlopHistory(IReadOnlyList<Commit> Commits)
{
    /// <summary>
    /// The noted commits as CSV, one row each, with the columns commit, date, branch, tickets (separated by spaces),
    /// erosion, high CC mass and function count, verbosity, verbose lines and sloc. A commit without a note has no row.
    /// </summary>
    public string Csv
    {
        get
        {
            var csv = new StringBuilder(
                "commit,date,branch,tickets,erosion,highCcMass,highCcFunctionCount,verbosity,verboseLines,sloc,"
                    + "tcc,sharedMethodPairs,possibleMethodPairs,depth,wmc,apiEntries,typeCount,classMetricVersion,references,bindingErrorCount,"
                    + "cohesionIncludedTypeCount,cohesionNotApplicableTypeCount,cohesionIncompleteTypeCount,depthIncludedTypeCount,depthNotApplicableTypeCount,unresolvedStateAccessCount\n"
            );
            foreach (Commit commit in Commits)
            {
                if (commit.Note is not { } note)
                    continue;
                string date = commit.Date.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
                csv.Append(
                    CultureInfo.InvariantCulture,
                    $"{commit.Sha},{date},{Cell(note.Branch)},{Cell(string.Join(' ', commit.Tickets))},"
                );
                csv.Append(
                    CultureInfo.InvariantCulture,
                    $"{note.Erosion},{note.HighCcMass},{note.HighCcFunctionCount},{note.Verbosity},{note.VerboseLines},{note.Sloc}"
                );
                AppendClassMetrics(csv, note.ClassMetrics);
                csv.AppendLine();
            }

            return csv.ToString();
        }
    }

    private static void AppendClassMetrics(StringBuilder csv, ClassMetricSummary? metrics)
    {
        if (metrics is null)
        {
            csv.Append(',', 16);
            return;
        }
        csv.Append(
            CultureInfo.InvariantCulture,
            $",{metrics.TccText},{metrics.Tcc.Numerator},{metrics.Tcc.Denominator},{(metrics.Depth is { } depth ? ClassMetricSummary.Show(depth) : "")},{metrics.Depth?.Numerator},{metrics.Depth?.Denominator},{metrics.TypeCount},{Cell(metrics.Provenance.Version)},{Cell(metrics.Provenance.References)},{metrics.BindingErrorCount},"
                + $"{metrics.CohesionPopulation?.Included},{metrics.CohesionPopulation?.NotApplicable},{metrics.CohesionPopulation?.Incomplete},{metrics.DepthPopulation?.Included},{metrics.DepthPopulation?.NotApplicable},{metrics.UnresolvedStateAccessCount}"
        );
    }

    // A text cell of CSV: quoted, with its quotes doubled, when it holds a comma, a quote or a line break.
    private static string Cell(string text) =>
        text.AsSpan().IndexOfAny(",\"\n") >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
}

/// <summary>How much one commit changed a metric, from the nearest note before it.</summary>
public readonly record struct CommitRaise(Commit Commit, double Change);
