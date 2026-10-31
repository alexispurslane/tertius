using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ThreadState = System.Threading.ThreadState;

namespace Tertius.Host.Tests;

/// <summary>
/// Runs a call on a thread of its own, which starts at once also when the thread pool is busy. A test that closes the
/// window calls <see cref="CancellationTokenSource.Cancel()"/> itself, not a pool timer, so that <see cref="Bound"/>
/// measures the code and not the pool.
/// </summary>
internal sealed class BlockingCall
{
    /// <summary>
    /// How long a test waits for another thread before it fails, so that a bug fails the test instead of hanging the
    /// run. The call runs on a thread of its own and no pool timer closes the window, so the bound covers only the OS's
    /// scheduling of threads that can run.
    /// </summary>
    internal static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private readonly Thread thread;
    private Exception? thrown;

    private BlockingCall(Action call) =>
        thread = new(() =>
        {
            try
            {
                call();
            }
            catch (Exception exception)
            {
                thrown = exception;
            }
        })
        {
            IsBackground = true,
        };

    /// <summary>Starts <paramref name="call"/> on a thread of its own.</summary>
    public static BlockingCall Start(Action call)
    {
        var started = new BlockingCall(call);
        started.thread.Start();
        return started;
    }

    /// <summary>Returns once the call blocks in a wait, or has ended. Any wait counts, not only the one that the test
    /// means, so a test that acts after it must also hold if it acts before that wait. Fails if neither happens within
    /// <see cref="Bound"/>.</summary>
    public void WaitUntilBlocked()
    {
        var clock = Stopwatch.StartNew();
        while ((thread.ThreadState & (ThreadState.WaitSleepJoin | ThreadState.Stopped)) == 0)
        {
            clock.Elapsed.Should().BeLessThan(Bound, "the call must start to wait");
            Thread.Yield();
        }
    }

    /// <summary>True if the call ends within <paramref name="time"/>.</summary>
    public bool EndsWithin(TimeSpan time) => thread.Join(time);

    /// <summary>Waits for the call to end, and throws on the test thread what the call threw. Fails if the call does
    /// not end within <see cref="Bound"/>.</summary>
    public void End()
    {
        EndsWithin(Bound).Should().BeTrue("the call must end");
        if (thrown is not null)
            ExceptionDispatchInfo.Throw(thrown);
    }
}
