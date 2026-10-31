using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;
using Tertius.Physics;

namespace Tertius.Testing;

/// <summary>
/// A world's physics backend, plus a count of what the threads of the world's steps allocate: the ticking thread, the
/// physics thread, and the contact callbacks on Jolt's job threads. Other threads, such as the finalizer, do not count
/// (ter-a00h). Make one with <see cref="WorldTesting.NewWorldWithStepAllocations(IPhysicsBackend, out StepAllocations)"/>.
/// </summary>
/// <remarks>
/// It forwards every call unchanged. It does not count JoltPhysicsSharp's body activation callbacks on Jolt's threads
/// (ter-6dju). Only one lives at a time, because joltc's contact callbacks serve the whole process.
/// </remarks>
public sealed unsafe class StepAllocations : IPhysicsBackend
{
    // The counter whose wrappers joltc calls. A second one would wrap the wrappers and restore the wrong table.
    private static StepAllocations? live;

    // The backend's own callbacks, which the wrappers call.
    private static ContactListenerProcs* backendCallbacks;

    // joltc keeps the pointer to the table, so the table lives as long as the process, as the backend's does.
    private static ContactListenerProcs* countingCallbacks;

    // What the wrappers saw the callbacks allocate on Jolt's job threads, other than the physics thread.
    private static long jobThreadBytes;

    // The managed id of the thread that runs the steps. It runs Jolt's jobs too while it waits for an update.
    private static int physicsThread;

    private readonly IPhysicsBackend backend;

    // The physics thread's own count when its first step began, and the bytes that it allocated from then to the end of
    // its latest step. A step and the loop that waits for the next run on that thread alone.
    private long physicsThreadAtFirstStep = -1;
    private long physicsThreadBytes;

    /// <summary>
    /// Wraps <paramref name="backend"/>, which it owns from the call on: it disposes the backend when it throws. The
    /// world must run gameplay on its ticking thread, because gameplay workers do not count.
    /// </summary>
    /// <param name="forwardTo">The contact callbacks that the counting wrappers call, or null for the backend's own.</param>
    /// <exception cref="InvalidOperationException">
    /// Another counter lives, no <see cref="JoltPhysicsBackend"/> gave joltc its callbacks yet, or the callbacks include a
    /// validate callback, which the counter does not count.
    /// </exception>
    internal StepAllocations(IPhysicsBackend backend, ContactListenerProcs* forwardTo = null)
    {
        this.backend = backend;
        try
        {
            WrapContactCallbacks(forwardTo is null ? BackendCallbacks() : forwardTo);
        }
        catch
        {
            backend.Dispose();
            throw;
        }

        live = this;
    }

    /// <summary>
    /// The bytes that the threads of the world's steps allocate while <paramref name="window"/> runs. The window runs on
    /// this thread, which ticks the world, so what the window itself allocates counts too.
    /// </summary>
    /// <param name="window">The ticks to measure. Create the delegate before the call, as a lambda argument does.</param>
    public long Bytes(Action window)
    {
        long ticking = GC.GetAllocatedBytesForCurrentThread();
        long physics = physicsThreadBytes;
        long jobs = Volatile.Read(ref jobThreadBytes);
        window();
        return GC.GetAllocatedBytesForCurrentThread()
            - ticking
            + (physicsThreadBytes - physics)
            + (Volatile.Read(ref jobThreadBytes) - jobs);
    }

    public void ApplyChanges(PhysicsChanges changes, FrameTree frames) => backend.ApplyChanges(changes, frames);

    /// <summary>
    /// Runs the step on the physics thread and takes that thread's count at its end. The world waits for the step before
    /// <see cref="Bytes"/> reads the count.
    /// </summary>
    public void Step(in PhysicsStepContext context, PhysicsStepOutput output)
    {
        physicsThread = Environment.CurrentManagedThreadId;
        if (physicsThreadAtFirstStep < 0)
            physicsThreadAtFirstStep = GC.GetAllocatedBytesForCurrentThread();
        backend.Step(in context, output);
        physicsThreadBytes = GC.GetAllocatedBytesForCurrentThread() - physicsThreadAtFirstStep;
    }

    public void CastRay(
        FrameHandle frame,
        Double3 origin,
        Double3 direction,
        double start,
        double end,
        IPhysicsQueryFilter filter,
        RayCastKind kind,
        List<RayCastHit> hits
    ) =>
        backend.CastRay(
            frame: frame,
            origin: origin,
            direction: direction,
            start: start,
            end: end,
            filter: filter,
            kind: kind,
            hits: hits
        );

