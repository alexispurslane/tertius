using System.Numerics;
using System.Runtime.InteropServices;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// A hash of the state that the sync point writes, so that the hash run (<c>--hash</c>, see <see cref="SyncSampler"/>) can
/// compare a change of the engine with the code before it bit for bit (ter-sn28). It reads the world through the internals
/// that the engine shows to this project, so the engine carries no code for it.
/// </summary>
internal static class WorldStateHash
{
    /// <summary>
    /// The FNV-1a hash of the front and back transform rows of every live entity, in slot order, then of the local pose,
    /// the velocities, the proper acceleration and the follow kind of every frame, numeric frames included, in dense order
    /// (the last two since ter-6azq).
    /// </summary>
    public static ulong Of(World world)
    {
        ulong hash = 14695981039346656037;

        void Mix(ReadOnlySpan<byte> bytes)
        {
            foreach (byte b in bytes)
                hash = (hash ^ b) * 1099511628211;
        }

        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        live.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (Entity entity in live)
        {
            TransformRow front = world.Entities.FrontRow(entity),
                back = world.Entities.BackRow(entity);
            Mix(MemoryMarshal.AsBytes(new ReadOnlySpan<TransformRow>(in front)));
            Mix(MemoryMarshal.AsBytes(new ReadOnlySpan<TransformRow>(in back)));
        }

        for (int i = 0; i < world.Frames.Count; i++)
        {
            FrameHandle frame = world.Frames.AtDense(i);
            DPose local = world.Frames.TreeLocalPose(frame);
            Double3 velocity = world.Frames.TreeLinearVelocity(frame);
            Vector3 angular = world.Frames.AngularVelocity(frame);
            Mix(MemoryMarshal.AsBytes(new ReadOnlySpan<DPose>(in local)));
            Mix(MemoryMarshal.AsBytes(new ReadOnlySpan<Double3>(in velocity)));
            Mix(MemoryMarshal.AsBytes(new ReadOnlySpan<Vector3>(in angular)));
            Vector3 proper = world.Frames.ProperAcceleration(frame);
            FollowKind followed = world.Frames.Followed(frame);
            Mix(MemoryMarshal.AsBytes(new ReadOnlySpan<Vector3>(in proper)));
            Mix(MemoryMarshal.AsBytes(new ReadOnlySpan<FollowKind>(in followed)));
        }

        return hash;
    }
}
