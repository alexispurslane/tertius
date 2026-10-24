using AwesomeAssertions;
using Tertius.Slop.History;

namespace Tertius.Slop.Tests;

/// <summary>
/// The commands of the slop history (tools/README.md, "Slop metrics"), on a real git repository: <c>measure</c> and
/// <c>note</c> write the metrics of commits, <c>history</c> and <c>summary</c> read them. They run jscpd, as the dev
/// shell has it.
/// </summary>
public sealed class HistoryCommandTests : IDisposable
{
    // 4 lines of code, then 8.
    private const string Short = "class A\n{\n    int F() => 1;\n}\n";

    private const string Long =
        "class A\n{\n    int F() => 1;\n    int G() => 2;\n    int H() => 3;\n    int I() => 4;\n    int J() => 5;\n}\n";

    // One function with a cyclomatic complexity of 12, so an erosion of 1 when it is the only function.
    private static readonly string Complex =
        $"class C\n{{\n    int F(int x)\n    {{\n{string.Concat(Enumerable.Range(0, 11).Select(i => $"        if (x == {i}) return {i};\n"))}        return 0;\n    }}\n}}\n";

    private readonly GitFolder git = new();
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public void Dispose()
    {
        git.Dispose();
        output.Dispose();
        error.Dispose();
    }

    [Fact]
    public void TheMeasureCommandMeasuresTheTreeOfACommitNotTheWorkingTree()
    {
        git.Write("src/A.cs", Short);
        git.Commit("first");
        git.Write("src/A.cs", Long);

        int exit = Run("measure", "HEAD", "--branch", "main", "--source", "pre-commit");

        exit.Should().Be(0);
        SlopNote note = SlopNote.Parse(output.ToString())!;
        (note.Sloc, note.Branch, note.Source).Should().Be((4, "main", NoteSource.PreCommit));
    }

    [Fact]
    public void TheMeasureCommandMeasuresATreeThatNoCommitHoldsYet()
    {
        git.Write("src/A.cs", Short);
        git.Commit("first");
        git.Write("src/A.cs", Long);
        git.Git("add", "-A");
        string staged = git.Git("write-tree").Trim();

        Run("measure", staged, "--branch", "main", "--source", "pre-commit");

        SlopNote.Parse(output.ToString())!.Sloc.Should().Be(8);
    }

    [Fact]
    public void TheNoteCommandNotesTheCommitsThatChangeTheSourceAndThePushedTip()
    {
        git.Write("src/A.cs", Short);
        string first = git.Commit("first");
        git.Write("README.md", "docs");
        string docs = git.Commit("docs only");
        git.Write("src/A.cs", Long);
        string source = git.Commit("ter-ab12: more code");
        git.Write("README.md", "more docs");
        string tip = git.Commit("docs again");

        int exit = Run("note", first, tip, "--branch", "work", "--source", "pre-push");

        exit.Should().Be(0);
        (git.NoteOf(first), git.NoteOf(docs)).Should().Be((null, null));
        SlopNote.Parse(git.NoteOf(source)!)!.Should().Match<SlopNote>(note =>
            note.Sloc == 8 && note.Branch == "work" && note.Source == NoteSource.PrePush);
        SlopNote.Parse(git.NoteOf(tip)!)!.Sloc.Should().Be(8);
    }

    [Fact]
    public void TheNoteCommandKeepsANoteThatACommitAlreadyHas()
    {
        git.Write("src/A.cs", Short);
        string first = git.Commit("first");
        git.Write("src/A.cs", Long);
        string tip = git.Commit("second");
        string kept = SlopNoteTests.Note(erosion: 0.5, verbosity: 0.5, sloc: 999).ToLine();
        git.Git("notes", "--ref=slop", "add", "-m", kept, tip);

        int exit = Run("note", first, tip, "--branch", "main", "--source", "pre-push");

        (exit, git.NoteOf(tip)).Should().Be((0, kept));
    }

    [Fact]
    public void TheNoteCommandNotesAMergeThatBringsSourceChangesToTheFirstParentLine()
    {
        git.Write("src/A.cs", Short);
        string first = git.Commit("first");
        git.Git("switch", "-q", "-c", "side");
        git.Write("src/B.cs", Short);
        git.Commit("on the side");
        git.Git("switch", "-q", "main");
        git.Write("README.md", "docs");
        git.Commit("docs on main");
        git.Git("merge", "-q", "--no-ff", "-m", "merge side", "side");
        string merge = git.Git("rev-parse", "HEAD").Trim();
        git.Write("README.md", "more docs");
        string tip = git.Commit("docs again");

        Run("note", first, tip, "--branch", "main", "--source", "pre-push");

        SlopNote.Parse(git.NoteOf(merge)!)!.Sloc.Should().Be(8);
    }

    [Fact]
    public void TheNoteCommandNotesAFirstCommitThatHoldsSource()
    {
        git.Write("src/A.cs", Short);
        string root = git.Commit("root");
        git.Write("README.md", "docs");
        string tip = git.Commit("docs");
        git.Git("switch", "-q", "--orphan", "other");
        git.Write("src/C.cs", Short);
        string other = git.Commit("an unrelated root");

        Run("note", other, tip, "--branch", "main", "--source", "pre-push");

        git.NoteOf(root).Should().NotBeNull();
    }

