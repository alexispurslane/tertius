using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The placement history (docs/plans/temporal-renderer.md, "The placement history"): the batch joins the snapshot's
/// instances with the keys and the rows of the last drawn frame, and gives each placed instance its motion row. An
/// instance that the last frame drew moves from the rows that the last frame drew; a new key, a new generation or a new
/// mesh is new; an instance without an identity is still in the camera's frame. No GPU needed.
/// </summary>
public sealed class BatcherMotionTests
{
    private const int Width = 1600,
        Height = 900;

    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 0.866f, Deform: null),
        new(FirstIndex: 36, IndexCount: 600, VertexOffset: 24, Radius: 1f, Deform: null),
    ];

    private static readonly Extent2D Size = new(Width, Height);

    private readonly FrameHistory history = new(StillRule.Window);
    private readonly SceneBatcher batcher = Batchers.Default();

    /// <summary>Batches <paramref name="snapshot"/> as the frame after the ones drawn so far, and commits it as drawn.</summary>
    private PreparedScene Draw(RenderSnapshot snapshot, float alpha = 1f)
    {
        var scene = new PreparedScene();
        FrameHistory.PendingFrame pending = history.Begin(step: FrameHistoryTesting.FilmStep, draw: 0, snapshot: snapshot, outputSize: Size, renderSize: Size);
        batcher.Prepare(
            snapshot: snapshot,
            alpha: alpha,
            highestStops: Batchers.HighestStops,
            width: Width,
            height: Height,
            meshes: Meshes,
            materials: MaterialSlots.Of(8),
            environment: default,
            region: DeformRegion.None,
            frame: pending,
            scene: scene
        );
        history.Drew(pending);
        return scene;
    }

    /// <summary>A snapshot with one instance of <paramref name="mesh"/> at <paramref name="position"/>, keyed by slot, generation and part.</summary>
    private static RenderSnapshot WithInstance(
        Vector3 position,
        uint slot = 3,
        uint generation = 1,
        int mesh = 0,
        Double3 origin = default
    )
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.OriginInFrame = origin;
        RenderInstance instance = Gpu.Instance(
            mesh: new(mesh),
            material: new(0),
            position: position,
            scale: Vector3.One,
            chainDepth: TestChains.CamerasFrame,
            entity: slot
        );
        instance.EntityGeneration = generation;
        snapshot.Instances.Add(instance);
        return snapshot;
    }

    private static Vector3 TranslationOf(in GpuMotion motion) =>
        new(motion.PreviousRow0.W, motion.PreviousRow1.W, motion.PreviousRow2.W);

    [Fact]
    public void AnInstanceThatTheLastFrameDrewMovesFromTheRowsThatItDrew()
    {
        Draw(WithInstance(position: new(0, 0, -10)));

        PreparedScene scene = Draw(WithInstance(position: new(1, 0, -10)));

        GpuMotion motion = scene.Motions[0];
        ((MotionKind)motion.Kind).Should().Be(MotionKind.Moved);
        TranslationOf(motion).Should().Be(new Vector3(0, 0, -10));
        motion.PreviousRow0.Should().Be(Vector4.UnitX, "the instance did not turn or grow");
    }

    [Fact]
    public void ThePreviousRowsAreTheRowsThatTheLastFrameDrewAtItsInterpolationFraction()
    {
        RenderSnapshot snapshot = WithInstance(position: new(2, 0, -10));
        RenderInstance moving = snapshot.Instances[0];
        moving.PreviousPosition = new(0, 0, -10);
        snapshot.Instances[0] = moving;
        Draw(snapshot, alpha: 0.5f);

        PreparedScene scene = Draw(snapshot, alpha: 1f);

        TranslationOf(scene.Motions[0])
            .Should()
            .Be(
                new Vector3(1, 0, -10),
                "the last frame drew the instance halfway, not at the snapshot's previous place"
            );
    }

    [Fact]
    public void ThePreviousRowsOfAStillObjectAreRelativeToTheLastFramesEye()
    {
        // The camera rose by 1 m, so the object, still in the frame, stands 1 m lower relative to this eye.
        Draw(WithInstance(position: new(0, 0, -10)));

        PreparedScene scene = Draw(WithInstance(position: new(0, -1, -10), origin: new(0, 1, 0)));

        TranslationOf(scene.Motions[0]).Should().Be(new Vector3(0, 0, -10));
        scene.Constants.PreviousEye.Should().Be(new Vector4(x: 0, y: -1, z: 0, w: 0));
    }

    [Fact]
    public void AnInstanceThatTheLastFrameDidNotDrawIsNew()
    {
        Draw(WithInstance(position: new(0, 0, -10), slot: 3));

        PreparedScene scene = Draw(WithInstance(position: new(0, 0, -10), slot: 4));

        ((MotionKind)scene.Motions[0].Kind).Should().Be(MotionKind.New);
    }

    [Fact]
    public void EveryInstanceOfTheFirstFrameIsNew()
    {
        PreparedScene scene = Draw(WithInstance(position: new(0, 0, -10)));

        ((MotionKind)scene.Motions[0].Kind).Should().Be(MotionKind.New);
    }

    [Fact]
    public void AnInstanceInAReusedSlotWithAnotherGenerationIsNew()
    {
        Draw(WithInstance(position: new(0, 0, -10), generation: 1));

        PreparedScene scene = Draw(WithInstance(position: new(0, 0, -10), generation: 2));

        ((MotionKind)scene.Motions[0].Kind).Should().Be(MotionKind.New);
    }

    [Fact]
    public void AnInstanceWhoseMeshChangedIsNew()
    {
        Draw(WithInstance(position: new(0, 0, -10), mesh: 0));

        PreparedScene scene = Draw(WithInstance(position: new(0, 0, -10), mesh: 1));

        ((MotionKind)scene.Motions[0].Kind).Should().Be(MotionKind.New);
    }

    [Fact]
    public void AnInstanceAfterAKeyThatTheLastFrameDidNotDrawStillFindsItsOwnKey()
    {
        RenderSnapshot first = WithInstance(position: new(0, 0, -10), slot: 3);
        first.Instances.Add(WithInstance(position: new(0, 0, -20), slot: 8).Instances[0]);
        Draw(first);
        RenderSnapshot second = WithInstance(position: new(0, 0, -10), slot: 5);
        second.Instances.Add(WithInstance(position: new(0, 0, -21), slot: 8).Instances[0]);

        PreparedScene scene = Draw(second);

        ((MotionKind)scene.Motions[0].Kind).Should().Be(MotionKind.New);
        ((MotionKind)scene.Motions[1].Kind).Should().Be(MotionKind.Moved);
        TranslationOf(scene.Motions[1]).Should().Be(new Vector3(0, 0, -20));
    }

    [Fact]
    public void AnInstanceWhoseKeyIsNotAboveTheOneBeforeItIsStillInTheCamerasFrame()
    {
        // A hand-built snapshot whose two instances both keep slot 0 and part 0: the second has no identity.
        RenderSnapshot first = WithInstance(position: new(0, 0, -10), slot: 0);
        first.Instances.Add(WithInstance(position: new(4, 0, -10), slot: 0).Instances[0]);
        Draw(first);
        RenderSnapshot second = WithInstance(position: new(0, -1, -10), slot: 0, origin: new(0, 1, 0));
        second.Instances.Add(WithInstance(position: new(4, -1, -10), slot: 0, origin: new(0, 1, 0)).Instances[0]);

        PreparedScene scene = Draw(second);

        GpuMotion still = scene.Motions[1];
        ((MotionKind)still.Kind).Should().Be(MotionKind.Still);
        TranslationOf(still).Should().Be(new Vector3(4, 0, -10), "this frame's place, moved by the eye's step");
    }

    [Fact]
    public void AGrowingInstanceDrawsAtItsInterpolatedScale()
    {
        RenderSnapshot snapshot = WithInstance(position: new(0, 0, -10));
        RenderInstance growing = snapshot.Instances[0];
        growing.Scale = new(3);
        growing.PreviousScale = new(1);
        snapshot.Instances[0] = growing;

        PreparedScene scene = Draw(snapshot, alpha: 0.5f);

        scene.Placed[0].Scale.Should().Be(new Vector3(2));
    }

    [Fact]
    public void AnInstanceWithoutAPreviousScaleDrawsAtItsScale()
    {
        RenderSnapshot snapshot = WithInstance(position: new(0, 0, -10));
        RenderInstance instance = snapshot.Instances[0];
        instance.Scale = new(3);
        instance.PreviousScale = Vector3.Zero;
        snapshot.Instances[0] = instance;

        PreparedScene scene = Draw(snapshot, alpha: 0.5f);

        scene.Placed[0].Scale.Should().Be(new Vector3(3));
    }

    [Fact]
    public void AnInstanceThatMirrorsBetweenTwoTicksDrawsAtItsScaleNotAFlatOne()
    {
        RenderSnapshot snapshot = WithInstance(position: new(0, 0, -10));
        RenderInstance mirrored = snapshot.Instances[0];
        mirrored.Scale = new(1, 1, 1);
        mirrored.PreviousScale = new(-1, 1, 1);
        snapshot.Instances[0] = mirrored;

        PreparedScene scene = Draw(snapshot, alpha: 0.5f);

        scene.Placed[0].Scale.Should().Be(new Vector3(1, 1, 1), "half way between −1 and 1 the mesh has no volume");
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0.3f, 0f, 0f)]
    [InlineData(0.2f, -0.4f, 0.1f)]
    public void TheMotionProjectionPlacesAPointWhereTheTurnedCameraSeesIt(float yaw, float pitch, float roll)
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
        float tanY = MathF.Tan(Gpu.Fov / 2);
        float tanX = tanY * Width / Height;
        Matrix4x4 projection = SceneBatcher.MotionProjection(rotation: rotation, tanHalfX: tanX, tanHalfY: tanY);

        foreach (Vector3 inView in (Vector3[])[new(0, 0, -5), new(1.5f, 0.8f, -6), new(-2, -1, -4)])
        {
            var point = Vector3.Transform(inView, rotation);
            var clip = Vector4.Transform(new Vector4(point, 1), projection);
            var pixel = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * Width, (clip.Y / clip.W * 0.5f + 0.5f) * Height);

            Vector2 expected = Gpu.Project(point: point, cameraRotation: rotation, width: Width, height: Height);
            (pixel - expected).Length().Should().BeLessThan(1e-3f, "the point {0} in the camera's view", inView);
            clip.W.Should().BeApproximately(-inView.Z, 1e-5f, "clip w is the view depth");
        }
    }

    [Fact]
    public void AGrowingInstancesBoundFollowsItsInterpolatedSize()
    {
        RenderSnapshot snapshot = WithInstance(position: new(0, 0, -10));
        RenderInstance growing = snapshot.Instances[0];
        growing.Scale = new(4);
        growing.PreviousScale = new(2);
        snapshot.Instances[0] = growing;

        PreparedScene scene = Draw(snapshot, alpha: 0.5f);

        scene.Placed[0].Radius.Should().BeApproximately(3 * Meshes[0].Radius, 1e-5f);
    }

    [Fact]
    public void TheFrameConstantsHoldTheLastFramesCameraAndRenderSize()
    {
        RenderSnapshot first = WithInstance(position: new(0, 0, -10));
        first.Camera.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f);
        first.Camera.PreviousRotation = first.Camera.Rotation;
        Draw(first);

        PreparedScene scene = Draw(WithInstance(position: new(0, 0, -10)));

        var lastForward = Vector3.Transform(-Vector3.UnitZ, first.Camera.Rotation);
        var forward = new Vector3(
            scene.Constants.PreviousCameraForward.X,
            scene.Constants.PreviousCameraForward.Y,
            scene.Constants.PreviousCameraForward.Z
        );
        (forward - lastForward).Length().Should().BeLessThan(1e-6f);
        scene.Constants.PreviousViewport.Should().Be(new Vector4(x: Width, y: Height, z: 1f / Width, w: 1f / Height));
    }

    [Fact]
    public void TheHistoryKeepsTheInvariantsOfItsPlacementsAfterABatch()
    {
        RenderSnapshot snapshot = WithInstance(position: new(0, 0, -10), slot: 2);
        snapshot.Instances.Add(WithInstance(position: new(1, 0, -10), slot: 2).Instances[0]);
        snapshot.Instances.Add(WithInstance(position: new(2, 0, -10), slot: 6).Instances[0]);

        Draw(snapshot);

        history.Invoking(h => h.AssertInvariants()).Should().NotThrow();
    }
}
