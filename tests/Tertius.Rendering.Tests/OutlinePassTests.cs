using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The CPU half of the overlay pass: how <see cref="OutlinePass.Collect"/> turns the frame outlines of a snapshot into
/// <see cref="GpuOutline"/> records. No GPU needed. The camera of every test is at the render origin with identity
/// rotation, so it looks down −Z.
/// </summary>
public sealed class OutlinePassTests
{
    private static readonly MeshInfo[] NoMeshes = [];

    private static RenderSnapshot Empty() => Gpu.Snapshot(Vector3.UnitY);

    private static PreparedScene Collect(RenderSnapshot snapshot, float alpha = 1f)
    {
        var scene = new PreparedScene();
        new SceneBatcher().Prepare(snapshot: snapshot, alpha: alpha, width: 1600, height: 900, meshes: NoMeshes,
            materialCount: 1, scene: scene);
        OutlinePass.Collect(snapshot, scene);
        return scene;
    }

    private static GpuOutline[] Records(PreparedScene scene) => scene.Outlines.AsSpan(0, scene.OutlineCount).ToArray();

    [Fact]
    public void AnOutlineOutsideTheViewIsLeftOutAndOneRoundTheCameraStays()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Outlines.Add(new RestingOutline(new(0, 0, -20), 2)); // ahead
        snapshot.Outlines.Add(new RestingOutline(new(0, 0, 50), 2)); // behind
        snapshot.Outlines.Add(new FallingOutline(new(500, 0, -20), 10)); // far right
        snapshot.Outlines.Add(new SemanticOutline(new(0, -400, -20), Quaternion.Identity,
            new(30, 20, 15))); // far below
        // The camera is inside this bubble, though its center is behind the camera.
        snapshot.Outlines.Add(new SemanticOutline(new(0, 0, 10), Quaternion.Identity, new(30, 20, 15)));
        // The center is outside the right plane, but the sphere reaches into the view.
        snapshot.Outlines.Add(new FollowingOutline(new(9.5f, 0, -5), 4));

        GpuOutline[] records = Records(Collect(snapshot));

