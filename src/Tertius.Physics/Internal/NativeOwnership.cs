using System.Runtime.CompilerServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// Says who frees each native Jolt object that the backend creates. Every JoltPhysicsSharp object that the backend creates
/// goes through <see cref="Owned"/> or <see cref="Disowned"/> before the backend uses it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NativeObject.Dispose()"/> destroys the native object only when <see cref="NativeObject.OwnsHandle"/> is true,
/// and JoltPhysicsSharp 2.23.0 does not set it the same way for every type. The constructors of
/// <see cref="CharacterVirtual"/>, <see cref="PhysicsSystem"/>, <see cref="JobSystemThreadPool"/>,
/// <see cref="BroadPhaseLayerInterfaceTable"/>, <see cref="BodyCreationSettings"/>, <see cref="ConvexHullShapeSettings"/> and
/// <see cref="MeshShapeSettings"/> leave it false, so a dispose of one of those frees nothing native. A character then
/// leaves its inner body in its system. The constructors of the other types set it true, also for the filter tables that
/// joltc's <c>JPH_PhysicsSystem_Destroy</c> deletes itself.
/// </para>
/// <para>
/// The backend disposes each object that it owns in the order that Jolt needs, for example a character before its system.
/// A finalizer runs in no order, on its own thread, so both methods turn it off. A native object that the backend never
/// disposes then stays in memory. It is never freed out of order or twice.
/// </para>
/// </remarks>
internal static class NativeOwnership
{
    /// <summary>Makes <see cref="NativeObject.Dispose()"/> of <paramref name="native"/> destroy the native object, and turns off its finalizer.</summary>
    public static T Owned<T>(T native) where T : NativeObject
    {
        SetOwnsHandle(native, true);
        GC.SuppressFinalize(native);
        return native;
    }

    /// <summary>
    /// Makes <see cref="NativeObject.Dispose()"/> of <paramref name="native"/> leave the native object alone, and turns off its
    /// finalizer. Use it for an object that another native object frees.
    /// </summary>
    public static T Disowned<T>(T native) where T : NativeObject
    {
        SetOwnsHandle(native, false);
        GC.SuppressFinalize(native);
        return native;
    }

    // The setter is protected. The runtime binds this accessor to it on the first call.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_OwnsHandle")]
    private static extern void SetOwnsHandle(NativeObject native, bool value);
}
