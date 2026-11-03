using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Posing in the batcher (docs/plans/skinning-and-morphs.md, "Posing in the batcher"): which posed instance draws a
/// deformed copy, where the copies lie in the deform region, and that every view draws and culls the copy. No GPU needed.
/// The camera sits at the render origin and looks down −Z.
/// </summary>
public sealed class BatcherDeformTests
{
    private const int Rigid = 0;
    private const int Cylinder = 1;
    private const int MorphBox = 2;
    private const int StaticMeshCount = 3;
    private const int Tip = 1;
    private const int RegionBase = 1_024;

    private static readonly MeshData CylinderData = Primitives.BendingCylinder(radius: 0.2f, length: 2f);
    private static readonly MeshData BoxData = Primitives.MorphingBox(new(0.5f));
    private static readonly MeshDeform CylinderDeform = MeshDeform.Pack(StagedMesh.Of(CylinderData))!.Deform;
    private static readonly MeshDeform BoxDeform = MeshDeform.Pack(StagedMesh.Of(BoxData))!.Deform;

    /// <summary>A rigid box, the bending cylinder and the morphing box, with the box's indices first.</summary>
    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: MathF.Sqrt(0.75f), Deform: null),
        new(
            FirstIndex: 36,
            IndexCount: (uint)CylinderData.Indices.Length,
            VertexOffset: 24,
            Radius: GeometryStore.Radius(CylinderData.Vertices),
            Deform: CylinderDeform
        ),
        new(
            FirstIndex: 36 + (uint)CylinderData.Indices.Length,
            IndexCount: (uint)BoxData.Indices.Length,
            VertexOffset: 24 + CylinderData.Vertices.Length,
            Radius: GeometryStore.Radius(BoxData.Vertices),
            Deform: BoxDeform
        ),
    ];

    private static readonly DeformRegion Roomy = new(
        Base: RegionBase,
        Capacity: 100_000,
        PoseRowCapacity: 100_000,
        JobCapacity: 1_000
    );

    private static RenderSnapshot Empty() => Gpu.Snapshot(Vector3.UnitY);

    private static RenderInstance Instance(int mesh, Vector3 position) =>
        Gpu.Instance(
            mesh: new(mesh),
            material: new(0),
            position: position,
            scale: Vector3.One,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>Appends <paramref name="instance"/> with <paramref name="pose"/> as its rig pose at both ends.</summary>
    private static void AddPosed(RenderSnapshot snapshot, RenderInstance instance, RigPose pose)
    {
        snapshot.Instances.Add(instance);
        snapshot.AddRigPose(
            instance: snapshot.Instances.Count - 1,
            joints: pose.Joints.AsSpan(),
            previousJoints: pose.Joints.AsSpan(),
            weights: pose.Weights.AsSpan(),
            previousWeights: pose.Weights.AsSpan()
        );
    }

    private static RigPose CylinderRest => CylinderData.Rigging!.Rig.Rest;

    private static RigPose BoxRest => BoxData.Rigging!.Rig.Rest;

    /// <summary>The cylinder's rest pose with Tip raised to <paramref name="height"/> m above Base.</summary>
    private static RigPose TipAt(float height) =>
        CylinderRest with
        {
            Joints = CylinderRest.Joints.SetItem(Tip, new(new(0, height, 0), Quaternion.Identity)),
        };

    private static PreparedScene Prepare(
        RenderSnapshot snapshot,
        DeformRegion region,
        EnvironmentDraw environment = default
    )
    {
        var scene = new PreparedScene();
        Batchers
            .Default()
            .Prepare(
                snapshot: snapshot,
                alpha: 1f,
                highestStops: Batchers.HighestStops,
                width: 1600,
                height: 900,
                meshes: Meshes,
                materials: MaterialSlots.Of(8),
                environment: environment,
                region: region,
                frame: Batchers.FirstFrame(),
                scene: scene
            );
        return scene;
    }

    /// <summary>The vertex offsets of the scene's commands, in command order.</summary>
    private static int[] VertexOffsets(PreparedScene scene) =>
        [.. scene.Commands.AsSpan(0, scene.CommandCount).ToArray().Select(command => command.VertexOffset)];

    [Fact]
    public void PosedInstanceDrawsItsDeformedCopy()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(0, 0, -10)), CylinderRest);

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.Deforms.ToArray().Should().Equal(new DeformJob(Mesh: Cylinder, Target: 0, Pose: 0));
        scene.Meshes.Length.Should().Be(StaticMeshCount + 1);
        scene
            .Meshes[StaticMeshCount]
            .Should()
            .BeEquivalentTo(
                new
                {
                    Meshes[Cylinder].FirstIndex,
                    Meshes[Cylinder].IndexCount,
                    VertexOffset = RegionBase,
                    Deform = (MeshDeform?)null,
                },
                "the copy draws the static mesh's indices over its own vertices"
            );
        scene.VisibleSpan[0].Mesh.Should().Be(StaticMeshCount);
        VertexOffsets(scene).Should().Equal(RegionBase);
    }

    [Fact]
    public void TwoPosedInstancesOfOneMeshGetTwoCopies()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(-1, 0, -10)), CylinderRest);
        AddPosed(snapshot, Instance(Cylinder, new(1, 0, -10)), CylinderRest);

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.Deforms.Length.Should().Be(2);
        VertexOffsets(scene)
            .Should()
            .Equal(
                [RegionBase, RegionBase + CylinderDeform.VertexCount],
                "each copy is a mesh of its own, so the two instances share no command"
            );
    }

    [Fact]
    public void CopiesLieEndToEndInTheRegion()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(-1, 0, -10)), CylinderRest);
        AddPosed(snapshot, Instance(MorphBox, new(1, 0, -10)), BoxRest);

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene
            .Deforms.ToArray()
            .Should()
            .Equal(
                new DeformJob(Mesh: Cylinder, Target: 0, Pose: 0),
                new DeformJob(Mesh: MorphBox, Target: CylinderDeform.VertexCount, Pose: CylinderDeform.PoseRows)
            );
        scene.Poses.Length.Should().Be(CylinderDeform.PoseRows + BoxDeform.PoseRows);
        scene.Meshes[StaticMeshCount + 1].VertexOffset.Should().Be(RegionBase + CylinderDeform.VertexCount);
    }

    [Fact]
    public void JobsFollowTheSnapshotOrder()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Instance(Cylinder, new(0, 0, -10)));
        AddPosed(snapshot, Instance(MorphBox, new(-1, 0, -10)), BoxRest);
        AddPosed(snapshot, Instance(Cylinder, new(1, 0, -10)), CylinderRest);

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.Deforms.ToArray().Select(job => job.Mesh).Should().Equal(MorphBox, Cylinder);
    }

    [Fact]
    public void InstanceWithoutARigPoseDrawsItsStaticMesh()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Instance(Cylinder, new(-1, 0, -10)));
        AddPosed(snapshot, Instance(MorphBox, new(1, 0, -10)), BoxRest);

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene
            .VisibleSpan.ToArray()
            .Select(instance => instance.Mesh)
            .Should()
            .Equal([Cylinder, StaticMeshCount], "only the box has a rig pose");
        scene.UndeformedPosedInstances.Should().Be(0, "an instance without a rig pose is not posed");
    }

    [Fact]
    public void MeshIdPastTheStaticMeshesNeverNamesADeformedCopy()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(-1, 0, -10)), CylinderRest);
        snapshot.Instances.Add(Instance(StaticMeshCount, new(1, 0, -10)));

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.VisibleCount.Should().Be(1, "mesh id 3 is not on the GPU, whatever the frame's copies");
    }

    [Fact]
    public void RigPoseThatDoesNotFitDrawsTheStaticMeshAndIsCounted()
    {
        RenderSnapshot snapshot = Empty();
        // The box's rig pose, of no joints and two weights, on the cylinder of two joints and no morphs.
        AddPosed(snapshot, Instance(Cylinder, new(0, 0, -10)), BoxRest);

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.VisibleSpan[0].Mesh.Should().Be(Cylinder);
        scene.UndeformedPosedInstances.Should().Be(1);
    }

    [Fact]
    public void RigPoseOnARigidMeshDrawsTheStaticMeshAndIsCounted()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Rigid, new(0, 0, -10)), CylinderRest);

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.VisibleSpan[0].Mesh.Should().Be(Rigid);
        scene.UndeformedPosedInstances.Should().Be(1);
    }

    [Fact]
    public void PosedInstancesPastTheRegionDrawTheirStaticMeshAndAreCounted()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(-1, 0, -10)), CylinderRest);
        AddPosed(snapshot, Instance(Cylinder, new(1, 0, -10)), CylinderRest);
        DeformRegion region = Roomy with { Capacity = CylinderDeform.VertexCount * 3 / 2 };

        PreparedScene scene = Prepare(snapshot, region);

        scene.VisibleSpan.ToArray().Select(instance => instance.Mesh).Should().Equal(StaticMeshCount, Cylinder);
        scene.UndeformedPosedInstances.Should().Be(1);
    }

    [Fact]
    public void PosedInstancesPastThePoseBufferDrawTheirStaticMeshAndAreCounted()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(-1, 0, -10)), CylinderRest);
        AddPosed(snapshot, Instance(Cylinder, new(1, 0, -10)), CylinderRest);
        DeformRegion region = Roomy with { PoseRowCapacity = CylinderDeform.PoseRows * 3 / 2 };

        PreparedScene scene = Prepare(snapshot, region);

        scene.VisibleSpan.ToArray().Select(instance => instance.Mesh).Should().Equal(StaticMeshCount, Cylinder);
        scene.UndeformedPosedInstances.Should().Be(1);
        scene.Poses.Length.Should().Be(CylinderDeform.PoseRows);
    }

    [Fact]
    public void PosedInstancesPastTheJobTableDrawTheirStaticMeshAndAreCounted()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(-1, 0, -10)), CylinderRest);
        AddPosed(snapshot, Instance(MorphBox, new(1, 0, -10)), BoxRest);
        DeformRegion region = Roomy with { JobCapacity = 1 };

        PreparedScene scene = Prepare(snapshot, region);

        scene.VisibleSpan.ToArray().Select(instance => instance.Mesh).Should().Equal(StaticMeshCount, MorphBox);
        scene.UndeformedPosedInstances.Should().Be(1);
    }

    [Fact]
    public void EmptyRegionDrawsEveryPosedInstanceAsUploaded()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(-1, 0, -10)), CylinderRest);
        AddPosed(snapshot, Instance(MorphBox, new(1, 0, -10)), BoxRest);

        PreparedScene scene = Prepare(snapshot, DeformRegion.None);

        scene.VisibleSpan.ToArray().Select(instance => instance.Mesh).Should().Equal(Cylinder, MorphBox);
        scene.UndeformedPosedInstances.Should().Be(2);
        scene.Deforms.Length.Should().Be(0);
    }

    [Fact]
    public void PoseWhoseBoundPassesThePositionLimitDrawsAsUploaded()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(0, 0, -10)), TipAt(2 * ShaderInput.PositionLimit));

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.VisibleSpan[0].Mesh.Should().Be(Cylinder);
        scene.UndeformedPosedInstances.Should().Be(1);
        scene.Poses.Length.Should().Be(0, "the dropped job leaves no pose rows");
    }

    /// <summary>A cylinder instance that no view draws: hidden and casting no shadow, or flat along one axis.</summary>
    private static RenderInstance Undrawn(string why) =>
        why switch
        {
            "hidden" => Instance(Cylinder, new(0, 0, -10)) with { Flags = RenderFlags.None },
            _ => Instance(Cylinder, new(0, 0, -10)) with { Scale = new(1, 0, 1) },
        };

    [Theory]
    [InlineData("hidden")]
    [InlineData("flat")]
    public void PosedInstanceThatNoViewDrawsTakesNoJobAndIsNotCounted(string why)
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Undrawn(why), CylinderRest);

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.Deforms.Length.Should().Be(0, "no draw would read its copy");
        scene.Poses.Length.Should().Be(0);
        scene.DeformedInstances.Should().Be(0);
        scene.UndeformedPosedInstances.Should().Be(0, "it draws in no view, so it does not draw as uploaded either");
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("flat")]
    public void DeformNeedLeavesOutPosedInstancesThatNoViewDraws(string why)
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Undrawn(why), CylinderRest);
        AddPosed(snapshot, Instance(MorphBox, new(1, 0, -10)), BoxRest);

        int need = SceneBatcher.DeformNeed(snapshot, Meshes);

        need.Should().Be(BoxDeform.VertexCount);
    }

    [Fact]
    public void ShadowOnlyPosedInstanceDeforms()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(0, 0, -10)) with { Flags = RenderFlags.CastsShadow }, CylinderRest);

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.DeformedInstances.Should().Be(1, "its shadow draws the copy");
    }

