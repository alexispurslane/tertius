using System.Text.Json;

namespace Tertius.Slop.History;

/// <summary>
/// The commands of the slop history (tools/README.md, "Slop metrics"; docs/plans/code-quality-tooling.md, decision
/// 18). <c>measure</c> and <c>note</c> write the metrics of commits for the git hooks, and <c>history</c> and
/// <c>summary</c> read them.
/// </summary>
public sealed class HistoryCommands(Repository repository, TextWriter output, string jscpd)
{
    // How far back on the first-parent line a summary looks for the note before a commit. A push that finds none in
    // that many commits shows the metrics of the tip only.
    private const int NoteSearchDepth = 500;

    /// <summary>
    /// Prints the note of the tree <paramref name="tree"/>, measured now, as the pre-commit hook does. With a
    /// <paramref name="details"/> path, it also writes the <see cref="MeasureDetails"/> of the tree there as JSON, for
    /// the commit report. Their baseline is the nearest noted commit on the first-parent line of
    /// <paramref name="parent"/>, by the rule of <see cref="Summary"/>, or null without a parent.
    /// </summary>
    public void Measure(string tree, string branch, NoteSource source, string? details, string? parent)
    {
        SlopReport report = repository.Measure(tree, jscpd);
        if (details is not null)
        {
            NotedCommit? baseline =
                parent is not null && NearestNoted(parent) is { Note: { } note } noted
                    ? new(noted.ShortSha, note)
                    : null;
            File.WriteAllText(
                details,
                JsonSerializer.Serialize(new MeasureDetails(report, baseline), SlopJson.Default.MeasureDetails)
            );
        }

        output.WriteLine(
            SlopNote.Of(report: report, branch: branch, source: source, measured: DateTimeOffset.UtcNow).ToLine()
        );
    }

    /// <summary>
    /// Notes each commit on the first-parent line after <paramref name="from"/> up to <paramref name="tip"/> that has
    /// no note and changes a C# file under <c>src</c>, and the tip. It prints one line per note: the short hash and
    /// the note. A merge, a commit made with <c>--no-verify</c> and the cleanup commit of the pre-push hook get their
    /// note here.
    /// </summary>
    public void Note(string from, string tip, string branch, NoteSource source)
    {
        IReadOnlyList<Commit> commits = repository.Log(["--first-parent", $"{from}..{tip}"]);
        foreach (Commit commit in commits.Where(commit => commit.Note is null))
        {
            if (commit.Sha != commits[0].Sha && !repository.ChangesSource(commit))
                continue;
            var note = SlopNote.Of(
                report: repository.Measure(commit.Sha, jscpd),
                branch: branch,
                source: source,
                measured: DateTimeOffset.UtcNow
            );
            repository.AddNote(commit.Sha, note);
            output.WriteLine($"{commit.ShortSha} {note.ToLine()}");
        }
    }

    /// <summary>Prints the noted commits that <c>git log</c> lists for <paramref name="arguments"/>, as CSV.</summary>
    public void History(IEnumerable<string> arguments) => output.Write(new SlopHistory(repository.Log(arguments)).Csv);

    /// <summary>
    /// Prints the summary of a push from <paramref name="from"/>, the last pushed commit, to <paramref name="tip"/>.
    /// The note at the last push is the note of <paramref name="from"/> or of its nearest noted ancestor on the
    /// first-parent line, and the note of now is the same for <paramref name="tip"/>.
    /// </summary>
    public void Summary(string from, string tip)
    {
        if (NearestNoted(tip)?.Note is not { } now)
        {
            output.WriteLine("slop: no commit up to the pushed tip has a note.");
            return;
        }

        IReadOnlyList<Commit> pushed = repository.Log(["--first-parent", "--reverse", $"{from}..{tip}"]);
        output.Write(PushSummary.Of(lastPush: NearestNoted(from)?.Note, now: now, pushed: pushed).Describe());
    }

    // The commit or its nearest ancestor on the first-parent line that has a note, or null when none of the last
    // NoteSearchDepth has one.
    private Commit? NearestNoted(string commit) =>
        repository
            .Log(["--first-parent", $"--max-count={NoteSearchDepth}", commit])
            .FirstOrDefault(each => each.Note is not null);
}
