using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// The joltc contact listener of one <see cref="FrameWorld"/>. Every physics system of the world uses it: the first one,
/// and each one that a growth makes. Jolt calls its callbacks on its worker threads during an update, and each callback
/// goes straight to the world's handler with the body and manifold pointers that Jolt passes. JoltPhysicsSharp's contact
/// events instead look up a managed wrapper for both bodies in a table behind a lock that only one thread holds at a time,
/// which made the reports of persisting contacts cost 15 % of a tick in a heap of 300 awake boxes (ter-wu6v, ter-pnco).
/// The added and persisted callbacks also pass Jolt's contact settings, through which the world's handlers apply the
/// minimum rebound (see <see cref="FrameWorld.MinReboundHeight"/>). JoltPhysicsSharp's <see cref="ContactSettings"/>
/// has the layout of joltc's <c>JPH_ContactSettings</c> (52 bytes, the combined restitution a float at offset 4), and
/// joltc copies the settings back to Jolt when a callback returns.
/// </summary>
/// <remarks>
/// <para>
/// joltc keeps one table of contact callbacks for the whole process. JoltPhysicsSharp fills it with its own in the static
/// constructor of <see cref="PhysicsSystem"/>, and <see cref="InstallCallbacks"/> then replaces them with this class's.
/// So the contact events of JoltPhysicsSharp never fire, and no code may subscribe to them.
/// </para>
/// <para>
/// JoltPhysicsSharp also gives each new system a listener of its own, with its own user data. That listener now calls
/// these callbacks, so <see cref="Attach"/> replaces it at once, in <see cref="FrameWorld.CreateSystem"/>, before the
/// system first updates. A callback that gets user data which is not a <see cref="FrameWorld"/> leaves the report and
/// counts it in <see cref="StrayReports"/>. Only a bug sends one: a system that kept the listener of JoltPhysicsSharp.
/// </para>
/// </remarks>
internal sealed unsafe class ContactListener : IDisposable
{
    // ---- The process-wide callbacks ----

    // The table that joltc reads. joltc keeps the pointer, so the table lives in native memory until the process ends.
    private static ContactListenerProcs* callbacks;
    private static int strayReports;

    /// <summary>The number of reports that reached these callbacks from a listener that is not a <see cref="ContactListener"/>
    /// .</summary>
    public static int StrayReports => Volatile.Read(ref strayReports);

    // Keeps the world alive, and lets a callback find it from the user data of the native listener.
    private GCHandle world;
    private nint handle;

    public ContactListener(FrameWorld world)
    {
        this.world = GCHandle.Alloc(world);
        handle = JoltNative.ContactListenerCreate(GCHandle.ToIntPtr(this.world));
    }

    /// <summary>Makes this the contact listener of <paramref name="system"/>, in place of the one that JoltPhysicsSharp gave it.</summary>
    public void Attach(PhysicsSystem system) => JoltNative.PhysicsSystemSetContactListener(system.Handle, handle);

    /// <summary>Destroys the native listener. Call it after the last system that uses it is destroyed.</summary>
    public void Dispose()
    {
        if (handle == 0)
            return;
        JoltNative.ContactListenerDestroy(handle);
        handle = 0;
        world.Free();
    }

    /// <summary>
    /// Makes joltc call this class's contact callbacks for every listener in the process. Call it once, after
    /// <c>Foundation.Init</c>, which JoltPhysicsSharp's static constructor of <see cref="PhysicsSystem"/> needs.
    /// </summary>
    public static void InstallCallbacks()
    {
        // That static constructor fills the table with JoltPhysicsSharp's callbacks. Run it now, so that it cannot
        // overwrite this table later, when the first system is created.
        RuntimeHelpers.RunClassConstructor(typeof(PhysicsSystem).TypeHandle);
        Contract.Requires(callbacks is null);
        callbacks = (ContactListenerProcs*)NativeMemory.AllocZeroed((nuint)sizeof(ContactListenerProcs));
        callbacks->OnContactAdded = &OnContactAdded;
        callbacks->OnContactPersisted = &OnContactPersisted;
        callbacks->OnContactRemoved = &OnContactRemoved;
        JoltNative.ContactListenerSetProcs(callbacks);
    }

    private static FrameWorld? WorldOf(nint userData)
    {
        if (userData != 0 && GCHandle.FromIntPtr(userData).Target is FrameWorld world)
            return world;
        Interlocked.Increment(ref strayReports);
        return null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnContactAdded(nint userData, nint body1, nint body2, nint manifold,
        ContactSettings* settings) =>
        WorldOf(userData)?.OnContactAdded(body1, body2, manifold, settings);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnContactPersisted(nint userData, nint body1, nint body2, nint manifold,
        ContactSettings* settings) =>
        WorldOf(userData)?.OnContactPersisted(body1, body2, manifold, settings);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnContactRemoved(nint userData, SubShapeIDPair* pair) =>
        WorldOf(userData)?.OnContactRemoved(pair->Body1ID, pair->Body2ID);
}