    public void CastShape(
        FrameHandle frame,
        in QueryShape shape,
        in DPose start,
        Double3 direction,
        double startDistance,
        double endDistance,
        IPhysicsQueryFilter filter,
        List<ShapeCastHit> hits
    ) =>
        backend.CastShape(
            frame: frame,
            shape: shape,
            start: start,
            direction: direction,
            startDistance: startDistance,
            endDistance: endDistance,
            filter: filter,
            hits: hits
        );

    public void Overlap(
        FrameHandle frame,
        in QueryShape shape,
        in DPose pose,
        IPhysicsQueryFilter filter,
        List<Entity> entities
    ) => backend.Overlap(frame: frame, shape: shape, pose: pose, filter: filter, entities: entities);

    public string? ShapeProblem(ShapeDesc shape) => backend.ShapeProblem(shape);

    public Vector3 ShapeCenterOfMass(ShapeDesc shape) => backend.ShapeCenterOfMass(shape);

    /// <summary>Gives joltc the backend's own contact callbacks again, then disposes the backend.</summary>
    public void Dispose()
    {
        if (live == this)
        {
            JoltNative.ContactListenerSetProcs(backendCallbacks);
            live = null;
        }

        backend.Dispose();
    }

    /// <summary>Gives joltc the counting wrappers of <paramref name="callbacks"/>, the table that they then call.</summary>
    private static void WrapContactCallbacks(ContactListenerProcs* callbacks)
    {
        if (live is not null)
            throw new InvalidOperationException(
                "A StepAllocations already wraps joltc's contact callbacks, which serve the whole process. Dispose it first."
            );
        if (callbacks->OnContactValidate != 0)
            throw new InvalidOperationException(
                "The contact callbacks include a validate callback, which runs on Jolt's threads and which the count does not wrap."
            );
        backendCallbacks = callbacks;
        if (countingCallbacks is null)
        {
            countingCallbacks = (ContactListenerProcs*)NativeMemory.AllocZeroed((nuint)sizeof(ContactListenerProcs));
            countingCallbacks->OnContactAdded = &OnContactAdded;
            countingCallbacks->OnContactPersisted = &OnContactPersisted;
            countingCallbacks->OnContactRemoved = &OnContactRemoved;
        }

        JoltNative.ContactListenerSetProcs(countingCallbacks);
    }

    /// <summary>The table of the backend's contact callbacks, which the backend gave joltc when it was first made.</summary>
    private static ContactListenerProcs* BackendCallbacks()
    {
        ContactListenerProcs* callbacks = ContactListener.Callbacks;
        return callbacks is not null
            ? callbacks
            : throw new InvalidOperationException("No JoltPhysicsBackend gave joltc its contact callbacks yet.");
    }

    /// <summary>
    /// This thread's count before a callback, or -1 on the physics thread, whose whole step counts already. It reads the
    /// thread's id first, so that what the read itself allocates on a new thread does not count.
    /// </summary>
    private static long JobThreadCountBefore() =>
        Environment.CurrentManagedThreadId == physicsThread ? -1 : GC.GetAllocatedBytesForCurrentThread();

    /// <summary>Adds what a callback allocated on this thread since <paramref name="before"/>, unless that is -1.</summary>
    private static void CountJobThread(long before)
    {
        if (before < 0)
            return;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes != 0)
            Interlocked.Add(ref jobThreadBytes, bytes);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnContactAdded(nint userData, nint body1, nint body2, nint manifold, ContactSettings* settings)
    {
        long before = JobThreadCountBefore();
        backendCallbacks->OnContactAdded(userData, body1, body2, manifold, settings);
        CountJobThread(before);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnContactPersisted(
        nint userData,
        nint body1,
        nint body2,
        nint manifold,
        ContactSettings* settings
    )
    {
        long before = JobThreadCountBefore();
        backendCallbacks->OnContactPersisted(userData, body1, body2, manifold, settings);
        CountJobThread(before);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnContactRemoved(nint userData, SubShapeIDPair* pair)
    {
        long before = JobThreadCountBefore();
        backendCallbacks->OnContactRemoved(userData, pair);
        CountJobThread(before);
    }
}
