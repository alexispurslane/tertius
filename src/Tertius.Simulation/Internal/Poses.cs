using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tertius.Simulation;

/// <summary>Pose helpers shared by the subsystems.</summary>
internal static class Poses
{
    /// <summary>A pose with a usable rotation: a zeroed quaternion, as in a defaulted struct, becomes the identity.</summary>
    public static Pose Safe(Pose pose) =>
        pose.Rotation == default ? pose with { Rotation = Quaternion.Identity } : pose;

    /// <summary>True if two poses are equal bit for bit. Unlike <c>==</c>, it tells 0 from -0 and treats NaN as equal to itself.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BitEquals(in DPose a, in DPose b) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<DPose>(in a)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<DPose>(in b)));
}
