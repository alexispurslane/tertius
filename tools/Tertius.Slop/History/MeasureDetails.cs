namespace Tertius.Slop.History;

/// <summary>
/// What <c>measure --details</c> writes for the commit report (tools/commit_report.py): the whole report of the
/// measured tree, and the baseline, the noted commit whose note the report compares the tree's metrics with. The
/// baseline is null when no commit up to the parent has a note, or when the measure names no parent.
/// </summary>
public sealed record MeasureDetails(SlopReport Report, NotedCommit? Baseline);

/// <summary>A commit and its note. <paramref name="Commit"/> is the hash in its short form, as git prints it.</summary>
public sealed record NotedCommit(string Commit, SlopNote Note);
