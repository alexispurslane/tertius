using System.Diagnostics;
using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>The staged commit report leads with touched findings and ends with project ratios.</summary>
public sealed class CommitReportTests
{
    [Fact]
    public async Task TheCommitReportShowsTouchedFindingsBeforeTheProjectAddendum()
    {
        string root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "tools", "commit_report.py")))
            root =
                Directory.GetParent(root)?.FullName
                ?? throw new InvalidOperationException("Repository root not found.");

        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("tests/Tertius.Slop.Tests/commit_report_cases.py");
        using Process python = Process.Start(start)!;
        Task<string> errors = python.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        string output = await python.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await python.WaitForExitAsync(TestContext.Current.CancellationToken);

        python.ExitCode.Should().Be(0, await errors + output);
    }
}
