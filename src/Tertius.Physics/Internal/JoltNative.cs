using System.Numerics;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// joltc entry points that JoltPhysicsSharp 2.23.0 binds with the wrong signature for double precision, binds only in a
/// form that allocates, or binds only behind its own contact events (see <see cref="ContactListener"/>).
/// </summary>
internal static class JoltNative
{
    // The same library that JoltPhysicsSharp loads after Foundation.Init(doublePrecision: true). It sits next to the
    // executable, so the default probing finds libjoltc_double.dylib (or .so) and dlopen returns the same handle.
    private const string Library = "joltc_double";

    // JoltPhysicsSharp passes a 12-byte Vector3* to these, but the double-precision library writes a 24-byte RVec3
    // through it, which overwrites the caller's stack. These declarations take the correct RVector3*.
    //
    // They use DllImport, not LibraryImport. Every argument is blittable, so both compile to the same direct call,
    // and DllImport needs no source generator: the generator does not load in Stryker.NET's bundled compiler, which
    // then cannot build the project for mutation testing.

    [DllImport(Library, EntryPoint = "JPH_ContactManifold_GetWorldSpaceContactPointOn1",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void ContactPointOn1(nint manifold, uint index, RVector3* result);

    [DllImport(Library, EntryPoint = "JPH_ContactManifold_GetWorldSpaceContactPointOn2",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void ContactPointOn2(nint manifold, uint index, RVector3* result);

    // JoltPhysicsSharp binds this one correctly, but only on a managed Body, which it looks up in a locked table and can
    // allocate. This takes the body pointer of a BodyLockRead instead, so a ray cast allocates nothing.
    [DllImport(Library, EntryPoint = "JPH_Body_GetWorldSpaceSurfaceNormal",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void SurfaceNormal(nint body, uint subShapeId, RVector3* position, Vector3* normal);

    // ---- The contact listener ----

    // joltc keeps one table of contact callbacks for the whole process and calls them for every listener. It keeps the
    // pointer, not a copy, so the table must live as long as the process.
    [DllImport(Library, EntryPoint = "JPH_ContactListener_SetProcs", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ContactListenerSetProcs(ContactListenerProcs* procs);

    [DllImport(Library, EntryPoint = "JPH_ContactListener_Create", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint ContactListenerCreate(nint userData);

    [DllImport(Library, EntryPoint = "JPH_ContactListener_Destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ContactListenerDestroy(nint listener);

    [DllImport(Library, EntryPoint = "JPH_PhysicsSystem_SetContactListener",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void PhysicsSystemSetContactListener(nint system, nint listener);

    // ---- What a contact callback reads, on the body and manifold pointers that Jolt passes ----

    // JoltPhysicsSharp binds these only on its managed Body, which each callback would have to look up for both bodies.

    [DllImport(Library, EntryPoint = "JPH_Body_GetID", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint BodyId(nint body);

    [DllImport(Library, EntryPoint = "JPH_Body_GetUserData", CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong BodyUserData(nint body);

    [DllImport(Library, EntryPoint = "JPH_Body_GetCenterOfMassPosition", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BodyCenterOfMass(nint body, RVector3* result);

    [DllImport(Library, EntryPoint = "JPH_Body_GetLinearVelocity", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BodyLinearVelocity(nint body, Vector3* result);

    [DllImport(Library, EntryPoint = "JPH_Body_GetAngularVelocity", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BodyAngularVelocity(nint body, Vector3* result);

    [DllImport(Library, EntryPoint = "JPH_ContactManifold_GetPointCount", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint ManifoldPointCount(nint manifold);

    [DllImport(Library, EntryPoint = "JPH_ContactManifold_GetWorldSpaceNormal",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void ManifoldNormal(nint manifold, Vector3* result);

    [DllImport(Library, EntryPoint = "JPH_ContactManifold_GetPenetrationDepth",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern float ManifoldPenetrationDepth(nint manifold);

    // What the minimum rebound reads of a body's push, as Jolt does when it takes that push off a rebound. Only a dynamic
    // body has an accumulated force and a mass: Jolt asserts on the force of any other, and a static body has no motion
    // properties. joltc's bool is one byte, so the declaration returns a byte, which needs no marshaling.

    [DllImport(Library, EntryPoint = "JPH_Body_IsDynamic", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte BodyIsDynamic(nint body);

    [DllImport(Library, EntryPoint = "JPH_Body_GetAccumulatedForce", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BodyAccumulatedForce(nint body, Vector3* force);

    // What the update of a turning frame reads of a torque of the intent, to turn its change of the angular velocity as Jolt
    // will apply it (see JoltPhysicsBackend.ApplyTurningUpdate). The inverse inertia in the frame's axes is symmetric, so
    // joltc's column-major matrix reads the same as a row-major Matrix4x4.

    [DllImport(Library, EntryPoint = "JPH_Body_GetAccumulatedTorque", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BodyAccumulatedTorque(nint body, Vector3* torque);

    [DllImport(Library, EntryPoint = "JPH_Body_GetInverseInertia", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BodyInverseInertia(nint body, Matrix4x4* inverseInertia);

    [DllImport(Library, EntryPoint = "JPH_Body_GetMotionPropertiesUnchecked",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern nint BodyMotionProperties(nint body);

    [DllImport(Library, EntryPoint = "JPH_MotionProperties_GetInverseMassUnchecked",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern float MotionPropertiesInverseMass(nint properties);

    // What a report reads of the two bodies of a contact with a ghost, which meets only what its real body meets (see
    // FrameWorld.ContactMotionsMeet), with BodyIsDynamic above.

    [DllImport(Library, EntryPoint = "JPH_Body_IsKinematic", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte BodyIsKinematic(nint body);

    [DllImport(Library, EntryPoint = "JPH_Body_IsSensor", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte BodyIsSensor(nint body);
}

/// <summary>
/// joltc's <c>JPH_ContactListener_Procs</c>: the four contact callbacks, in its order. A null callback is not called. For
/// a null <see cref="OnContactValidate"/>, joltc accepts every contact, as JoltPhysicsSharp does without a handler.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ContactListenerProcs
{
    public nint OnContactValidate;
    public delegate* unmanaged[Cdecl]<nint, nint, nint, nint, ContactSettings*, void> OnContactAdded;
    public delegate* unmanaged[Cdecl]<nint, nint, nint, nint, ContactSettings*, void> OnContactPersisted;
    public delegate* unmanaged[Cdecl]<nint, SubShapeIDPair*, void> OnContactRemoved;
}
