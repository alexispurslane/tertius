using System.Reflection;
using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.TestHost;
using Microsoft.Testing.Platform.Services;

namespace Tertius.TestRecord;

/// <summary>
/// Adds the <see cref="TestRecorder"/> to a test project. The test platform calls this by name, because
/// Directory.Build.props names it in a TestingPlatformBuilderHook item.
/// </summary>
public static class TestingPlatformBuilderHook
{
    /// <summary>Adds the recorder for one test run, with the command line arguments of that run.</summary>
    public static void AddExtensions(ITestApplicationBuilder builder, string[] args)
    {
        CompositeExtensionFactory<TestRecorder> recorder = new(() => new(fullRun: TestRecorder.IsFullRun(args)));
        builder.TestHost.AddDataConsumer(recorder);
        builder.TestHost.AddTestSessionLifetimeHandler(recorder);
    }
}

/// <summary>
/// Records that every test of a test project passed on the code that the project was built from, so that the pre-commit
/// hook can skip those tests (tools/README.md, "Git hooks").
/// </summary>
/// <remarks>
/// The build writes the code hash of tools/code-tree.sh into <c>code-tree.txt</c> next to the test assembly. After a full
/// run with at least one passed test and no failed one, the recorder copies that hash to
/// <c>tertius-tested/&lt;configuration&gt;/&lt;assembly&gt;</c> in the git folder of the worktree.
/// </remarks>
public sealed class TestRecorder(bool fullRun) : IDataConsumer, ITestSessionLifetimeHandler
{
    private const string StampFile = "code-tree.txt";

    // Options that run only part of the tests or none of them. A response file (@file) can hold any of them.
    private static readonly string[] PartialRunPrefixes = ["--filter", "--treenode-filter", "--list-tests", "--explicit", "@"];

    private int passed;
    private int failed;

    /// <inheritdoc/>
    public string Uid => nameof(TestRecorder);

    /// <inheritdoc/>
    public string Version => "1.0.0";

    /// <inheritdoc/>
    public string DisplayName => "Tertius test record";

    /// <inheritdoc/>
    public string Description => "Records a passed full run of the tests, for the pre-commit hook.";

    /// <inheritdoc/>
    public Type[] DataTypesConsumed => [typeof(TestNodeUpdateMessage)];

    /// <summary>True when no argument selects part of the tests, so the run covers every test of the project.</summary>
    public static bool IsFullRun(IEnumerable<string> args) =>
        !args.Any(arg => PartialRunPrefixes.Any(prefix => arg.StartsWith(prefix, StringComparison.Ordinal)));

    /// <inheritdoc/>
    public Task<bool> IsEnabledAsync() => Task.FromResult(true);

    /// <inheritdoc/>
    public Task ConsumeAsync(IDataProducer dataProducer, IData value, CancellationToken cancellationToken)
    {
        TestNodeStateProperty? state = (value as TestNodeUpdateMessage)?.TestNode.Properties.SingleOrDefault<TestNodeStateProperty>();
        if (state is PassedTestNodeStateProperty)
            Interlocked.Increment(ref passed);
        else if (state is FailedTestNodeStateProperty or ErrorTestNodeStateProperty or TimeoutTestNodeStateProperty)
            Interlocked.Increment(ref failed);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task OnTestSessionStartingAsync(ITestSessionContext testSessionContext) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task OnTestSessionFinishingAsync(ITestSessionContext testSessionContext)
    {
        if (fullRun && RecordPath() is { } record)
            if (failed > 0)
                File.Delete(record);
            else if (passed > 0)
                Record(record);
        return Task.CompletedTask;
    }

    // The build's stamp: the code hash, then the git folder. A build outside a git checkout writes no stamp.
    private static string[]? Stamp()
    {
        string stamp = Path.Combine(AppContext.BaseDirectory, StampFile);
        return File.Exists(stamp) ? File.ReadAllLines(stamp) : null;
    }

    // The record file of this test assembly and configuration, or null without a stamp, so that nothing is recorded and
    // the hook runs the tests. A failed full run deletes the record, so that a flaky failure never hides behind an old pass.
    private static string? RecordPath()
    {
        if (Stamp() is not [_, string gitDir, ..] || Assembly.GetEntryAssembly() is not { } entry)
            return null;
        string configuration = entry.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "unknown";
        return Path.Combine(
            path1: gitDir,
            path2: "tertius-tested",
            path3: configuration,
            path4: entry.GetName().Name ?? "unknown"
        );
    }

    // Writes the stamp's code hash to the record file, through a temporary file, so that the hook never reads half of it.
    private static void Record(string record)
    {
        if (Stamp() is not [string tree, ..])
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(record) ?? ".");
        string temporary = $"{record}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporary, tree + "\n");
        File.Move(temporary, record, overwrite: true);
    }
}
