using System.Runtime.ExceptionServices;

namespace Tertius.Simulation;

/// <summary>
/// The dedicated physics thread. It is a long-lived thread, not a pool thread, so that a long step never starves the
/// gameplay workers. <see cref="Begin"/> hands it one step. <see cref="Wait"/> blocks until the step is done.
/// </summary>
internal sealed class PhysicsWorker : IDisposable
{
    private readonly IPhysicsBackend backend;
    private readonly PhysicsStepOutput output;
    private readonly Thread thread;
    // Both events block at once, with no spin. A spin and its yields before each block cost CPU and gave no speed: on
    // 2026-10-24 the Blue Danube generator wrote the same script in the same wall time with 8 to 15 percent less CPU, and
    // the Physics tests, which run many worlds at once, spent about 6 CPU-seconds in the yields.
    private readonly ManualResetEventSlim start = new(false, spinCount: 0);
    private readonly ManualResetEventSlim done = new(true, spinCount: 0);
    private PhysicsStepContext context;
    private ExceptionDispatchInfo? error;
    private volatile bool stopping;
    private bool running;

    public PhysicsWorker(IPhysicsBackend backend, PhysicsStepOutput output)
    {
        this.backend = backend;
        this.output = output;
        thread = new(Loop) { IsBackground = true, Name = "Tertius physics" };
        thread.Start();
    }

    public int ManagedThreadId => thread.ManagedThreadId;

    public void Begin(in PhysicsStepContext stepContext)
    {
        if (running)
            throw new InvalidOperationException("A physics step is already running.");
        context = stepContext;
        error = null;
        running = true;
        done.Reset();
        start.Set();
    }

    /// <summary>
    /// Waits for the running step. Returns the exception that the backend threw, if any, captured with the stack of the
    /// physics thread, so that the caller can throw it again on its own thread.
    /// </summary>
    public ExceptionDispatchInfo? Wait()
    {
        if (!running)
            return null;
        done.Wait();
        running = false;
        return error;
    }

    private void Loop()
    {
        while (true)
        {
            start.Wait();
            // Begin sets the event again only after Wait saw this step done, so this reset cannot lose a step.
            start.Reset();
            if (stopping)
                return;
            try
            {
                backend.Step(in context, output);
            }
            // The ticking thread decides what to do with it, a broken contract included.
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }

            done.Set();
        }
    }

    public void Dispose()
    {
        Wait();
        stopping = true;
        start.Set();
        thread.Join();
        start.Dispose();
        done.Dispose();
    }
}
