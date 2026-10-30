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

    /// <summary>
    /// Half the last shown digit of high CC mass.
    /// </summary>
    public const double LeastMassRaise = 0.05;

    /// <summary>The note at the last push, or null when no commit before the push has one.</summary>
    public required SlopNote? LastPush { get; init; }

    /// <summary>The note of the pushed tip.</summary>
    public required SlopNote Now { get; init; }

    /// <summary>The pushed commits that raised high CC mass, the largest raise first.</summary>
    public required IReadOnlyList<CommitRaise> HighCcMassRaises { get; init; }

    /// <summary>The pushed commits that raised verbose lines, the largest raise first.</summary>
    public required IReadOnlyList<CommitRaise> VerboseLineRaises { get; init; }

    /// <summary>
    /// The summary of a push. <paramref name="pushed"/> holds the pushed commits on the first-parent line, oldest first.
    /// A noted commit raises a metric by the change from the nearest note before it: the note of an earlier pushed
    /// commit, or <paramref name="lastPush"/>. A commit without a note changed no code, or a hook did not measure it.
    /// </summary>
    public static PushSummary Of(SlopNote? lastPush, SlopNote now, IReadOnlyList<Commit> pushed)
    {
        var mass = new List<CommitRaise>();
        var lines = new List<CommitRaise>();
        SlopNote? before = lastPush;
        foreach (Commit commit in pushed)
        {
            if (commit.Note is not { } note)
                continue;
            if (before?.HighCcMass is { } previousMass && note.HighCcMass is { } currentMass)
                mass.Add(new(commit, currentMass - previousMass));
            if (before?.VerboseLines is { } previousLines && note.VerboseLines is { } currentLines)
                lines.Add(new(commit, currentLines - previousLines));

            before = note;
        }

        return new()
        {
            LastPush = lastPush,
            Now = now,
            HighCcMassRaises = Largest(mass, LeastMassRaise),
            VerboseLineRaises = Largest(lines, 1),
        };
    }

    /// <summary>The summary as text for the person who pushes.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        if (LastPush is not { } last)
        {
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"slop now: erosion {Now.Erosion:F4}, verbosity {Now.Verbosity:F4}, sloc {Now.Sloc} (no note before the push to compare with)."
            );
            AppendCounts(text, null, Now);
            return text.ToString();
        }

        text.AppendLine("slop since the last push:");
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"  erosion    {last.Erosion:F4} -> {Now.Erosion:F4}  {Now.Erosion - last.Erosion:+0.0000;-0.0000;+0.0000}"
        );
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"  verbosity  {last.Verbosity:F4} -> {Now.Verbosity:F4}  {Now.Verbosity - last.Verbosity:+0.0000;-0.0000;+0.0000}"
        );
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"  sloc       {last.Sloc} -> {Now.Sloc}  {Now.Sloc - last.Sloc:+0;-0;+0}"
        );
        AppendCounts(text, last, Now);
        AppendRaises(text: text, metric: "high CC mass", raises: HighCcMassRaises, changeFormat: "+0.0;-0.0;+0.0");
        AppendRaises(text: text, metric: "verbose lines", raises: VerboseLineRaises, changeFormat: "+0;-0;+0");
        return text.ToString();
    }

    private static CommitRaise[] Largest(IEnumerable<CommitRaise> raises, double leastRaise) =>
        [
            .. raises
                .Where(raise => raise.Change >= leastRaise)
                .OrderByDescending(raise => raise.Change)
                .Take(RaiseCount),
        ];

    private static void AppendCounts(StringBuilder text, SlopNote? before, SlopNote now)
    {
        AppendAbsolute(
            text: text,
            name: "high CC mass",
            before: before?.HighCcMass,
            now: now.HighCcMass,
            valueFormat: "0.0",
            changeFormat: "+0.0;-0.0;+0.0"
        );
        AppendAbsolute(
            text: text,
            name: "high CC functions",
            before: before?.HighCcFunctionCount,
            now: now.HighCcFunctionCount,
            valueFormat: "0",
            changeFormat: "+0;-0;+0"
        );
        AppendAbsolute(
            text: text,
            name: "verbose lines",
            before: before?.VerboseLines,
            now: now.VerboseLines,
            valueFormat: "0",
            changeFormat: "+0;-0;+0"
        );
    }

    private static void AppendAbsolute(
        StringBuilder text,
        string name,
        double? before,
        double? now,
        string valueFormat,
        string changeFormat
    )
    {
        if (now is not { } value)
            return;
        string shown = value.ToString(valueFormat, CultureInfo.InvariantCulture);
        if (before is not { } previous)
        {
            text.AppendLine($"  {name} {shown}");
            return;
        }

        string old = previous.ToString(valueFormat, CultureInfo.InvariantCulture);
        string change = (value - previous).ToString(changeFormat, CultureInfo.InvariantCulture);
        text.AppendLine($"  {name} {old} -> {shown}  {change}");
    }

    private static void AppendRaises(
        StringBuilder text,
        string metric,
        IReadOnlyList<CommitRaise> raises,
        string changeFormat
    )
    {
        if (raises.Count == 0)
            return;
        text.AppendLine(CultureInfo.InvariantCulture, $"the commits that raised the {metric} the most:");
        foreach (CommitRaise raise in raises)
        {
            Commit commit = raise.Commit;
            string tickets = commit.Tickets.Count > 0 ? string.Join(' ', commit.Tickets) : "(no ticket)";
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"  {raise.Change.ToString(changeFormat, CultureInfo.InvariantCulture)}  {commit.ShortSha}  {tickets}  {commit.Subject}"
            );
        }
    }
}