        records.Select(r => r.Center).Should()
            .Equal(new Vector3(0, 0, -20), new Vector3(0, 0, 10), new Vector3(9.5f, 0, -5));
    }

    [Fact]
    public void AnEllipsoidLeavesTheViewOnlyWhenItsLargestRadiusDoes()
    {
        // 30 m to the right of the right plane at 20 m ahead: the outline sphere must use the largest radius, 40 m, to reach in.
        RenderSnapshot snapshot = Empty();
        float tanHalfX = MathF.Tan(Gpu.Fov / 2) * 1600 / 900;
        float rightEdge = 20 * tanHalfX;
        snapshot.Outlines.Add(new SemanticOutline(new(rightEdge + 30, 0, -20), Quaternion.Identity, new(1, 1, 40)));

        Collect(snapshot).OutlineCount.Should().Be(1);
    }

    [Fact]
    public void EachKindOfFrameGetsItsOwnColor()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Outlines.Add(new SemanticOutline(new(0, 0, -20), Quaternion.Identity, new(1, 2, 3)));
        snapshot.Outlines.Add(new FallingOutline(new(0, 0, -20), 1));
        snapshot.Outlines.Add(new RestingOutline(new(0, 0, -20), 1));
        snapshot.Outlines.Add(new NestingOutline(new(0, 0, -20), 1));
        snapshot.Outlines.Add(new FollowingOutline(new(0, 0, -20), 1));

        uint[] colors = [.. Records(Collect(snapshot)).Select(r => r.Color)];

        colors.Should().Equal(OutlineColors.Semantic, OutlineColors.Falling, OutlineColors.Resting,
            OutlineColors.Nesting, OutlineColors.Following);
        colors.Should().OnlyHaveUniqueItems("a legend tells the kinds of frame apart by color alone");
        colors.Should()
            .AllSatisfy(c => (c >> 24).Should().Be(0xFFu, "the pass sets the alpha of each fragment itself"));
    }

    [Fact]
    public void ASphereGetsItsOutlineAsSeenFromTheCameraAndAnEllipsoidDoesNot()
    {
        RenderSnapshot snapshot = Empty();
        var turned = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new(1, 2, 0)), 0.7f);
        snapshot.Outlines.Add(new SemanticOutline(new(1, 2, -20), turned, new(4, 5, 6)));
        snapshot.Outlines.Add(new FallingOutline(new(-1, 0, -20), 3));
        snapshot.Outlines.Add(new RestingOutline(new(-1, 0, -20), 3));
        snapshot.Outlines.Add(new NestingOutline(new(-1, 0, -20), 3));
        snapshot.Outlines.Add(new FollowingOutline(new(-1, 0, -20), 3));

        GpuOutline[] records = Records(Collect(snapshot));

        GpuOutline ellipsoid = records[0];
        (ellipsoid.Flags & GpuOutline.SphereFlag).Should().Be(0u);
        ellipsoid.Rotation.Should().Be(new Vector4(x: turned.X, y: turned.Y, z: turned.Z, w: turned.W),
            "an ellipsoid's rings lie in the principal planes of its frame");
        ellipsoid.Radii.Should().Be(new Vector3(4, 5, 6));
        foreach (GpuOutline sphere in records[1..])
        {
            (sphere.Flags & GpuOutline.SphereFlag).Should().Be(GpuOutline.SphereFlag);
            sphere.Radii.Should().Be(new Vector3(3));
            // A numeric frame's outline has no axes of its own, so its rings lie in the axes of the camera's frame.
            sphere.Rotation.Should().Be(new Vector4(x: 0, y: 0, z: 0, w: 1));
        }
    }

    [Fact]
    public void ASphericalBubbleGetsItsOutlineAsSeenFromTheCameraAndKeepsTheAxesOfItsFrame()
    {
        // A bubble with three equal radii is a sphere (FrameBounds.IsSphere), like the bubble of Earth in the demo.
        RenderSnapshot snapshot = Empty();
        var turned = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new(1, 2, 0)), 0.7f);
        snapshot.Outlines.Add(new SemanticOutline(new(1, 2, -20), turned, new(5)));
        // Two equal radii do not make a sphere.
        snapshot.Outlines.Add(new SemanticOutline(new(1, 2, -20), turned, new(5, 5, 6)));

        GpuOutline[] records = Records(Collect(snapshot));

        GpuOutline sphere = records[0];
        (sphere.Flags & GpuOutline.SphereFlag).Should()
            .Be(GpuOutline.SphereFlag, "a sphere seen from outside also draws its rim");
        sphere.Rotation.Should().Be(new Vector4(x: turned.X, y: turned.Y, z: turned.Z, w: turned.W),
            "a bubble's rings lie in the principal planes of its frame");
        sphere.Radii.Should().Be(new Vector3(5));
        (records[1].Flags & GpuOutline.SphereFlag).Should().Be(0u);
    }

    [Fact]
    public void AnOutlineOfRadiusZeroDrawsNothing()
    {
        RenderSnapshot snapshot = Empty();
        Vector3 ahead = new(0, 0, -20);
        snapshot.Outlines.Add(new FallingOutline(ahead, 0));
        snapshot.Outlines.Add(new RestingOutline(ahead, 0));
        snapshot.Outlines.Add(new NestingOutline(ahead, -1));
        snapshot.Outlines.Add(new FollowingOutline(ahead, float.NaN));
        snapshot.Outlines.Add(new SemanticOutline(ahead, Quaternion.Identity, Vector3.Zero));
        snapshot.Outlines.Add(new SemanticOutline(ahead, Quaternion.Identity, new(float.NaN, 1, 1)));
        snapshot.Outlines.Add(new RestingOutline(new(float.PositiveInfinity, 0, -20), 2));
        snapshot.Outlines.Add(default);
        // One that draws, to show that the others were left out and not everything.
        snapshot.Outlines.Add(new SemanticOutline(ahead, Quaternion.Identity, new(0, 0, 2)));

        GpuOutline[] records = Records(Collect(snapshot));

        records.Should().ContainSingle().Which.Radii.Should().Be(new Vector3(0, 0, 2),
            "an ellipsoid flat on two axes still has a ring to draw");
    }

    [Fact]
    public void AnOutlineIsPlacedRelativeToTheInterpolatedEye()
    {
        // An outline is not interpolated, but the eye is: at alpha 0.25 the eye is at lerp(2, 0, 0.25) = 1.5 m up.
        RenderSnapshot snapshot = Empty();
        snapshot.Camera.PreviousPosition = new(0, 2, 0);
        snapshot.Outlines.Add(new FallingOutline(new(1, 0, -10), 2));
        snapshot.Outlines.Add(new SemanticOutline(new(-1, 0, -10), Quaternion.Identity, Vector3.One));

        GpuOutline[] records = Records(Collect(snapshot, alpha: 0.25f));

        records.Select(r => r.Center).Should().Equal(new Vector3(1, -1.5f, -10), new Vector3(-1, -1.5f, -10));
    }

    [Fact]
    public void ManyOutlinesGrowTheRecordArrayAndKeepTheOrderOfTheSnapshot()
    {
        // The array starts at 16 records. Outline i has the radius i + 1 and cycles through the five cases.
        RenderSnapshot snapshot = Empty();
        for (int i = 0; i < 40; i++)
        {
            Vector3 center = new(0, 0, -100);
            snapshot.Outlines.Add((i % 5) switch
            {
                0 => new SemanticOutline(center, Quaternion.Identity, new(i + 1)),
                1 => new FallingOutline(center, i + 1),
                2 => new RestingOutline(center, i + 1),
                3 => new NestingOutline(center, i + 1),
                _ => new FollowingOutline(center, i + 1)
            });
        }

        PreparedScene scene = Collect(snapshot);

        scene.OutlineCount.Should().Be(40);
        Records(scene).Select(r => r.Radii.X).Should().Equal(Enumerable.Range(1, 40).Select(i => (float)i));
    }

    [Fact]
    public void ASnapshotWithoutOutlinesLeavesNoRecordFromTheFrameBefore()
    {
        RenderSnapshot shown = Empty();
        shown.Outlines.Add(new RestingOutline(new(0, 0, -20), 2));
        var scene = new PreparedScene();
        var batcher = new SceneBatcher();
        batcher.Prepare(snapshot: shown, alpha: 1, width: 1600, height: 900, meshes: NoMeshes, materialCount: 1,
            scene: scene);
        OutlinePass.Collect(shown, scene);

        RenderSnapshot hidden = Empty();
        batcher.Prepare(snapshot: hidden, alpha: 1, width: 1600, height: 900, meshes: NoMeshes, materialCount: 1,
            scene: scene);
        OutlinePass.Collect(hidden, scene);

        scene.OutlineCount.Should().Be(0);
    }
}
