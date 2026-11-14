using System.Diagnostics;

namespace Tertius.Host.Tests;

/// <summary>
/// The shared GPU lock that a GPU test project holds for its whole run (<see cref="GpuTestLock"/>, ticket ter-zoof). The
/// tests run this tree's tools/gpu-lock.sh on a lock and a queue in a fresh folder, never on the real lock.
/// </summary>
public sealed class GpuTestLockTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("tertius-gpu-test-lock-tests-");
    private readonly List<Process> processes = [];

    private string Lock => Path.Combine(folder.FullName, "lock");

    private Dictionary<string, string> Environment =>
        new() { ["TERTIUS_GPU_LOCK"] = Lock, ["TERTIUS_GPU_QUEUE"] = Path.Combine(folder.FullName, "queue") };

    private static string Helper
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(directory!.FullName, "Tertius.slnx")))
                directory = directory.Parent;
            return Path.Combine(directory.FullName, "tools", "gpu-lock.sh");
        }
    }

    public void Dispose()
    {
        foreach (Process process in processes)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
        }
        folder.Delete(recursive: true);
    }

    private GpuTestLock Take(string? held = null, int? owner = null) =>
        new(helperPath: Helper, held: held, owner: owner ?? System.Environment.ProcessId, environment: Environment);

    private string[] SharedRecords =>
        Directory.Exists(Path.Combine(Lock, "shared")) ? Directory.GetFiles(Path.Combine(Lock, "shared")) : [];

    private static bool Within(TimeSpan time, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < time)
        {
            if (condition())
                return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    private Process Started(string file, params string[] arguments)
    {
        var start = new ProcessStartInfo(file);
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        foreach ((string name, string value) in Environment)
            start.Environment[name] = value;
        Process process = Process.Start(start)!;
        processes.Add(process);
        return process;
    }

    [Fact]
    public void TheRunHoldsTheSharedLockUntilItsDisposal()
    {
        GpuTestLock held = Take();

        SharedRecords.Should().ContainSingle("the run holds the lock in its shared mode");
        File.ReadAllText(SharedRecords[0]).Should().Contain("pid=");

        held.Dispose();

        Within(TimeSpan.FromSeconds(5), () => !Directory.Exists(Lock)).Should().BeTrue("the release removes the lock");
    }

    [Theory]
    [InlineData("exclusive")]
    [InlineData("shared")]
    public void ARunInsideARunThatHoldsTheLockTakesNothing(string mode)
    {
        using GpuTestLock held = Take(held: mode);

        Directory.Exists(Lock).Should().BeFalse("waiting for its parent's lock would never end");
    }

    [Fact]
    public async Task ARunWaitsWhileATimedRunHoldsTheGpuAlone()
    {
        string release = Path.Combine(folder.FullName, "release");
        Started(
            "bash",
            Helper,
            Path.Combine(folder.FullName, "timed.log"),
            "bash",
            "-c",
            "while [ ! -e \"$1\" ]; do sleep 0.05; done",
            "timed",
            release
        );
        Within(TimeSpan.FromSeconds(10), () => File.Exists(Path.Combine(Lock, "holder")))
            .Should()
            .BeTrue("the timed run takes the lock");

        CancellationToken cancel = TestContext.Current.CancellationToken;
        Task<GpuTestLock> taking = Task.Run(() => Take(), cancel);
        await Task.Delay(1000, cancel);
        taking.IsCompleted.Should().BeFalse("a shared run never holds the GPU beside an exclusive one");
        File.WriteAllText(release, "");

        using GpuTestLock held = await taking.WaitAsync(TimeSpan.FromSeconds(10), cancel);
        SharedRecords.Should().ContainSingle();
    }

    [Fact]
    public void TheLockIsFreedWhenTheTestProcessIsGone()
    {
        Process owner = Started("sleep", "60");
        GpuTestLock held = Take(owner: owner.Id);
        SharedRecords.Should().ContainSingle();

        owner.Kill();

        Within(TimeSpan.FromSeconds(5), () => !Directory.Exists(Lock))
            .Should()
            .BeTrue("the helper's command ends when the test process is gone, and the helper releases the lock");
        held.Dispose();
    }
}
