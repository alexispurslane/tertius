using System.Globalization;
using System.Text;

namespace Tertius.Slop.History;

/// <summary>
/// What the pre-push hook prints about the slop metrics (docs/plans/code-quality-tooling.md, decision 18): the metrics
/// at the last push against now, and the pushed commits whose notes raised a metric the most. It is a report, so it
/// never stops a push.
/// </summary>
public sealed record PushSummary
{
    /// <summary>The number of commits that each list of raises names at most.</summary>
    public const int RaiseCount = 3;

    /// <summary>The note at the last push, or null when no commit before the push has one.</summary>
    public required SlopNote? LastPush { get; init; }

    /// <summary>The note of the pushed tip.</summary>
    public required SlopNote Now { get; init; }

    /// <summary>The pushed commits that raised the erosion, the largest raise first.</summary>
    public required IReadOnlyList<CommitRaise> ErosionRaises { get; init; }

    /// <summary>The pushed commits that raised the verbosity, the largest raise first.</summary>
    public required IReadOnlyList<CommitRaise> VerbosityRaises { get; init; }

    /// <summary>
    /// The summary of a push. <paramref name="pushed"/> holds the pushed commits on the first-parent line, oldest first.
    /// A noted commit raises a metric by the change from the nearest note before it: the note of an earlier pushed
    /// commit, or <paramref name="lastPush"/>. A commit without a note changed no code, or a hook did not measure it.
    /// </summary>
    public static PushSummary Of(SlopNote? lastPush, SlopNote now, IReadOnlyList<Commit> pushed)
    {
        var erosion = new List<CommitRaise>();
        var verbosity = new List<CommitRaise>();
        SlopNote? before = lastPush;
        foreach (Commit commit in pushed)
        {
            if (commit.Note is not { } note)
                continue;
            if (before is not null)
            {
                erosion.Add(new(commit, note.Erosion - before.Erosion));
                verbosity.Add(new(commit, note.Verbosity - before.Verbosity));
            }

            before = note;
        }

        return new()
        {
            LastPush = lastPush, Now = now, ErosionRaises = Largest(erosion), VerbosityRaises = Largest(verbosity)
        };
    }

    /// <summary>The summary as text for the person who pushes.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        if (LastPush is not { } last)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"slop now: erosion {Now.Erosion:F4}, verbosity {Now.Verbosity:F4}, sloc {Now.Sloc} (no note before the push to compare with).");
            return text.ToString();
        }

        text.AppendLine("slop since the last push:");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"  erosion    {last.Erosion:F4} -> {Now.Erosion:F4}  {Now.Erosion - last.Erosion:+0.0000;-0.0000;+0.0000}");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"  verbosity  {last.Verbosity:F4} -> {Now.Verbosity:F4}  {Now.Verbosity - last.Verbosity:+0.0000;-0.0000;+0.0000}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  sloc       {last.Sloc} -> {Now.Sloc}  {Now.Sloc - last.Sloc:+0;-0;+0}");
        AppendRaises(text, "erosion", ErosionRaises);
        AppendRaises(text, "verbosity", VerbosityRaises);
        return text.ToString();
    }

    private static CommitRaise[] Largest(IEnumerable<CommitRaise> raises) =>
        [.. raises.Where(raise => raise.Change > 0).OrderByDescending(raise => raise.Change).Take(RaiseCount)];

    private static void AppendRaises(StringBuilder text, string metric, IReadOnlyList<CommitRaise> raises)
    {
        if (raises.Count == 0)
            return;
        text.AppendLine(CultureInfo.InvariantCulture, $"the commits that raised the {metric} the most:");
        foreach ((Commit commit, double change) in raises)
        {
            string tickets = commit.Tickets.Count > 0 ? string.Join(' ', commit.Tickets) : "(no ticket)";
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {change:+0.0000;-0.0000}  {commit.ShortSha}  {tickets}  {commit.Subject}");
        }
    }
}
