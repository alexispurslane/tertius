using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Helpers for the render snapshot tests. A render snapshot lists what to draw, with every position given relative to the
/// render origin, the position of the camera at the tick of the snapshot. The camera is an entity: its pose composed with its
/// view offset is the eye.
/// </summary>
internal static class RenderSnapshotSupport
{
    /// <summary>A render row that draws mesh <paramref name="mesh"/> at scale 2 with material 3.</summary>
    public static RenderData Visible(int mesh = 0, float radius = 1) => new()
    {
        Mesh = new(mesh),
        Material = new(3),
        Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
        BoundingRadius = radius,
        Scale = new(2)
    };

    /// <summary>Spawns a visible entity at <paramref name="pose"/> in <paramref name="frame"/>.</summary>
    public static Entity Show(this TestWorld test, FrameHandle frame, DPose pose, params IComponent[] components) =>
        test.World.Spawn(new() { Frame = frame, Pose = pose, Render = Visible(), Components = components });

    /// <summary>Spawns an entity with a render row that draws nothing, to be the camera.</summary>
    public static Entity SpawnCamera(this TestWorld test, FrameHandle frame, DPose pose,
        params IComponent[] components) =>
        test.World.Spawn(new() { Frame = frame, Pose = pose, Render = new RenderData(), Components = components });

    /// <summary>The snapshot that the last tick published. It checks that one was published since the last call.</summary>
    public static RenderSnapshot Latest(this World world)
    {
        world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot).Should()
            .BeTrue("a snapshot is published at the end of every tick");
        return snapshot;
    }

    /// <summary>The one instance that <paramref name="entity"/> contributes to the snapshot.</summary>
    public static RenderInstance InstanceOf(this RenderSnapshot snapshot, Entity entity) =>
        snapshot.Instances.Should().ContainSingle(i => i.EntityIndex == entity.Index).Subject;

    /// <summary>
    /// The id that a snapshot gives the frame it is relative to: the generation of the frame in the high 32 bits and its
    /// index in the low 32 bits.
    /// </summary>
    public static long IdOf(FrameHandle frame) => ((long)frame.Generation << 32) | (uint)frame.Index;
}
