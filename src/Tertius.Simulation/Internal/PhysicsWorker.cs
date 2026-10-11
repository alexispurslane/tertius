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
    private readonly SemaphoreSlim start = new(0, 1);
    private readonly ManualResetEventSlim done = new(true);
    private PhysicsStepContext context;
    private ExceptionDispatchInfo? error;
    private volatile bool stopping;
    private bool running;

    public PhysicsWorker(IPhysicsBackend backend, PhysicsStepOutput output)
    {
        this.backend = backend;
        this.output = output;
        thread = new Thread(Loop) { IsBackground = true, Name = "Tertius physics" };
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
        start.Release();
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
        start.Release();
        thread.Join();
        start.Dispose();
        done.Dispose();
    }
}
