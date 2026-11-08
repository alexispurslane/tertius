using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>Automatic enclosures follow the existing map assignment and contain complete prepared geometry.</summary>
public sealed class EnvironmentBoxTests
{
    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 2, Deform: null),
    ];

    [Fact]
    public void AncestorsCapturedInAChildMapDoNotEnlargeTheChildsBox()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(Instance(new(100, 0, -10), depth: 2));
        snapshot.Instances.Add(Instance(new(20, 0, -10), depth: 1));
        snapshot.Instances.Add(Instance(new(0, 0, -10), depth: 0));

        PreparedScene scene = Prepare(snapshot, maps: 3);

        scene.Constants.MapBoxes[0].Minimum.Should().Be(new Vector4(new(98, -2, -12), 1));
        scene.Constants.MapBoxes[1].Minimum.Should().Be(new Vector4(new(18, -2, -12), 1));
        scene.Constants.MapBoxes[2].Minimum.Should().Be(new Vector4(new(-2, -2, -12), 1));
        scene.Constants.MapBoxes[2].Maximum.Should().Be(new Vector4(new(2, 2, -8), 0));
    }

    [Fact]
    public void OffScreenScaledRotatedGeometryUsesItsMeshExtentInsteadOfItsCullingRadius()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(30, 4, -8),
                scale: new(-3, 1, 2),
                chainDepth: 0,
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f),
                radius: 0.1f
            )
        );

        PreparedScene scene = Prepare(snapshot, maps: 1);

        scene.VisibleCount.Should().Be(0);
        scene.PlacedSpan[0].Radius.Should().Be(0.1f, "projection must not change visibility bounds");
        scene.Constants.MapBoxes[0].Minimum.Should().Be(new Vector4(new(24, -2, -14), 1));
        scene.Constants.MapBoxes[0].Maximum.Should().Be(new Vector4(new(36, 10, -2), 0));
    }

    [Fact]
    public void RigidFrameRotationPreservesTheLocalEnclosure()
    {
        Vector3 center = new(4, 2, -6);
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.8f);
        RenderSnapshot original = Gpu.Snapshot(Vector3.UnitY);
        original.Instances.Add(Instance(center, depth: 1));
        RenderSnapshot moved = Gpu.Snapshot(Vector3.UnitY);
        moved.Instances.Add(Instance(Vector3.Transform(center, turn), depth: 1));
        moved.Chain.Add(new(1, Quaternion.Identity, Quaternion.Identity));
        moved.Chain.Add(new(2, turn, turn));

        GpuMapBox before = Prepare(original, maps: 2).Constants.MapBoxes[0];
        GpuMapBox after = Prepare(moved, maps: 2).Constants.MapBoxes[0];

        before.Minimum.Should().Be(new Vector4(new(2, 0, -8), 1));
        Vector4.Distance(before.Minimum, after.Minimum).Should().BeLessThan(0.00001f);
        Vector4.Distance(before.Maximum, after.Maximum).Should().BeLessThan(0.00001f);
    }

    [Fact]
    public void MovingTheEyeWithItsGeometryPreservesTheLocalEnclosure()
    {
        Vector3 translation = new(10000, 20000, -30000);
        RenderSnapshot original = Gpu.Snapshot(Vector3.UnitY);
        original.Instances.Add(Instance(new(4, 2, -6), depth: 0));
        RenderSnapshot moved = Gpu.Snapshot(Vector3.UnitY);
        moved.Camera.Position = translation;
        moved.Camera.PreviousPosition = translation;
        moved.Instances.Add(Instance(new Vector3(4, 2, -6) + translation, depth: 0));

        GpuMapBox before = Prepare(original, maps: 1).Constants.MapBoxes[0];
        GpuMapBox after = Prepare(moved, maps: 1).Constants.MapBoxes[0];

        after.Should().BeEquivalentTo(before);
        after.Minimum.Should().Be(new Vector4(new(2, 0, -8), 1));
    }

    [Fact]
    public void LightsWithoutGeometryLeaveEveryBoxEmpty()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);

        PreparedScene scene = Prepare(snapshot, maps: 3);

        for (int map = 0; map < 3; map++)
            scene.Constants.MapBoxes[map].Should().BeEquivalentTo(default(GpuMapBox));
    }

    [Fact]
    public void TheServedLimitMergesOuterGeometryIntoOnlyTheOutermostBox()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(Instance(new(10, 0, -8), depth: 41));
        snapshot.Instances.Add(Instance(new(20, 0, -8), depth: 44));
        snapshot.Instances.Add(Instance(new(0, 0, -8), depth: 0));

        PreparedScene scene = Prepare(snapshot, maps: EnvironmentChain.MaxMaps);

        scene.Constants.MapBoxes[0].Minimum.Should().Be(new Vector4(new(8, -2, -10), 1));
        scene.Constants.MapBoxes[0].Maximum.Should().Be(new Vector4(new(22, 2, -6), 0));
        scene.Constants.MapBoxes[1].Minimum.W.Should().Be(0);
        scene.Constants.MapBoxes[41].Maximum.Should().Be(new Vector4(new(2, 2, -6), 0));
    }

    [Fact]
    public void RemovingOrReassigningGeometryRebuildsRatherThanRetainsTheBox()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(Instance(new(20, 0, -8), depth: 1));
        var scene = new PreparedScene();
        Prepare(snapshot, maps: 2, scene: scene);
        scene.Constants.MapBoxes[0].Minimum.W.Should().Be(1);

        snapshot.Instances.Clear();
        snapshot.Instances.Add(Instance(new(0, 0, -8), depth: 0));
        Prepare(snapshot, maps: 2, scene: scene);

        scene.Constants.MapBoxes[0].Minimum.W.Should().Be(0);
        scene.Constants.MapBoxes[1].Minimum.Should().Be(new Vector4(new(-2, -2, -10), 1));
    }

    private static RenderInstance Instance(Vector3 position, ushort depth) =>
        Gpu.Instance(mesh: new(0), material: new(0), position: position, scale: Vector3.One, chainDepth: depth);

    private static PreparedScene Prepare(RenderSnapshot snapshot, int maps, PreparedScene? scene = null)
    {
        scene ??= new();
        Batchers
            .Default()
            .Prepare(
                snapshot: snapshot,
                alpha: 1,
                highestStops: Batchers.HighestStops,
                width: 1600,
                height: 900,
                meshes: Meshes,
                materials: MaterialSlots.Of(1),
                environment: new WithMap(new FaceMask[maps], new IrradianceReading?[maps]),
                region: DeformRegion.None,
                frame: Batchers.FirstFrame(),
                scene: scene
            );
        return scene;
    }
}
