namespace Tertius.Rendering.Tests;

/// <summary>
/// The marker lines of a measurement's timed span (tools/README.md, "The GPU clock of a timed run"). A measurement that
/// runs beside gpu-clock through tools/gpu-clock.sh runs its timed work through <see cref="Run{T}"/>, and
/// tools/gpu_clock.py then takes gpu-clock's clock over the samples between the two lines. The headless demo writes the
/// same lines from Tertius.Host's ScriptCommand.
/// </summary>
internal static class TimedSpan
{
    /// <summary>The line before the timed work.</summary>
    public const string Start = "timed-start";

    /// <summary>The line after the timed work.</summary>
    public const string End = "timed-end";

    /// <summary>
    /// Writes <see cref="Start"/> to the test's output, runs <paramref name="work"/>, writes <see cref="End"/>, and returns
    /// the work's result, such as its times. A warm-up stays outside, before the call.
    /// </summary>
    public static T Run<T>(Func<T> work)
    {
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        output.WriteLine(Start);
        T result = work();
        output.WriteLine(End);
        return result;
    }
}
