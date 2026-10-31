using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ThreadState = System.Threading.ThreadState;

namespace Tertius.Host.Tests;

/// <summary>
/// A call that blocks, as the simulation thread makes it, on a thread of its own. A dedicated thread starts at once
/// also when the thread pool is busy. A test that closes the window does it on the test thread with
/// <see cref="CancellationTokenSource.Cancel()"/>, not with a timer of the pool, so that
/// <see cref="CaptureMailboxTests.Bound"/> measures the code and not the pool's delays.
/// </summary>
internal sealed class BlockingCall
{
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

    /// <summary>Returns once the call blocks in a wait, or has ended. Fails if neither happens within
    /// <see cref="CaptureMailboxTests.Bound"/>.</summary>
    public void WaitUntilBlocked()
    {
        var clock = Stopwatch.StartNew();
        while ((thread.ThreadState & (ThreadState.WaitSleepJoin | ThreadState.Stopped)) == 0)
        {
            clock.Elapsed.Should().BeLessThan(CaptureMailboxTests.Bound, "the call must start to wait");
            Thread.Yield();
        }
    }

    /// <summary>True if the call ends within <paramref name="time"/>.</summary>
    public bool EndsWithin(TimeSpan time) => thread.Join(time);

    /// <summary>Waits for the call to end, and throws on the test thread what the call threw. Fails if the call does
    /// not end within <see cref="CaptureMailboxTests.Bound"/>.</summary>
    public void End()
    {
        EndsWithin(CaptureMailboxTests.Bound).Should().BeTrue("the call must end");
        if (thrown is not null)
            ExceptionDispatchInfo.Throw(thrown);
    }
}
