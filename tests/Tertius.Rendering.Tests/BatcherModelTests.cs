using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Models in the batch (ARCHITECTURE.md, "Models"): every collector sees each part of a model as an ordinary instance,
/// so the main view culls each part by its own bounds, and the placement history keys each part by its entity and part
/// index. No GPU needed.
/// </summary>
public sealed class BatcherModelTests
{
    private const int Width = 1600,
        Height = 900;

    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 0.866f, Deform: null, Closed: true),
        new(FirstIndex: 36, IndexCount: 600, VertexOffset: 24, Radius: 1f, Deform: null, Closed: true),
    ];

    private static readonly Extent2D Size = new(Width, Height);

    private static readonly Quaternion Quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);

    /// <summary>A box at the entity's origin, and a ball 40 m along the entity's +X.</summary>
    private static readonly ModelDesc Boom = new(
        [
            new(Mesh: new(0), Material: new(1), Offset: Pose.Identity, Scale: Vector3.One, BoundingRadius: 0.866f),
            new(
                Mesh: new(1),
                Material: new(2),
                Offset: new(new(40, 0, 0), Quaternion.Identity),
                Scale: Vector3.One,
                BoundingRadius: 1
            ),
        ]
    );

    private readonly FrameHistory history = new(StillRule.Window);
    private readonly SceneBatcher batcher = Batchers.Default();

    private PreparedScene Draw(RenderSnapshot snapshot, float alpha = 1f)
    {
        var scene = new PreparedScene();
        FrameHistory.PendingFrame pending = history.Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: snapshot,
            outputSize: Size,
            renderSize: Size
        );
        batcher.Prepare(
            snapshot: snapshot,
            alpha: alpha,
            highestStops: Batchers.HighestStops,
            meshes: Meshes,
            materials: MaterialSlots.Of(8),
            models: [Boom],
            environment: default,
            region: DeformRegion.None,
            frame: pending,
            scene: scene
        );
        history.Drew(pending);
        return scene;
    }

    /// <summary>A snapshot of the boom's entity at <paramref name="position"/> turned by <paramref name="rotation"/>, still since the last tick.</summary>
    private static RenderSnapshot BoomAt(Vector3 position, Quaternion rotation)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        RenderInstance instance = Gpu.Instance(
            mesh: MeshId.None,
            material: MaterialId.None,
            position: position,
            scale: Vector3.One,
            chainDepth: TestChains.CamerasFrame,
            rotation: rotation,
            entity: 4
        );
        instance.Model = new(0);
        snapshot.Instances.Add(instance);
        return snapshot;
    }

    private static Vector3 TranslationOf(in GpuMotion motion) =>
        new(motion.PreviousRow0.W, motion.PreviousRow1.W, motion.PreviousRow2.W);

    [Fact]
    public void APartIsDrawnWhenItIsInTheViewAndItsEntitysOriginIsNot()
    {
        // The camera looks down -Z. The entity stands 40 m to the left, out of the view, and its boom reaches to x = 0.
        PreparedScene scene = Draw(BoomAt(new(-40, 0, -10), Quaternion.Identity));

        PreparedInstance ball = scene.VisibleSpan.ToArray().Should().ContainSingle().Subject;
        ball.Part.Should().Be(1);
        ball.Position.Should().Be(new Vector3(0, 0, -10));
    }

    [Fact]
    public void APartOutsideTheViewIsCulledByItsOwnBounds()
    {
        // The entity stands in the view, and its boom reaches 40 m to the right, out of it.
        PreparedScene scene = Draw(BoomAt(new(0, 0, -10), Quaternion.Identity));

        scene.VisibleSpan.ToArray().Select(v => v.Part).Should().Equal((ushort)0);
        scene.PlacedCount.Should().Be(2, "the culled part is still placed for the other views");
    }

    [Fact]
    public void EachPartOfAMovingAndTurningEntityMovesFromTheRowsThatTheLastFrameDrewForThatPart()
    {
        // In the first frame the entity's boom points along +X from x = -40, so the ball is at the view's center. Then the
        // entity moves 2 m up and turns a quarter about Y, so the boom points along -Z.
        Draw(BoomAt(new(-40, 0, -10), Quaternion.Identity));
        RenderSnapshot moved = BoomAt(new(-40, 2, -10), Quarter);

        PreparedScene scene = Draw(moved);

        GpuMotion[] motions = scene.MotionSpan.ToArray();
        motions.Should().HaveCount(2);
        motions.Should().OnlyContain(m => (MotionKind)m.Kind == MotionKind.Moved);
        TranslationOf(motions[0]).Should().Be(new Vector3(-40, 0, -10));
        TranslationOf(motions[1]).Should().Be(new Vector3(0, 0, -10), "the ball's last place was at the boom's end");
        Vector3.Distance(scene.PlacedSpan[1].Position, new(-40, 2, -50)).Should().BeLessThan(1e-4f);
    }

    [Fact]
    public void AModelInstanceCountsOnceAmongTheSubmittedInstances()
    {
        PreparedScene scene = Draw(BoomAt(new(0, 0, -10), Quaternion.Identity));

        scene.SubmittedInstances.Should().Be(1);
    }

    [Fact]
    public void AModelInstanceThatIsNotVisibleShowsNoPart()
    {
        RenderSnapshot snapshot = BoomAt(new(-20, 0, -10), Quaternion.Identity);
        RenderInstance hidden = snapshot.Instances[0];
        hidden.Flags = RenderFlags.None;
        snapshot.Instances[0] = hidden;

        PreparedScene scene = Draw(snapshot);

        scene.VisibleCount.Should().Be(0);
        scene.PlacedCount.Should().Be(0);
    }
}
