using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// joltc entry points that JoltPhysicsSharp 2.23.0 binds with the wrong signature for double precision.
/// </summary>
internal static unsafe partial class JoltNative
{
    // The same library that JoltPhysicsSharp loads after Foundation.Init(doublePrecision: true). It sits next to the
    // executable, so the default probing finds libjoltc_double.dylib (or .so) and dlopen returns the same handle.
    private const string Library = "joltc_double";

    // JoltPhysicsSharp passes a 12-byte Vector3* to these, but the double-precision library writes a 24-byte RVec3
    // through it, which overwrites the caller's stack. These declarations take the correct RVector3*.

    [LibraryImport(Library, EntryPoint = "JPH_ContactManifold_GetWorldSpaceContactPointOn1")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ContactPointOn1(nint manifold, uint index, RVector3* result);

    [LibraryImport(Library, EntryPoint = "JPH_ContactManifold_GetWorldSpaceContactPointOn2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ContactPointOn2(nint manifold, uint index, RVector3* result);
}
