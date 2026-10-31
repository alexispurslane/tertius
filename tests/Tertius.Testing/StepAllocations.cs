using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;
using Tertius.Physics;

namespace Tertius.Testing;

/// <summary>
/// A world's physics backend, plus a count of what the threads of the world's steps allocate: the thread that ticks the
/// world, the physics thread, and Jolt's job threads, where the contact callbacks run. Other threads of the process, such
/// as the finalizer thread, do not count (ter-a00h).
/// </summary>
/// <remarks>
/// It forwards every call unchanged. Gameplay workers do not count, so the world runs gameplay on its ticking thread. While
/// it lives, joltc's contact callbacks for the whole process go through counting wrappers, so only one lives at a time.
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
    /// Wraps <paramref name="backend"/>, which it then owns. Throws <see cref="InvalidOperationException"/>, and leaves
    /// the backend to the caller, while another counter lives or before the process made a <see cref="JoltPhysicsBackend"/>.
    /// </summary>
    public StepAllocations(IPhysicsBackend backend)
    {
        if (live is not null)
            throw new InvalidOperationException(
                "A StepAllocations already wraps joltc's contact callbacks, which serve the whole process. Dispose it first."
            );
        this.backend = backend;
        backendCallbacks = BackendCallbacks();
        if (countingCallbacks is null)
        {
            countingCallbacks = (ContactListenerProcs*)NativeMemory.AllocZeroed((nuint)sizeof(ContactListenerProcs));
            countingCallbacks->OnContactAdded = &OnContactAdded;
            countingCallbacks->OnContactPersisted = &OnContactPersisted;
            countingCallbacks->OnContactRemoved = &OnContactRemoved;
        }

        countingCallbacks->OnContactValidate = backendCallbacks->OnContactValidate;
        JoltNative.ContactListenerSetProcs(countingCallbacks);
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

    /// <summary>The table of the backend's contact callbacks, which the backend gave joltc when it was first made.</summary>
    private static ContactListenerProcs* BackendCallbacks()
    {
        FieldInfo field =
            typeof(ContactListener).GetField("callbacks", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "ContactListener has no static field 'callbacks', so the count cannot wrap the contact callbacks."
            );
        var callbacks = (ContactListenerProcs*)Pointer.Unbox(field.GetValue(null)!);
        return callbacks is not null
            ? callbacks
            : throw new InvalidOperationException("No JoltPhysicsBackend gave joltc its contact callbacks yet.");
    }

    /// <summary>
    /// Adds what a callback allocated on this thread since <paramref name="before"/>, unless this is the physics thread,
    /// whose whole step counts already.
    /// </summary>
    private static void CountJobThread(long before)
    {
        if (Environment.CurrentManagedThreadId == physicsThread)
            return;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes != 0)
            Interlocked.Add(ref jobThreadBytes, bytes);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnContactAdded(
        nint userData,
        nint body1,
        nint body2,
        nint manifold,
        ContactSettings* settings
    )
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
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
        long before = GC.GetAllocatedBytesForCurrentThread();
        backendCallbacks->OnContactPersisted(userData, body1, body2, manifold, settings);
        CountJobThread(before);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnContactRemoved(nint userData, SubShapeIDPair* pair)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        backendCallbacks->OnContactRemoved(userData, pair);
        CountJobThread(before);
    }
}
