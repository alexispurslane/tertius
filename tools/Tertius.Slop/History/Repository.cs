using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;

namespace Tertius.Slop.History;

/// <summary>
/// The git repository whose slop history the commands read and write (docs/plans/code-quality-tooling.md, decision
/// 18). Each call runs git in <paramref name="folder"/>. Git runs on the repository of that folder, whatever the
/// <c>GIT_</c> variables of a hook that runs the tool say, so a hook's temporary index never leaks into a read.
/// </summary>
public sealed class Repository(string folder)
{
    /// <summary>The ref that holds the notes.</summary>
    public const string NotesRef = "refs/notes/slop";

    // The variables by which a hook points git at its repository, its index or its objects.
    private static readonly string[] HookVariables =
    [
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_INDEX_FILE",
        "GIT_COMMON_DIR",
        "GIT_OBJECT_DIRECTORY",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_PREFIX",
    ];

    /// <summary>The commits that <c>git log</c> lists for <paramref name="arguments"/>, newest first, with their notes.</summary>
    public IReadOnlyList<Commit> Log(IEnumerable<string> arguments)
    {
        // A unit separator between the fields and a record separator after each commit, which no message holds.
        string log = Git(["log", $"--notes={NotesRef}", "--format=%H%x1f%P%x1f%cI%x1f%B%x1f%N%x1e", .. arguments]);
        return
        [
            .. log.Split('\x1e')
                .Select(record => record.TrimStart('\n'))
                .Where(record => record.Length > 0)
                .Select(record => record.Split('\x1f'))
                .Select(fields => new Commit
                {
                    Sha = fields[0],
                    FirstParent = fields[1].Split(' ')[0] is { Length: > 0 } parent ? parent : null,
                    Date = DateTimeOffset.Parse(fields[2], CultureInfo.InvariantCulture),
                    Message = fields[3].TrimEnd(),
                    Note = SlopNote.Parse(fields[4]),
                }),
        ];
    }

    /// <summary>
    /// The slop report of the <c>src</c> folder of a tree or a commit, from a copy of it in a temporary folder, so that
    /// the working tree does not matter. The copy holds the tests, the benchmarks and the scripts too, because they use
    /// the code (<see cref="Codebase.NamesOutside"/>).
    /// </summary>
    public SlopReport Measure(string tree, string jscpd)
    {
        // An archive of a path that the tree does not hold fails, and an old commit can lack the benchmarks.
        string[] folders = [.. Git(["ls-tree", "--name-only", tree]).Split('\n').Where(Codebase.Folders.Contains)];
        string root = Directory.CreateTempSubdirectory("tertius-slop-tree-").FullName;
        try
        {
            using (Process archive = Start(["archive", "--format=tar", tree, "--", .. folders]))
            {
                Task<string> errors = archive.StandardError.ReadToEndAsync();
                TarFile.ExtractToDirectory(archive.StandardOutput.BaseStream, root, overwriteFiles: false);
                archive.WaitForExit();
                if (archive.ExitCode != 0)
                    throw new InvalidOperationException($"git archive of {tree} failed: {errors.Result}");
            }

            return SlopReport.Of(Codebase.Under(root), Clones.Find(root, jscpd));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Whether a commit changes a C# file under <c>src</c> against its first parent. A first commit does.</summary>
    public bool ChangesSource(Commit commit)
    {
        if (commit.FirstParent is not { } parent)
            return true;
        int exit = Exit(["diff", "--quiet", parent, commit.Sha, "--", "src/*.cs"]);
        return exit switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException($"git diff of {commit.Sha} failed with exit code {exit}."),
        };
    }

    /// <summary>Attaches <paramref name="note"/> to the commit <paramref name="sha"/>, which has no note yet.</summary>
    public void AddNote(string sha, SlopNote note) =>
        Git(["notes", $"--ref={NotesRef}", "add", "-m", note.ToLine(), sha]);

    /// <summary>The object hashes with a slop note, including commits off the current branch.</summary>
    public IReadOnlyList<string> NoteTargets() =>
        [
            .. Git(["notes", $"--ref={NotesRef}", "list"])
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(' ')[^1]),
        ];

    /// <summary>The complete text of one slop note.</summary>
    public string NoteText(string sha) => Git(["notes", $"--ref={NotesRef}", "show", sha]).TrimEnd('\n');

    /// <summary>Replaces one slop note with its backfilled text.</summary>
    public void ReplaceNote(string sha, string text) =>
        Git(["notes", $"--ref={NotesRef}", "add", "-f", "-m", text, sha]);

    private string Git(IReadOnlyList<string> arguments)
    {
        using Process git = Start(arguments);
        Task<string> errors = git.StandardError.ReadToEndAsync();
        string output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        return git.ExitCode == 0
            ? output
            : throw new InvalidOperationException($"git {arguments[0]} failed: {errors.Result.Trim()}");
    }

    private int Exit(IReadOnlyList<string> arguments)
    {
        using Process git = Start(arguments);
        Task<string> errors = git.StandardError.ReadToEndAsync();
        git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        _ = errors.Result;
        return git.ExitCode;
    }

    private Process Start(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (string variable in HookVariables)
            start.Environment.Remove(variable);
        return Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
    }
}
