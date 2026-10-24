using System.Diagnostics;

namespace Tertius.Slop.Tests;

/// <summary>
/// A git repository in a new temporary folder, for the tests of the slop history. Its git commands ignore the
/// <c>GIT_</c> variables of a hook that runs the tests, such as <c>GIT_INDEX_FILE</c>, so that they never touch the
/// repository that is being committed.
/// </summary>
internal sealed class GitFolder : IDisposable
{
    public GitFolder()
    {
        Git("init", "-q", "-b", "main");
        Git("config", "user.name", "Test");
        Git("config", "user.email", "test@example.com");
        Git("config", "commit.gpgsign", "false");
    }

    /// <summary>The folder of the repository.</summary>
    public string Root { get; } = Directory.CreateTempSubdirectory("tertius-slop-git-").FullName;

    public void Dispose() => Directory.Delete(Root, recursive: true);

    /// <summary>Writes <paramref name="text"/> to the file at <paramref name="path"/>, relative to the root.</summary>
    public void Write(string path, string text)
    {
        string full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>Commits every change with <paramref name="message"/> and gives the commit's hash.</summary>
    public string Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "--allow-empty", "-m", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    /// <summary>The slop note of <paramref name="commit"/>, or null.</summary>
    public string? NoteOf(string commit)
    {
        (int exit, string output) = Run("notes", "--ref=slop", "show", commit);
        return exit == 0 ? output.Trim() : null;
    }

    /// <summary>Runs git with <paramref name="arguments"/> in the root and gives its output. It fails on an error.</summary>
    public string Git(params string[] arguments)
    {
        (int exit, string output) = Run(arguments);
        return exit == 0 ? output : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed.");
    }

    private (int Exit, string Output) Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (
            string name in start
                .Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.Ordinal))
                .ToList()
        )
            start.Environment.Remove(name);
        using Process git = Process.Start(start)!;
        Task<string> errors = git.StandardError.ReadToEndAsync();
        string output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        _ = errors.Result;
        return (git.ExitCode, output);
    }
}