    [Fact]
    public void TheHistoryCommandPrintsTheNotedCommitsAsCsvNewestFirst()
    {
        git.Write("src/A.cs", Short);
        NoteAt(git.Commit("ter-aaaa: first"));
        git.Write("README.md", "docs");
        git.Commit("docs only");
        git.Write("src/A.cs", Long);
        NoteAt(git.Commit("ter-bbbb: third"));

        int exit = Run("history");

        exit.Should().Be(0);
        string[] lines = output.ToString().TrimEnd().Split('\n');
        lines[0].Should().Be("commit,date,branch,tickets,erosion,verbosity,sloc");
        lines[1..].Select(line => line.Split(',')).Select(cells => (cells[3], cells[6]))
            .Should().Equal(("ter-bbbb", "8"), ("ter-aaaa", "4"));
    }

    [Fact]
    public void TheHistoryCommandFollowsOnlyTheFirstParentWhenAsked()
    {
        git.Write("src/A.cs", Short);
        string first = git.Commit("first");
        git.Git("switch", "-q", "-c", "side");
        git.Write("src/B.cs", Short);
        string side = git.Commit("on the side");
        git.Git("switch", "-q", "main");
        git.Git("merge", "-q", "--no-ff", "-m", "merge side", "side");
        NoteAt(first);
        NoteAt(side);
        NoteAt("HEAD");

        Run("history", "--first-parent", "main");

        output.ToString().Should().Contain(first).And.NotContain(side);
    }

    [Fact]
    public void TheSummaryCommandComparesTheNoteAtTheLastPushWithTheNoteOfTheTip()
    {
        git.Write("src/A.cs", Short);
        string pushed = git.Commit("first");
        NoteAt(pushed);
        git.Write("README.md", "docs");
        git.Commit("docs only");
        string ifs = string.Concat(Enumerable.Range(0, 11).Select(i => $"        if (x == {i}) return {i};\n"));
        git.Write("src/A.cs", $"class A\n{{\n    int F(int x)\n    {{\n{ifs}        return 0;\n    }}\n}}\n");
        string complex = git.Commit("ter-ab12: a complex function");
        NoteAt(complex);

        int exit = Run("summary", pushed, "HEAD");

        exit.Should().Be(0);
        output.ToString().Should().Contain("erosion    0.0000 -> 1.0000  +1.0000").And
            .Contain($"+1.0000  {complex[..7]}  ter-ab12  ter-ab12: a complex function");
    }

    [Fact]
    public void TheSummaryCommandNamesOnlyTheCommitsThatRaisedAMetricAgainstTheNoteBeforeThem()
    {
        git.Write("src/A.cs", Short);
        string pushed = git.Commit("first");
        NoteAt(pushed);
        git.Write("src/A.cs", Complex);
        string complex = git.Commit("ter-ab12: a complex function");
        NoteAt(complex);
        git.Write("src/B.cs", Long);
        string simple = git.Commit("ter-cd34: simple functions beside it");
        NoteAt(simple);

        Run("summary", pushed, "HEAD");

        output.ToString().Should().Contain($"+1.0000  {complex[..7]}").And.NotContain(simple[..7]);
    }

    [Fact]
    public void TheSummaryCommandReadsTheNearestNoteBeforeAPushedTipWithoutOne()
    {
        git.Write("src/A.cs", Short);
        string pushed = git.Commit("first");
        NoteAt(pushed);
        git.Write("src/A.cs", Complex);
        NoteAt(git.Commit("ter-ab12: a complex function"));
        git.Write("README.md", "docs");
        git.Commit("docs only");

        Run("summary", pushed, "HEAD");

        output.ToString().Should().Contain("erosion    0.0000 -> 1.0000  +1.0000");
    }

    [Fact]
    public void AHistoryCommandThatGitFailsForPrintsTheFailureAndExitsWithOne()
    {
        git.Write("src/A.cs", Short);
        git.Commit("first");

        int exit = Run("history", "no-such-commit");

        exit.Should().Be(1);
        error.ToString().Should().StartWith("Tertius.Slop: git log failed");
    }

    [Theory]
    [InlineData("measure", "HEAD")]
    [InlineData("note", "HEAD", "HEAD", "--branch", "main")]
    [InlineData("summary", "HEAD")]
    public void AHistoryCommandWithWrongArgumentsPrintsTheUsageAndFails(params string[] args)
    {
        int exit = Run(args);

        (exit, output.ToString()).Should().Be((2, ""));
        error.ToString().Should().Contain("usage");
    }

    // Measures the tree of a commit and attaches the note, as the hooks do.
    private void NoteAt(string commit)
    {
        Run("measure", commit, "--branch", "main", "--source", "seed");
        git.Git("notes", "--ref=slop", "add", "-f", "-m", output.ToString().Trim(), commit);
        output.GetStringBuilder().Clear();
    }

    private int Run(params string[] args) =>
        Program.Run(args: ["-C", git.Root, .. args], output: output, error: error, jscpd: "jscpd");
}