#if TERTIUS_CONTRACTS
    [Fact]
    public void StaticMeshesShorterThanTheTableBeforeAreABug()
    {
        var scene = new PreparedScene();
        scene.SetStaticMeshes(Meshes);
        scene.Reset();

        Action shorter = () => scene.SetStaticMeshes(Meshes.AsSpan(0, 1));

        shorter
            .Should()
            .Throw<ContractException>("the mesh table only appends, so a shorter one is another table")
            .WithMessage("Precondition*");
    }
#endif

    [Fact]
    public void PoseThatIsNotFiniteDrawsAsUploaded()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(0, 0, -10)), TipAt(float.NaN));

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.VisibleSpan[0].Mesh.Should().Be(Cylinder, "no NaN pose row reaches the deform pass");
        scene.UndeformedPosedInstances.Should().Be(1);
        scene.Poses.Length.Should().Be(0, "the dropped job leaves no pose rows");
    }

    [Fact]
    public void PosedInstanceIsCulledByItsPosedBound()
    {
        RenderSnapshot snapshot = Empty();
        // At 10 m the view reaches 5.8 m below the axis. The rest bound, about 2 m, ends 7 m below it, while the upper
        // half raised by 4 m reaches into the view.
        AddPosed(snapshot, Instance(Cylinder, new(0, -9, -10)), TipAt(5));

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.VisibleCount.Should().Be(1);
        scene.VisibleSpan[0].Mesh.Should().Be(StaticMeshCount);
    }

    [Fact]
    public void RenderRowBoundingRadiusOverridesThePosedBound()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(0, 0, -10)) with { BoundingRadius = 7 }, TipAt(5));

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.VisibleSpan[0].Should().BeEquivalentTo(new { Mesh = StaticMeshCount, Radius = 7f });
    }

    [Fact]
    public void ShadowCastersOfAPosedInstanceDrawItsCopy()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(
            snapshot,
            Instance(Cylinder, new(0, 0, -10)) with
            {
                Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
            },
            TipAt(5)
        );

        PreparedScene scene = Prepare(snapshot, Roomy);

        PreparedInstance caster = scene.PlacedSpan.ToArray().Single(placed => placed.CastsShadow);
        caster.Mesh.Should().Be(StaticMeshCount);
        caster.Radius.Should().Be(scene.Meshes[StaticMeshCount].Radius, "the caster is culled by its posed bound");
    }

    [Fact]
    public void AHiddenPosedCasterAndAVisiblePosedInstanceAfterItEachDrawTheirOwnCopy()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(-1, 0, -10)) with { Flags = RenderFlags.CastsShadow }, TipAt(5));
        AddPosed(snapshot, Instance(Cylinder, new(1, 0, -10)), CylinderRest);

        PreparedScene scene = Prepare(snapshot, Roomy);

        scene.PlacedSpan.ToArray().Single(placed => placed.CastsShadow).Mesh.Should().Be(StaticMeshCount);
        scene.VisibleSpan.ToArray().Should().ContainSingle().Which.Mesh.Should().Be(StaticMeshCount + 1);
    }

    [Fact]
    public void EnvironmentFacesDrawTheDeformedCopy()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(0, 0, -10)), CylinderRest);
        EnvironmentDraw map = new WithMap(Faces: new[] { FaceMask.NegativeZ }, Irradiance: new IrradianceReading?[1]);

        PreparedScene scene = Prepare(snapshot, Roomy, map);

        CommandRange near = scene.EnvironmentFaces.Single().Near.OneSided.Solid;
        near.Count.Should().Be(1);
        scene.Commands[near.First].VertexOffset.Should().Be(RegionBase);
    }

    /// <summary>
    /// The one interpolation of each instance gives every face the instance's frame mesh, also for an instance that the
    /// main view culls: a posed box behind the camera, after a rigid box and a posed cylinder ahead of it, draws its own
    /// deformed copy in the face that looks at it.
    /// </summary>
    [Fact]
    public void AFaceDrawsTheDeformedCopyOfAPosedInstanceThatTheMainViewCulls()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Instance(Rigid, new(0, 0, -10)));
        AddPosed(snapshot, Instance(Cylinder, new(0, 0, -10)), CylinderRest);
        AddPosed(snapshot, Instance(MorphBox, new(0, 0, 10)), BoxRest);
        EnvironmentDraw map = new WithMap(Faces: new[] { FaceMask.PositiveZ }, Irradiance: new IrradianceReading?[1]);

        PreparedScene scene = Prepare(snapshot, Roomy, map);

        scene.VisibleCount.Should().Be(2, "the main view culls the box behind the camera");
        CommandRange near = scene.EnvironmentFaces.Single().Near.OneSided.Solid;
        near.Count.Should().Be(1);
        scene
            .Commands[near.First]
            .VertexOffset.Should()
            .Be(RegionBase + CylinderDeform.VertexCount, "the box's copy follows the cylinder's in the region");
    }

    [Fact]
    public void DeformNeedCountsTheVerticesOfEveryPosedDeformingInstance()
    {
        RenderSnapshot snapshot = Empty();
        AddPosed(snapshot, Instance(Cylinder, new(-1, 0, -10)), CylinderRest);
        AddPosed(snapshot, Instance(Cylinder, new(0, 0, -10)), CylinderRest);
        AddPosed(snapshot, Instance(MorphBox, new(1, 0, -10)), BoxRest);
        AddPosed(snapshot, Instance(Rigid, new(2, 0, -10)), CylinderRest);
        snapshot.Instances.Add(Instance(Cylinder, new(3, 0, -10)));

        int need = SceneBatcher.DeformNeed(snapshot, Meshes);

        need.Should().Be(2 * CylinderDeform.VertexCount + BoxDeform.VertexCount);
    }

    [Fact]
    public void PrepareWithPosedInstancesAllocatesNothingInTheSteadyState()
    {
        RenderSnapshot snapshot = Empty();
        for (int i = 0; i < 20; i++)
        {
            AddPosed(
                snapshot,
                Instance(Cylinder, new(i - 10, 0, -10)) with
                {
                    Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
                },
                TipAt(1 + 0.1f * i)
            );
            AddPosed(snapshot, Instance(MorphBox, new(i - 10, 2, -10)), BoxRest);
        }

        var scene = new PreparedScene();
        SceneBatcher batcher = Batchers.Default();
        // Outside the frame, whose allocations the test counts.
        MaterialInfo[] materials = MaterialSlots.Of(8);
        var history = new FrameHistory();
        // Each frame continues the one before, so the join of the placement history runs as in a window's frames.
        void Frame()
        {
            PendingFrame frame = history.Begin(
                snapshot: snapshot,
                outputSize: new(1600, 900),
                renderSize: new(1600, 900)
            );
            batcher.Prepare(
                snapshot: snapshot,
                alpha: 0.5f,
                highestStops: Batchers.HighestStops,
                width: 1600,
                height: 900,
                meshes: Meshes,
                materials: materials,
                environment: default,
                region: Roomy,
                frame: frame,
                scene: scene
            );
            history.Drew(frame);
        }

        for (int i = 0; i < 3; i++)
            Frame();

        long before = GC.GetAllocatedBytesForCurrentThread();
        Frame();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        scene.DeformedInstances.Should().Be(40);
        allocated.Should().Be(0);
    }
}
