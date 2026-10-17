using System.Numerics;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// joltc entry points that JoltPhysicsSharp 2.23.0 binds with the wrong signature for double precision, or binds only in a
/// form that allocates.
/// </summary>
internal static unsafe class JoltNative
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

    [DllImport(Library, EntryPoint = "JPH_ContactManifold_GetWorldSpaceContactPointOn1", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ContactPointOn1(nint manifold, uint index, RVector3* result);

    [DllImport(Library, EntryPoint = "JPH_ContactManifold_GetWorldSpaceContactPointOn2", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ContactPointOn2(nint manifold, uint index, RVector3* result);

    // JoltPhysicsSharp binds this one correctly, but only on a managed Body, which it looks up in a locked table and can
    // allocate. This takes the body pointer of a BodyLockRead instead, so a ray cast allocates nothing.
    [DllImport(Library, EntryPoint = "JPH_Body_GetWorldSpaceSurfaceNormal", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SurfaceNormal(nint body, uint subShapeId, RVector3* position, Vector3* normal);
}
