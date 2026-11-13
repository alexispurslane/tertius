using System.Diagnostics;

namespace Tertius.Testing;

/// <summary>
/// The GPU lock in its shared mode for a whole run of a GPU test project (ticket ter-zoof). A timed run holds the lock
/// alone, so it never runs beside the GPU tests, wherever they start: the pre-commit hook, tools/test-all.sh or a plain
/// dotnet test in a worktree. Before, three of eight traces of a timed run were lost on 2026-11-12, when another agent's
/// suites loaded the Mac to 44 to 53 beside it. Each GPU test project names this class as an assembly fixture, so xunit
/// takes the lock before the first test and releases it after the last.
/// <para>
/// It runs the one helper of the lock, tools/gpu-lock.sh of the main checkout, whose record, queue and liveness rule it
/// keeps (tools/README.md, "The GPU lock"). The helper's command marks the take with a file and holds the lock until
/// <see cref="Dispose"/> or until the test process is gone, so that a killed run frees the lock within a fraction of a
/// second.
/// </para>
/// <para>
/// A test run inside a run that holds the lock already sees <see cref="HeldVariable"/> and takes nothing, because it
/// would wait for its own parent's lock forever: the FSR timing test under an exclusive take, or a mutant's tests under
/// a shared one.
/// </para>
/// </summary>
public sealed class GpuTestLock : IDisposable
{
    /// <summary>The variable that tools/gpu-lock.sh sets for its command: exclusive or shared.</summary>
    public const string HeldVariable = "TERTIUS_GPU_LOCK_HELD";

    /// <summary>The folder of the helper's logs, which keep the seconds that each run waited.</summary>
    public const string LogFolder = "/tmp/tertius-gpu-test-lock";

    // The helper's command: it marks the take, then holds the lock while the run is not done and its process lives.
    private const string Hold =
        "touch \"$1\"; while [ ! -e \"$2\" ] && kill -0 \"$3\" 2> /dev/null; do sleep 0.2; done";

    private readonly Process? helper;
    private readonly string? flags;

    /// <summary>Takes the real lock for this test process, unless a parent holds it already.</summary>
    public GpuTestLock()
        : this(
            helperPath: HelperPath(),
            held: Environment.GetEnvironmentVariable(HeldVariable),
            owner: Environment.ProcessId,
            environment: new Dictionary<string, string> { ["TERTIUS_GPU_SHARED_CAP"] = TestRunCap.ToString() }
        ) { }

    /// <summary>
    /// The most shared holders beside which a test run still joins. Before ter-zoof the test runs took no lock and so
    /// had no cap, and one Checked suite runs two GPU projects at once. With the stills' cap of 3, two agents' suites
    /// would already wait for each other. A still still joins only beside fewer than 3 holders, because each run checks
    /// its own cap.
    /// </summary>
    public const int TestRunCap = 16;

    /// <summary>
    /// Takes the lock through the helper at <paramref name="helperPath"/> for the process <paramref name="owner"/>, with
    /// <paramref name="environment"/> over this process's, unless <paramref name="held"/> names a mode. Returns once the
    /// lock is held.
    /// </summary>
    /// <exception cref="InvalidOperationException">The helper ended without taking the lock.</exception>
    internal GpuTestLock(string helperPath, string? held, int owner, IReadOnlyDictionary<string, string> environment)
    {
        if (!string.IsNullOrEmpty(held))
            return;
        flags = Directory.CreateTempSubdirectory("tertius-gpu-test-lock-").FullName;
        Directory.CreateDirectory(LogFolder);
        string log = Path.Combine(LogFolder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{owner}-{Path.GetFileName(flags)}.log");
        string taken = Path.Combine(flags, "taken");
        var start = new ProcessStartInfo("bash")
        {
            ArgumentList = { helperPath, "--shared", log, "bash", "-c", Hold, "hold", taken, Done, owner.ToString() },
            RedirectStandardError = true,
        };
        foreach ((string name, string value) in environment)
            start.Environment[name] = value;
        helper = Process.Start(start) ?? throw new InvalidOperationException("bash did not start.");
        var waiting = Stopwatch.StartNew();
        bool told = false;
        while (!File.Exists(taken))
        {
            if (helper.HasExited)
                throw new InvalidOperationException(
                    $"tools/gpu-lock.sh ended with {helper.ExitCode} before it took the GPU lock: "
                        + helper.StandardError.ReadToEnd()
                );
            if (!told && waiting.Elapsed > TimeSpan.FromSeconds(2))
            {
                Console.Error.WriteLine($"Waiting for the GPU lock in its shared mode (log {log}).");
                told = true;
            }
            Thread.Sleep(50);
        }
    }

    private string Done => Path.Combine(flags!, "done");

    /// <summary>Releases the lock: the helper's command sees the marker, ends, and the helper removes its record.</summary>
    public void Dispose()
    {
        if (helper is null)
            return;
        File.WriteAllText(Done, "");
        helper.WaitForExit(TimeSpan.FromSeconds(10));
        helper.Dispose();
        Directory.Delete(flags!, recursive: true);
    }

    /// <summary>
    /// tools/gpu-lock.sh of the main checkout, which tools/README.md asks for, so that an older copy in a worktree does
    /// not take the lock. A worktree's .git file names the main checkout's .git folder. Without one, as on a cloud worker,
    /// the helper of this tree.
    /// </summary>
    private static string HelperPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Tertius.slnx")))
            directory = directory.Parent;
        string root = directory?.FullName ?? throw new InvalidOperationException("Cannot find the repository root.");
        string git = Path.Combine(root, ".git");
        if (File.Exists(git) && File.ReadAllText(git).Trim() is var line && line.StartsWith("gitdir: ", StringComparison.Ordinal))
        {
            // gitdir: <main checkout>/.git/worktrees/<name>
            string? main = Directory.GetParent(line["gitdir: ".Length..])?.Parent?.Parent?.FullName;
            if (main is not null && File.Exists(Path.Combine(main, "tools", "gpu-lock.sh")))
                return Path.Combine(main, "tools", "gpu-lock.sh");
        }
        return Path.Combine(root, "tools", "gpu-lock.sh");
    }
}
