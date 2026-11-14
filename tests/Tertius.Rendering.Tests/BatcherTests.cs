using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>CPU-side batching: interpolation, culling, ranges, sorting and indirect commands. No GPU needed.</summary>
public sealed class BatcherTests
{
    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 0.866f, Deform: null),
        new(FirstIndex: 36, IndexCount: 600, VertexOffset: 24, Radius: 1f, Deform: null),
        new(FirstIndex: 636, IndexCount: 6, VertexOffset: 300, Radius: 1.414f, Deform: null),
    ];

    // Puffs. Each puff of the snapshot draws as one GpuPuff, at its interpolated place and render age, or not at all. The
    // camera sits at the render origin and looks down −Z, with a vertical field of view of 60° at 1600 × 900.

    /// <summary>A glow look: life 2 s, a radius that grows from 1 m to 3 m, and an orange radiance of (4, 2, 1).</summary>
    private static readonly PuffLook GlowLook = new(
        Life: 2f,
        StartRadius: 1f,
        EndRadius: 3f,
        TemperatureRatio: 3f,
        Shading: PuffShading.Glow(new(4, 2, 1))
    );

    private static RenderSnapshot Empty() => Gpu.Snapshot(Vector3.UnitY);

    private static PreparedScene Prepare(
        RenderSnapshot snapshot,
        float alpha = 1f,
        int materials = 8,
        SceneBatcher? batcher = null,
        EnvironmentDraw environment = default,
        float highestStops = Batchers.HighestStops
    )
    {
        var scene = new PreparedScene();
        (batcher ?? Batchers.Default()).Prepare(
            snapshot: snapshot,
            alpha: alpha,
            highestStops: highestStops,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materials: MaterialSlots.Of(materials),
            environment: environment,
            region: DeformRegion.None,
            frame: Batchers.FirstFrame(),
            scene: scene
        );
        return scene;
    }

    [Fact]
    public void InstancesOfOneMeshInOneRangeShareOneCommand()
    {
        RenderSnapshot snapshot = Empty();
        for (int i = 0; i < 100; i++)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: new(i % 2),
                    material: new(i % 3),
                    position: new(i % 10 - 5, 0, -10 - i),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.VisibleCount.Should().Be(100);
        scene.InstanceCount.Should().Be(100);
        scene.CommandCount.Should().Be(2);
        scene
            .Opaque[DepthRanges.Near]
            .Should()
            .Be(new SidedRuns(OneSided: new(Solid: new(0, 2), Cutout: default), TwoSided: default));
        DrawIndexedIndirectCommand first = scene.Commands[0],
            second = scene.Commands[1];
        first.InstanceCount.Should().Be(50u);
        second.InstanceCount.Should().Be(50u);
        first.FirstInstance.Should().Be(0u);
        second.FirstInstance.Should().Be(50u);
        second.FirstIndex.Should().Be(Meshes[1].FirstIndex);
        second.IndexCount.Should().Be(Meshes[1].IndexCount);
        second.VertexOffset.Should().Be(Meshes[1].VertexOffset);
    }

    [Fact]
    public void RangesAreOrderedFarToNearAndStraddlersAppearInBoth()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(0, 0, -10),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(0, 0, -50_000),
                scale: new(1_000),
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(1),
                material: new(0),
                position: new(0, 0, -3e7f),
                scale: new(6e6f),
                chainDepth: TestChains.CamerasFrame
            )
        );
        // Radius 50 m around 2 km: drawn in the near and middle ranges.
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(1),
                material: new(0),
                position: new(0, 0, -2_000),
                scale: new(50),
                chainDepth: TestChains.CamerasFrame
            )
        );
        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.VisibleCount.Should().Be(4);
        scene.InstanceCount.Should().Be(5, "the straddler is written once per range");
        scene.Opaque[DepthRanges.Far].Count.Should().Be(1);
        scene.Opaque[DepthRanges.Middle].Count.Should().Be(2);
        scene.Opaque[DepthRanges.Near].Count.Should().Be(2);
        scene
            .Opaque[DepthRanges.Far]
            .OneSided.Solid.First.Should()
            .BeLessThan(scene.Opaque[DepthRanges.Middle].OneSided.Solid.First);
        scene
            .Opaque[DepthRanges.Middle]
            .OneSided.Solid.First.Should()
            .BeLessThan(scene.Opaque[DepthRanges.Near].OneSided.Solid.First);
        // The planet's instance is the first one written, in the far range.
        scene
            .Instances[scene.Commands[scene.Opaque[DepthRanges.Far].OneSided.Solid.First].FirstInstance]
            .ModelRow2.W.Should()
            .Be(-3e7f);
    }

    [Fact]
    public void GlassInstancesAreSortedBackToFrontAcrossRanges()
    {
        RenderSnapshot snapshot = Empty();
        float[] depths = [5, 3_000, 20, 1e7f, 12, 700];
        foreach (float depth in depths)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: new(0),
                    material: MaterialSlots.Glass,
                    position: new(0, 0, -depth),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.InstanceCount.Should().Be(6);
        scene.Transparent[DepthRanges.Far].Should().Equal(GlassRun(0, 1));
        scene.Transparent[DepthRanges.Middle].Should().Equal(GlassRun(1, 1));
        scene.Transparent[DepthRanges.Near].Should().Equal(GlassRun(2, 1));
        scene.Commands[2].InstanceCount.Should().Be(4u);
        float[] drawn = [.. scene.Instances.AsSpan(0, scene.InstanceCount).ToArray().Select(i => -i.ModelRow2.W)];
        drawn.Should().Equal(1e7f, 3_000f, 700f, 20f, 12f, 5f);
        scene.Opaque.Should().AllSatisfy(range => range.Count.Should().Be(0));
    }

    [Fact]
    public void AGlassMaterialDrawsInTheTransparentPassOnly()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: MaterialSlots.Glass,
                position: new(0, 0, -10),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Transparent[DepthRanges.Near].Should().Equal(GlassRun(0, 1));
        scene.Opaque.Should().AllSatisfy(range => range.Count.Should().Be(0), "glass draws in no opaque pass");
    }

    [Fact]
    public void AnOpaqueMaterialWithTheGlassBitOfOldIsOpaque()
    {
        // 1 << 2 was RenderFlags.Transparent, which the material's alpha mode replaced: no flag makes an instance glass.
        const RenderFlags glassBitOfOld = (RenderFlags)(1 << 2);
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(0, 0, -10),
                scale: Vector3.One,
                flags: glassBitOfOld,
                chainDepth: TestChains.CamerasFrame
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene
            .Opaque[DepthRanges.Near]
            .Should()
            .Be(new SidedRuns(OneSided: new(Solid: new(0, 1), Cutout: default), TwoSided: default));
        scene.Transparent.Should().AllSatisfy(runs => runs.Should().BeEmpty());
    }

    /// <summary>A unit box of <paramref name="mesh"/> and <paramref name="material"/> at <paramref name="position"/>.</summary>
    private static RenderInstance BoxOf(MaterialId material, Vector3 position, int mesh = 0) =>
        Gpu.Instance(
            mesh: new(mesh),
            material: material,
            position: position,
            scale: Vector3.One,
            chainDepth: TestChains.CamerasFrame
        );

    [Fact]
    public void ACutoutMaterialDrawsAfterTheSolidOnesInItsRange()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(BoxOf(MaterialSlots.Cutout, new(0, 0, -10)));
        snapshot.Instances.Add(BoxOf(new(0), new(0, 0, -12), mesh: 1));
        snapshot.Instances.Add(BoxOf(MaterialSlots.Cutout, new(0, 0, -14), mesh: 1));
        snapshot.Instances.Add(BoxOf(new(0), new(0, 0, -16)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        SidedRuns near = scene.Opaque[DepthRanges.Near];
        near.Should()
            .Be(
                new SidedRuns(OneSided: new(Solid: new(0, 2), Cutout: new(2, 2)), TwoSided: default),
                "each run has a command per mesh"
            );
        Placed(scene, near.OneSided.Solid).Should().Equal(new Vector3(0, 0, -16), new(0, 0, -12));
        Placed(scene, near.OneSided.Cutout).Should().Equal(new Vector3(0, 0, -10), new(0, 0, -14));
    }

    [Fact]
    public void AFringedMaterialDrawsItsCoreWithTheCutoutsAndItsFringeWithTheTransparentObjects()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(BoxOf(MaterialSlots.Fringed, new(0, 0, -10)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        SidedRuns near = scene.Opaque[DepthRanges.Near];
        near.Should().Be(new SidedRuns(OneSided: new(Solid: default, Cutout: new(0, 1)), TwoSided: default));
        scene
            .Transparent[DepthRanges.Near]
            .Should()
            .Equal(new TransparentRun(new FringeCommands(Commands: new(1, 1), TwoSided: false)));
        Placed(scene, near.OneSided.Cutout).Should().Equal(new Vector3(0, 0, -10));
        Placed(scene, new CommandRange(1, 1)).Should().Equal(new Vector3(0, 0, -10));
    }

    [Fact]
    public void AHairMaterialDrawsInTheTwoSidedRunAfterTheOneSidedRunOfItsAlpha()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(BoxOf(MaterialSlots.Hair, new(0, 0, -10)));
        snapshot.Instances.Add(BoxOf(new(0), new(0, 0, -12)));
        snapshot.Instances.Add(BoxOf(MaterialSlots.FringedHair, new(0, 0, -14)));
        snapshot.Instances.Add(BoxOf(MaterialSlots.Cutout, new(0, 0, -16)));

        PreparedScene scene = Prepare(snapshot: snapshot, materials: 10);

        SidedRuns near = scene.Opaque[DepthRanges.Near];
        near.Should()
            .Be(
                new SidedRuns(
                    OneSided: new(Solid: new(0, 1), Cutout: new(2, 1)),
                    TwoSided: new(Solid: new(1, 1), Cutout: new(3, 1))
                ),
                "both solid runs draw before both cutout runs, for the early depth writes"
            );
        Placed(scene, near.TwoSided.Solid).Should().Equal(new Vector3(0, 0, -10));
        Placed(scene, near.TwoSided.Cutout).Should().Equal(new Vector3(0, 0, -14));
    }

    [Fact]
    public void AFringedHairMaterialsFringeDrawsInATwoSidedRun()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(BoxOf(MaterialSlots.FringedHair, new(0, 0, -10)));
        snapshot.Instances.Add(BoxOf(MaterialSlots.Fringed, new(0, 0, -15)));

        PreparedScene scene = Prepare(snapshot: snapshot, materials: 10);

        // Commands 0 and 1 draw the cores; the fringes follow, back to front, one run per side.
        scene
            .Transparent[DepthRanges.Near]
            .Should()
            .Equal(
                new FringeCommands(Commands: new(2, 1), TwoSided: false),
                new FringeCommands(Commands: new(3, 1), TwoSided: true)
            );
        Placed(scene, new CommandRange(3, 1)).Should().Equal(new Vector3(0, 0, -10));
    }

    [Fact]
    public void AGlassHairMaterialDrawsInATwoSidedGlassRun()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(BoxOf(MaterialSlots.GlassHair, new(0, 0, -10)));
        snapshot.Instances.Add(BoxOf(MaterialSlots.Glass, new(0, 0, -15)));

        PreparedScene scene = Prepare(snapshot: snapshot, materials: 11);

        // Glass has no core, so the glass commands start at 0, back to front, one run per side.
        scene
            .Transparent[DepthRanges.Near]
            .Should()
            .Equal(GlassRun(0, 1), new GlassCommands(Commands: new(1, 1), TwoSided: true));
        Placed(scene, new CommandRange(1, 1)).Should().Equal(new Vector3(0, 0, -10));
    }

    [Fact]
    public void AFaceDrawsAHairMaterialInItsTwoSidedRun()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(BoxOf(MaterialSlots.Hair, new(10, 0, 0)));
        snapshot.Instances.Add(BoxOf(new(0), new(10, 2, 0)));

        PreparedScene scene = Prepare(snapshot: snapshot, materials: 10, environment: MapOf(FaceMask.PositiveX));

        SidedRuns near = scene.EnvironmentFaces.Should().ContainSingle().Subject[DepthRanges.Near];
        Placed(scene, near.OneSided.Solid).Should().Equal(new Vector3(10, 2, 0));
        Placed(scene, near.TwoSided.Solid).Should().Equal(new Vector3(10, 0, 0));
    }

    [Fact]
    public void AFringeSortsWithTheGlassAndThePuffsByItsCenter()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Instances.Add(BoxOf(MaterialSlots.Glass, new(0, 0, -5)));
        snapshot.Instances.Add(BoxOf(MaterialSlots.Fringed, new(0, 0, -15)));
        snapshot.Instances.Add(BoxOf(MaterialSlots.Glass, new(0, 0, -20)));
        snapshot.Instances.Add(BoxOf(MaterialSlots.Fringed, new(0, 0, -17), mesh: 1));
        snapshot.Puffs.Add(Puff(new(0, 0, -12)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        // Command 0 and 1 draw the cores; the transparent commands follow, back to front.
        scene
            .Transparent[DepthRanges.Near]
            .Should()
            .Equal(
                GlassRun(2, 1),
                new FringeCommands(Commands: new(3, 2), TwoSided: false),
                PuffRun(0, 1),
                GlassRun(5, 1)
            );
        Placed(scene, new CommandRange(3, 2)).Should().Equal(new Vector3(0, 0, -17), new(0, 0, -15));
    }

    [Fact]
    public void AFaceDrawsTheCoreOfAFringedMaterial()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(BoxOf(MaterialSlots.Fringed, new(10, 0, 0)));
        snapshot.Instances.Add(BoxOf(new(0), new(10, 2, 0)));
        snapshot.Instances.Add(BoxOf(MaterialSlots.Glass, new(10, -2, 0)));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.PositiveX));

        SidedRuns near = scene.EnvironmentFaces.Should().ContainSingle().Subject[DepthRanges.Near];
        Placed(scene, near.OneSided.Solid).Should().Equal(new Vector3(10, 2, 0));
        Placed(scene, near.OneSided.Cutout).Should().Equal([new Vector3(10, 0, 0)], "a face has no transparent pass");
    }

    [Fact]
    public void CullingDropsInvisibleBehindAndOffscreenInstances()
    {
        RenderSnapshot snapshot = Empty();
        var box = new MeshId(0);
        var material = new MaterialId(0);
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: material,
                position: new(0, 0, -5),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: material,
                position: new(0, 0, 5),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        ); // behind
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: material,
                position: new(100, 0, -5),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        ); // far right
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: material,
                position: new(0, -100, -5),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        ); // far below
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(7),
                material: material,
                position: new(0, 0, -5),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        ); // unknown mesh
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: MeshId.None,
                material: material,
                position: new(0, 0, -5),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        ); // no mesh
        snapshot.Instances.Add(
            RenderInstance.Create(
                mesh: box,
                material: material,
                position: new(0, 0, -5),
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                boundingRadius: 0f,
                hemisphere: HemisphereUp.Even,
                chainDepth: TestChains.CamerasFrame,
                flags: RenderFlags.CastsShadow
            )
        ); // not Visible
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: material,
                position: new(0, 0, -5),
                scale: new(1, 0, 1),
                chainDepth: TestChains.CamerasFrame
            )
        ); // flat
        snapshot.Instances.Add(default); // zeroed
        // Partly in view: the center is outside the right plane but the bounding sphere reaches in.
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: material,
                position: new(9.5f, 0, -5),
                scale: Vector3.One,
                radius: 4f,
                chainDepth: TestChains.CamerasFrame
            )
        );
        PreparedScene scene = Prepare(snapshot: snapshot);
        scene.SubmittedInstances.Should().Be(10);
        scene.VisibleCount.Should().Be(2);
    }

    [Fact]
    public void InterpolationUsesAlphaForInstancesAndTheCamera()
    {
        RenderSnapshot snapshot = Empty();
        RenderInstance instance = Gpu.Instance(
            mesh: new(0),
            material: new(0),
            position: new(4, 0, -10),
            scale: Vector3.One,
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2),
            chainDepth: TestChains.CamerasFrame
        );
        instance.PreviousPosition = new(-4, 0, -10);
        instance.PreviousRotation = Quaternion.Identity;
        snapshot.Instances.Add(instance);
        snapshot.Camera.PreviousPosition = new(0, 2, 0);

        PreparedScene quarter = Prepare(snapshot: snapshot, alpha: 0.25f);
        PreparedInstance prepared = quarter.Visible[0];
        // Instance at lerp(-4, 4, 0.25) = -2; the camera at lerp(2, 0, 0.25) = 1.5 is subtracted.
        prepared.Position.Should().Be(new Vector3(-2, -1.5f, -10));
        quarter.Camera.Offset.Should().Be(new Vector3(0, 1.5f, 0));
        float angle = 2 * MathF.Acos(Math.Clamp(prepared.Rotation.W, -1, 1));
        angle.Should().BeApproximately(MathF.PI / 8, 1e-4f);

        // Alpha outside [0, 1] is clamped.
        Prepare(snapshot: snapshot, alpha: 3f).Visible[0].Position.Should().Be(new Vector3(4, 0, -10));
    }

    [Fact]
    public void UnsetQuaternionsCountAsIdentityAndUnknownMaterialsAsDefault()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Camera = new() { VerticalFov = 0 };
        RenderInstance instance = Gpu.Instance(
            mesh: new(0),
            material: new(42),
            position: new(0, 0, -5),
            scale: Vector3.One,
            chainDepth: TestChains.CamerasFrame
        );
        instance.Rotation = default;
        instance.PreviousRotation = default;
        snapshot.Instances.Add(instance);
        PreparedScene scene = Prepare(snapshot: snapshot, materials: 4);
        scene.Visible[0].Rotation.Should().Be(Quaternion.Identity);
        scene.Visible[0].Material.Should().Be(MaterialStore.DefaultSlot);
        scene.Camera.VerticalFov.Should().BeApproximately(MathF.PI / 3, 1e-5f);
    }

    [Fact]
    public void NoneMeansNoMeshOrTheDefaultMaterialAndRealIdsStartAtZero()
    {
        RenderSnapshot snapshot = Empty();
        // Four slots on the GPU: the default and materials 0 to 2.
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: MaterialId.None,
                position: new(-2, 0, -5),
                scale: Vector3.One,
                entity: 1,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(0, 0, -5),
                scale: Vector3.One,
                entity: 2,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(2),
                position: new(2, 0, -5),
                scale: Vector3.One,
                entity: 3,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(3),
                position: new(0, 2, -5),
                scale: Vector3.One,
                entity: 4,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(-7),
                position: new(0, -2, -5),
                scale: Vector3.One,
                entity: 5,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: MeshId.None,
                material: new(0),
                position: new(0, 0, -6),
                scale: Vector3.One,
                entity: 6,
                chainDepth: TestChains.CamerasFrame
            )
        );
        PreparedScene scene = Prepare(snapshot: snapshot, materials: MaterialStore.BuiltInCount + 3);

        Dictionary<uint, uint> materials = scene
            .VisibleSpan.ToArray()
            .ToDictionary(v => v.Entity.Index, v => v.Material);
        materials.Should().NotContainKey(6u, "MeshId.None draws nothing");
        materials[1].Should().Be(MaterialStore.DefaultSlot, "MaterialId.None asks for the default material");
        // Material 0 is a real material, not the default: ids are not confused with None.
        materials[2].Should().Be(MaterialStore.Slot(new(0))).And.NotBe(MaterialStore.DefaultSlot);
        materials[3].Should().Be(3u);
        materials[4].Should().Be(MaterialStore.DefaultSlot, "material 3 is not on the GPU");
        materials[5].Should().Be(MaterialStore.DefaultSlot, "no material has a negative id");

        MeshId.None.IsNone.Should().BeTrue();
        default(MeshId).Should().Be(MeshId.None);
        SceneBatcher.MeshIndex(MeshId.None, Meshes.Length).Should().Be(-1);
        SceneBatcher.MeshIndex(new(0), Meshes.Length).Should().Be(0);
        SceneBatcher.MeshIndex(new(Meshes.Length), Meshes.Length).Should().Be(-1);
    }

    [Fact]
    public void ThePartOfAnEntityReachesThePreparedInstance()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(-1, 0, -5),
                scale: Vector3.One,
                entity: 9,
                part: 0,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(0),
                material: new(0),
                position: new(1, 0, -5),
                scale: Vector3.One,
                entity: 9,
                part: 3,
                chainDepth: TestChains.CamerasFrame
            )
        );
        PreparedScene scene = Prepare(snapshot: snapshot);
        scene.VisibleSpan.ToArray().Select(v => (v.Entity.Index, v.Part)).Should().Equal((9u, 0), (9u, 3));
    }

    [Fact]
    public void ProcessorsCanReplaceInstancesBeforeBatching()
    {
        RenderSnapshot snapshot = Empty();
        for (int i = 0; i < 10; i++)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: new(0),
                    material: new(0),
                    position: new(0, 0, -10 - i * 100),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        SceneBatcher batcher = Batchers.Default();
        batcher.Processors.Add(new BillboardSwap());
        PreparedScene scene = Prepare(snapshot: snapshot, batcher: batcher);
        // Objects beyond 300 m became mesh 2 "billboards".
        scene.VisibleCount.Should().Be(10);
        scene.CommandCount.Should().Be(2);
        scene.Commands[0].InstanceCount.Should().Be(3u);
        scene.Commands[1].InstanceCount.Should().Be(7u);
        scene.Commands[1].FirstIndex.Should().Be(Meshes[2].FirstIndex);
    }

    private sealed class BillboardSwap : IInstanceProcessor
    {
        public void Process(PreparedScene scene)
        {
            for (int i = scene.VisibleCount - 1; i >= 0; i--)
            {
                if (scene.Visible[i].Distance <= 300)
                    continue;
                PreparedInstance original = scene.Visible[i];
                scene.RemoveVisibleAt(i);
                ref PreparedInstance billboard = ref scene.AddVisible();
                billboard = original;
                billboard.Mesh = 2;
            }
        }
    }

    [Fact]
    public void GpuInstanceMatchesSystemNumericsTransforms()
    {
        var rotation = Quaternion.Normalize(new(x: 0.3f, y: -0.5f, z: 0.2f, w: 0.8f));
        var scale = new Vector3(2, 3, 0.5f);
        var position = new Vector3(10, -20, 30);
        var instance = GpuInstance.From(
            position: position,
            rotation: rotation,
            scale: scale,
            material: 7,
            map: 0,
            holdsLight: false,
            placedIndex: 0
        );
        var point = new Vector3(0.4f, -1.1f, 2.5f);
        var p = new Vector4(point, 1);
        var transformed = new Vector3(
            Vector4.Dot(instance.ModelRow0, p),
            Vector4.Dot(instance.ModelRow1, p),
            Vector4.Dot(instance.ModelRow2, p)
        );
        Vector3 expected = position + Vector3.Transform(point * scale, rotation);
        Vector3.Distance(expected, transformed).Should().BeLessThan(1e-4f, "the model matrix is T · R · S");

        // The shader's normal transform R · S⁻¹ · n from M · (n / s²).
        var normal = Vector3.Normalize(new(1, 2, 3));
        var linear = new Matrix4x4(
            m11: instance.ModelRow0.X,
            m12: instance.ModelRow1.X,
            m13: instance.ModelRow2.X,
            m14: 0,
            m21: instance.ModelRow0.Y,
            m22: instance.ModelRow1.Y,
            m23: instance.ModelRow2.Y,
            m24: 0,
            m31: instance.ModelRow0.Z,
            m32: instance.ModelRow1.Z,
            m33: instance.ModelRow2.Z,
            m34: 0,
            m41: 0,
            m42: 0,
            m43: 0,
            m44: 1
        );
        var shaderNormal = Vector3.Normalize(Vector3.TransformNormal(normal * instance.InverseScaleSquared, linear));
        var expectedNormal = Vector3.Normalize(Vector3.Transform(normal / scale, rotation));
        Vector3.Distance(expectedNormal, shaderNormal).Should().BeLessThan(1e-5f, "normals transform by R · S⁻¹");
        instance.Material.Should().Be(7u);
    }

    [Fact]
    public void EachInstanceTakesItsHemispheresUpToTheGpuRelativeToTheInterpolatedEye()
    {
        RenderSnapshot snapshot = Empty();
        // At alpha 0.5 the eye is 1 m along +X from the render origin, so every position moves 1 m along -X.
        snapshot.Camera.PreviousPosition = new(2, 0, 0);
        snapshot.Instances.Add(
            RenderInstance.Create(
                mesh: new(0),
                material: new(1),
                position: new(0, 0, -10),
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                boundingRadius: 1,
                hemisphere: HemisphereUp.Along(Vector3.UnitX),
                chainDepth: TestChains.CamerasFrame
            )
        );
        var moving = RenderInstance.Create(
            mesh: new(0),
            material: new(1),
            position: new(0, 1, -10),
            rotation: Quaternion.Identity,
            scale: Vector3.One,
            boundingRadius: 1,
            hemisphere: HemisphereUp.AwayFrom(new(0, -50, 0)),
            chainDepth: TestChains.CamerasFrame
        );
        moving.PreviousPosition = new(4, 1, -10);
        snapshot.Instances.Add(moving);
        snapshot.Instances.Add(
            RenderInstance.Create(
                mesh: new(0),
                material: new(1),
                position: new(0, 2, -10),
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                boundingRadius: 1,
                hemisphere: HemisphereUp.Even,
                chainDepth: TestChains.CamerasFrame
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.5f);

        // The kind is in w: 1 along a direction, 2 away from a point, 0 even. The point moves with its instance: halfway
        // from x = 4 to x = 0 is x = 2, less the eye's 1 m, so 50 m below (1, 1, -10).
        scene
            .Instances.AsSpan(0, 3)
            .ToArray()
            .Select(instance => instance.Hemisphere)
            .Should()
            .BeEquivalentTo([new(x: 1, y: 0, z: 0, w: 1), new(x: 1, y: -49, z: -10, w: 2), Vector4.Zero]);
    }

    [Fact]
    public void TheRootRotationBlendsFromThePreviousTickAsTheCameraDoes()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Chain.Add(new(FrameId: 1, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        snapshot.Chain.Add(
            new(
                FrameId: 0,
                Rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2),
                PreviousRotation: Quaternion.Identity
            )
        );

        Vector4 root = Prepare(snapshot: snapshot, alpha: 0.25f).Constants.RootRotation;

        // A quarter of the way from the previous rotation to the current one: an eighth of a turn about +Y.
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 8);
        Vector4
            .Distance(root, new(x: expected.X, y: expected.Y, z: expected.Z, w: expected.W))
            .Should()
            .BeLessThan(1e-6f);
    }

    [Fact]
    public void AnUnsetRootRotationLeavesTheSkyInTheAxesOfTheCamerasFrame()
    {
        RenderSnapshot snapshot = Empty();
        // The camera's frame is the root, and its rotations are left zeroed.
        snapshot.Chain.Add(new(FrameId: 0, Rotation: default, PreviousRotation: default));

        Prepare(snapshot: snapshot).Constants.RootRotation.Should().Be(new Vector4(x: 0, y: 0, z: 0, w: 1));
    }

    // The map's faces. The camera sits at the render origin and looks down −Z, so it sees nothing along ±X or ±Y.

    /// <summary>
    /// The map is on, with one served map for each of <paramref name="faces"/>, root first, which draws those faces this
    /// frame, and has no irradiance reading yet.
    /// </summary>
    private static EnvironmentDraw MapOf(params FaceMask[] faces) =>
        new WithMap(Faces: faces, Irradiance: new IrradianceReading?[faces.Length]);

    /// <summary>A unit box of mesh 0 at <paramref name="position"/>.</summary>
    private static RenderInstance Box(Vector3 position, ushort chainDepth, RenderFlags flags = RenderFlags.None) =>
        Gpu.Instance(
            mesh: new(0),
            material: new(0),
            position: position,
            scale: Vector3.One,
            flags: flags,
            chainDepth: chainDepth
        );

    /// <summary>A box of glass.</summary>
    private static RenderInstance Pane(Vector3 position, ushort chainDepth) =>
        Gpu.Instance(
            mesh: new(0),
            material: MaterialSlots.Glass,
            position: position,
            scale: Vector3.One,
            chainDepth: chainDepth
        );

    private static Vector3 Position(in GpuInstance instance) =>
        new(instance.ModelRow0.W, instance.ModelRow1.W, instance.ModelRow2.W);

    /// <summary>The places of the GPU instances that the commands of <paramref name="commands"/> draw, in draw order.</summary>
    private static List<Vector3> Placed(PreparedScene scene, CommandRange commands)
    {
        var positions = new List<Vector3>();
        for (int c = commands.First; c < commands.First + commands.Count; c++)
        {
            DrawIndexedIndirectCommand command = scene.Commands[c];
            for (uint i = command.FirstInstance; i < command.FirstInstance + command.InstanceCount; i++)
                positions.Add(Position(scene.Instances[i]));
        }

        return positions;
    }

    /// <summary>The places of the GPU instances that the runs of <paramref name="runs"/> draw, in their order: the solid runs, then the cutout runs.</summary>
    private static List<Vector3> Placed(PreparedScene scene, SidedRuns runs) =>
        [
            .. Placed(scene, runs.OneSided.Solid),
            .. Placed(scene, runs.TwoSided.Solid),
            .. Placed(scene, runs.OneSided.Cutout),
            .. Placed(scene, runs.TwoSided.Cutout),
        ];

    /// <summary>The places of the GPU instances that a face draws over its three depth ranges.</summary>
    private static List<Vector3> Placed(PreparedScene scene, EnvironmentFaceDraw face) =>
        [.. Placed(scene, face.Far), .. Placed(scene, face.Middle), .. Placed(scene, face.Near)];

    /// <summary>A snapshot with no light: the test star of <see cref="Gpu.Snapshot"/> has no power, so it does not shine.</summary>
    private static RenderSnapshot Dark() => Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);

    /// <summary>
    /// The highest exposure of a frame of −10 stops, where the cutoff irradiance is π · 2^−13 · 2^10 = π / 8 W/m², so that
    /// a lamp's reach is a few meters.
    /// </summary>
    private const float TenStopsDown = -10;

    /// <summary>A white lamp at <paramref name="position"/> whose reach is 10 m at −10 stops, or no light at all when <paramref name="shines"/> is false.</summary>
    private static PointRenderLight Lamp(Vector3 position, bool shines = true) =>
        Gpu.Lamp(
            position: position,
            color: Vector3.One,
            intensity: shines ? (float)(100 * LightUnits.CutoffIrradiance(TenStopsDown)) : 0,
            chainDepth: TestChains.CamerasFrame
        );

    [Fact]
    public void AFaceDrawsTheOpaqueInstancesInItsFrustumAndNoGlassOne()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Box(new(10, 0, 0), chainDepth: TestChains.CamerasFrame));
        snapshot.Instances.Add(Pane(new(10, 3, 0), chainDepth: TestChains.CamerasFrame));
        snapshot.Instances.Add(Box(new(-10, 0, 0), chainDepth: TestChains.CamerasFrame)); // behind the face
        snapshot.Instances.Add(Box(new(10, 30, 0), chainDepth: TestChains.CamerasFrame)); // above its top plane
        snapshot.Instances.Add(Box(new(10, 0, 30), chainDepth: TestChains.CamerasFrame)); // beyond its side plane
        snapshot.Instances.Add(
            RenderInstance.Create(
                mesh: new(0),
                material: new(0),
                position: new(10, -3, 0),
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                boundingRadius: 0f,
                hemisphere: HemisphereUp.Even,
                chainDepth: TestChains.CamerasFrame,
                flags: RenderFlags.CastsShadow
            )
        ); // not Visible

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.PositiveX));

        EnvironmentFaceDraw face = scene.EnvironmentFaces.Should().ContainSingle().Subject;
        face.Face.Should().Be(CubeFace.PositiveX);
        Placed(scene, face).Should().Equal(new Vector3(10, 0, 0));
    }

    [Fact]
    public void AFaceDrawsEachInstanceAtItsInterpolatedPlaceRelativeToTheInterpolatedEye()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Camera.PreviousPosition = new(0, 0, 0);
        snapshot.Camera.Position = new(0, 2, 0);
        RenderInstance box = Box(new(12, 0, 0), chainDepth: TestChains.CamerasFrame);
        box.PreviousPosition = new(10, 0, 0);
        snapshot.Instances.Add(box);

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.5f, environment: MapOf(FaceMask.PositiveX));

        Placed(scene, scene.EnvironmentFaces[0]).Should().Equal(new Vector3(11, -1, 0));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(MathF.PI / 2)]
    [InlineData(MathF.PI)]
    [InlineData(-MathF.PI / 2)]
    public void AFaceSortsItsInstancesIntoTheRangesOfTheirDepthAlongItsOwnAxis(float cameraTurn)
    {
        // The camera turns about +Y: it looks down −Z, −X, +Z and +X in turn.
        RenderSnapshot snapshot = Empty();
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, cameraTurn);
        snapshot.Camera.Rotation = turn;
        snapshot.Camera.PreviousRotation = turn;
        snapshot.Instances.Add(Box(new(10_000, 0, 0), chainDepth: TestChains.CamerasFrame));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.PositiveX));

        EnvironmentFaceDraw face = scene.EnvironmentFaces.Should().ContainSingle().Subject;
        Placed(scene, face[DepthRanges.Middle]).Should().Equal(new Vector3(10_000, 0, 0));
        face[DepthRanges.Far].Count.Should().Be(0);
        face[DepthRanges.Near].Count.Should().Be(0);
    }

    [Fact]
    public void ARefreshPreparesSixFaces()
    {
        Vector3[] axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        RenderSnapshot snapshot = Empty();
        foreach (Vector3 axis in axes)
            snapshot.Instances.Add(Box(axis * 10, chainDepth: TestChains.CamerasFrame));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.All));

        scene
            .EnvironmentFaces.Select(face => face.Face)
            .Should()
            .Equal(
                CubeFace.PositiveX,
                CubeFace.NegativeX,
                CubeFace.PositiveY,
                CubeFace.NegativeY,
                CubeFace.PositiveZ,
                CubeFace.NegativeZ
            );
        for (int i = 0; i < CubeFaces.Count; i++)
            Placed(scene, scene.EnvironmentFaces[i])
                .Should()
                .Equal([axes[i] * 10], $"face {i} sees the box on its axis");
    }

    /// <summary>
    /// The main view and a face that both see an instance draw it with the same GPU instance, bit for bit: its place,
    /// its rotation interpolated between the two ticks, its scale, its material and its up. So one interpolation of the
    /// instance serves every view.
    /// </summary>
    [Fact]
    public void AFaceDrawsAnInstanceThatTheMainViewSeesWithTheMainViewsGpuInstance()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Camera.PreviousPosition = new(0, 0, 1);
        RenderInstance box = Gpu.Instance(
            mesh: new(1),
            material: new(3),
            position: new(1, 2, -10),
            rotation: Quaternion.CreateFromYawPitchRoll(0.7f, 0.2f, -0.4f),
            scale: new(1, 2, 0.5f),
            chainDepth: TestChains.CamerasFrame
        );
        box.PreviousPosition = new(-1, 2, -12);
        box.PreviousRotation = Quaternion.CreateFromYawPitchRoll(-0.3f, 0.5f, 0.1f);
        snapshot.Instances.Add(box);

        PreparedScene scene = Prepare(snapshot: snapshot, alpha: 0.3f, environment: MapOf(FaceMask.NegativeZ));

        CommandRange main = scene.Opaque[DepthRanges.Near].OneSided.Solid;
        CommandRange face = scene.EnvironmentFaces.Should().ContainSingle().Subject[DepthRanges.Near].OneSided.Solid;
        GpuInstance seen = scene.Instances[scene.Commands[main.First].FirstInstance];
        GpuInstance reflected = scene.Instances[scene.Commands[face.First].FirstInstance];
        reflected.Should().Be(seen, "the face and the main view draw one interpolation of the instance");
        seen.ModelRow0.W.Should().NotBe(1, "the place is interpolated");
    }

    /// <summary>
    /// An entity that holds a light sends its power through that light, so its glow is the light's face and bounces no
    /// light of its own (docs/plans/screen-space-illumination.md, "The bounce source"). The lights of the snapshot carry
    /// their entities, so the batcher marks the GPU instances of those entities alone.
    /// </summary>
    [Fact]
    public void TheGpuInstanceOfAnEntityThatHoldsALightSaysSoAndAnyOtherDoesNot()
    {
        RenderSnapshot snapshot = Empty();
        // Live generations start at 1, so the lamp's generation is one that a live entity has.
        RenderInstance lamp = Box(new(-2, 0, -10), chainDepth: TestChains.CamerasFrame);
        lamp.EntityIndex = 7;
        lamp.EntityGeneration = 3;
        RenderInstance screen = Box(new(2, 0, -10), chainDepth: TestChains.CamerasFrame);
        screen.EntityIndex = 8;
        screen.EntityGeneration = 3;
        snapshot.Instances.Add(lamp);
        snapshot.Instances.Add(screen);
        snapshot.Lights.Add(Lamp(new(-2, 1, -10)) with { EntityIndex = 7, EntityGeneration = 3 });

        PreparedScene scene = Prepare(snapshot: snapshot);

        GpuInstance[] drawn = scene.Instances[..scene.InstanceCount];
        drawn.Single(each => each.ModelRow0.W < 0).HoldsLight.Should().Be(1u, "a light carries the lamp's entity");
        drawn.Single(each => each.ModelRow0.W > 0).HoldsLight.Should().Be(0u, "no light carries the screen's entity");
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(7u)]
    [InlineData(65_537u)]
    public void OnlyTheGpuInstancesOfASpotLightsEntityCarryItsLightHolderMark(uint generation)
    {
        RenderSnapshot snapshot = Empty();
        RenderInstance lamp = Box(new(-2, 0, -10), chainDepth: TestChains.CamerasFrame);
        lamp.EntityIndex = 7;
        lamp.EntityGeneration = generation;
        RenderInstance screen = Box(new(2, 0, -10), chainDepth: TestChains.CamerasFrame);
        screen.EntityIndex = 8;
        screen.EntityGeneration = generation + 1;
        snapshot.Instances.Add(lamp);
        snapshot.Instances.Add(screen);
        snapshot.Lights.Add(
            Gpu.Spot(
                position: new(-2, 1, -10),
                direction: -Vector3.UnitY,
                color: Vector3.One,
                intensity: 1,
                innerConeAngle: 0.3f,
                outerConeAngle: 0.5f,
                chainDepth: TestChains.CamerasFrame
            ) with
            {
                EntityIndex = 7,
                EntityGeneration = generation,
            }
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        GpuInstance[] drawn = scene.Instances[..scene.InstanceCount];
        drawn
            .Single(each => each.ModelRow0.W < 0)
            .HoldsLight.Should()
            .Be(1u, "the spot light carries the lamp's entity");
        drawn.Single(each => each.ModelRow0.W > 0).HoldsLight.Should().Be(0u, "the screen carries no light");
    }

    [Fact]
    public void OnlyTheGpuInstancesOfATubeLightsEntityCarryItsLightHolderMark()
    {
        RenderSnapshot snapshot = Empty();
        RenderInstance bar = Box(new(-2, 0, -10), chainDepth: TestChains.CamerasFrame);
        bar.EntityIndex = 7;
        bar.EntityGeneration = 3;
        RenderInstance screen = Box(new(2, 0, -10), chainDepth: TestChains.CamerasFrame);
        screen.EntityIndex = 8;
        screen.EntityGeneration = 3;
        snapshot.Instances.Add(bar);
        snapshot.Instances.Add(screen);
        snapshot.Lights.Add(
            new TubeRenderLight(
                EntityIndex: 7,
                EntityGeneration: 3,
                Position: new(-2, 0, -10),
                PreviousPosition: new(-2, 0, -10),
                Axis: Vector3.UnitY,
                PreviousAxis: Vector3.UnitY,
                Color: Vector3.One,
                Power: 10,
                Radius: 0.1f,
                Length: 1,
                CastsShadow: false,
                ChainDepth: TestChains.CamerasFrame
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        GpuInstance[] drawn = scene.Instances[..scene.InstanceCount];
        drawn
            .Single(each => each.ModelRow0.W < 0)
            .HoldsLight.Should()
            .Be(1u, "the tube light carries the bar's entity, so a ray that hits the bar brings none of its glow");
        drawn.Single(each => each.ModelRow0.W > 0).HoldsLight.Should().Be(0u, "the screen carries no light");
    }

    /// <summary>
    /// The renderer follows an instance from frame to frame by its whole entity, slot and generation, because a new entity
    /// can take the slot of a destroyed one (ImpostorKey, the shadow tiles' casters). The batcher builds that entity.
    /// </summary>
    [Fact]
    public void APlacedInstanceCarriesTheSlotAndTheGenerationOfItsEntity()
    {
        RenderSnapshot snapshot = Empty();
        RenderInstance box = Box(new(0, 0, -10), chainDepth: TestChains.CamerasFrame);
        box.EntityIndex = 7;
        box.EntityGeneration = 4;
        snapshot.Instances.Add(box);

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene
            .PlacedSpan.ToArray()
            .Should()
            .ContainSingle()
            .Which.Entity.Should()
            .Be(new Entity(Index: 7, Generation: 4));
    }

    [Fact]
    public void AFaceDrawsOnlyTheInstancesOfTheSnapshotThatTheBatcherPreparesNow()
    {
        SceneBatcher batcher = Batchers.Default();
        RenderSnapshot before = Empty();
        before.Instances.Add(Box(new(0, 0, -10), chainDepth: TestChains.CamerasFrame));
        RenderSnapshot now = Empty();
        now.Instances.Add(Box(new(0, 1, -12), chainDepth: TestChains.CamerasFrame));

        Prepare(snapshot: before, batcher: batcher, environment: MapOf(FaceMask.NegativeZ));
        PreparedScene scene = Prepare(snapshot: now, batcher: batcher, environment: MapOf(FaceMask.NegativeZ));

        Placed(scene, scene.EnvironmentFaces[0]).Should().Equal(new Vector3(0, 1, -12));
    }

    [Fact]
    public void TheFacesCommandsAndInstancesFollowTheMainViews()
    {
        // The box ahead is in the main view and in face −Z; the pane is in the main view's transparent pass.
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Box(new(0, 0, -10), chainDepth: TestChains.CamerasFrame));
        snapshot.Instances.Add(Pane(new(0, 0, -20), chainDepth: TestChains.CamerasFrame));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.NegativeZ));

        SidedRuns near = scene.EnvironmentFaces.Should().ContainSingle().Subject[DepthRanges.Near];
        near.Should()
            .Be(
                new SidedRuns(OneSided: new(Solid: new(First: 2, Count: 1), Cutout: default), TwoSided: default),
                "the main view's opaque and transparent commands come first"
            );
        scene.Commands[near.OneSided.Solid.First].FirstInstance.Should().Be(2u);
        scene.InstanceCount.Should().Be(3);
    }

    [Fact]
    public void AFaceDrawsAnObjectThatAProcessorTookOutOfTheMainView()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Box(new(0, 0, -10), chainDepth: TestChains.CamerasFrame));
        SceneBatcher batcher = Batchers.Default();
        batcher.Processors.Add(new TakeAllOut());

        PreparedScene scene = Prepare(snapshot: snapshot, batcher: batcher, environment: MapOf(FaceMask.NegativeZ));

        scene.Opaque[DepthRanges.Near].Count.Should().Be(0);
        Placed(scene, scene.EnvironmentFaces[0]).Should().Equal(new Vector3(0, 0, -10));
    }

    /// <summary>Takes every visible instance out of the main view, as the impostor manager takes its impostors out.</summary>
    private sealed class TakeAllOut : IInstanceProcessor
    {
        public void Process(PreparedScene scene) => scene.VisibleCount = 0;
    }

    [Fact]
    public void AFaceKeepsALightThatTheMainViewLeavesOut()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Lamp(new(50, 0, 0)));

        PreparedScene scene = Prepare(
            snapshot: snapshot,
            highestStops: TenStopsDown,
            environment: MapOf(FaceMask.PositiveX)
        );

        scene.LightCount.Should().Be(1);
        scene.Lights[0].Position.Should().Be(new Vector3(50, 0, 0));
        scene.Constants.Counts.X.Should().Be(1u);
    }

    [Fact]
    public void EachLightSaysWhetherTheMainViewKeepsIt()
    {
        // Faces only, the main view only, the main view and a face, and faces only.
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Lamp(new(50, 0, 0)));
        snapshot.Lights.Add(Lamp(new(0, 0, -5)));
        snapshot.Lights.Add(Lamp(new(0, 0, -50)));
        snapshot.Lights.Add(Lamp(new(0, 50, 0)));

        PreparedScene scene = Prepare(snapshot: snapshot, highestStops: TenStopsDown, environment: MapOf(FaceMask.All));

        scene.LightSpan.ToArray().Select(light => light.InMainView).Should().Equal(false, true, true, false);
    }

    [Fact]
    public void TheGpuFormOfEachLightSaysWhetherTheMainViewKeepsIt()
    {
        // Faces only, then the main view only.
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Lamp(new(50, 0, 0)));
        snapshot.Lights.Add(Lamp(new(0, 0, -5)));

        PreparedScene scene = Prepare(snapshot: snapshot, highestStops: TenStopsDown, environment: MapOf(FaceMask.All));

        scene
            .LightSpan.ToArray()
            .Select(light => GpuLight.From(light).InMainView)
            .Should()
            .Equal([0u, 1u], "the main view's shaders skip the lights that only a face keeps");
    }

    [Fact]
    public void AFarLightOfTheMainViewTakesTheCascadeSetBeforeABrighterOneThatOnlyAFaceKeeps()
    {
        const float distance = 3e7f;
        double cutoff = LightUnits.CutoffIrradiance(TenStopsDown);
        PointRenderLight FarLamp(float z, double eyeShareOfTheCutoff) =>
            Gpu.Lamp(
                position: new(0, 0, z),
                color: Vector3.One,
                intensity: (float)(eyeShareOfTheCutoff * cutoff * distance * distance),
                chainDepth: TestChains.CamerasFrame
            ) with
            {
                CastsShadow = true,
            };
        // Behind the camera, with a reach of 0.9 of its distance, so that only face +Z keeps it, then ahead of it.
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(FarLamp(z: distance, eyeShareOfTheCutoff: 0.81));
        snapshot.Lights.Add(FarLamp(z: -distance, eyeShareOfTheCutoff: 0.25));

        PreparedScene scene = Prepare(snapshot: snapshot, highestStops: TenStopsDown, environment: MapOf(FaceMask.All));

        scene
            .LightSpan.ToArray()
            .Select(light => light.Shadow.TryGetValue(out CascadeSet _))
            .Should()
            .Equal([false, true], "the batcher's one cascade set goes to the main view's light");
    }

    [Fact]
    public void ALightThatOnlyAFaceThatThisFrameSkipsCouldReachIsLeftOut()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Lamp(new(50, 0, 0)));

        PreparedScene scene = Prepare(
            snapshot: snapshot,
            highestStops: TenStopsDown,
            environment: MapOf(FaceMask.All & ~FaceMask.PositiveX)
        );

        scene.LightCount.Should().Be(0);
    }

    [Fact]
    public void TheLightsKeepTheSnapshotsOrder()
    {
        // Faces only, the main view only, faces only, and one that lights nothing.
        Vector3[] places = [new(50, 0, 0), new(0, 0, -5), new(0, 50, 0), new(30, 0, 0)];
        bool[] shines = [true, true, true, false];
        RenderSnapshot snapshot = Dark();
        for (int i = 0; i < places.Length; i++)
            snapshot.Lights.Add(Lamp(places[i], shines[i]));

        PreparedScene scene = Prepare(snapshot: snapshot, highestStops: TenStopsDown, environment: MapOf(FaceMask.All));

        scene.LightSpan.ToArray().Select(light => light.Position).Should().Equal(places[0], places[1], places[2]);
    }

    [Fact]
    public void WithoutTheMapTheLightsAreTheMainViewsAlone()
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(Lamp(new(50, 0, 0)));
        snapshot.Lights.Add(Lamp(new(0, 0, -5)));
        snapshot.Instances.Add(Box(new(10, 0, 0), chainDepth: TestChains.CamerasFrame));

        PreparedScene scene = Prepare(snapshot: snapshot, highestStops: TenStopsDown, environment: new WithoutMap());

        scene.LightCount.Should().Be(1);
        scene.Lights[0].Position.Should().Be(new Vector3(0, 0, -5));
        scene.EnvironmentFaces.Should().BeEmpty();
    }

    [Fact]
    public void WithTheMapOnTheFrameConstantsHoldNoHemisphere()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Ambient = new()
        {
            SkyColor = new(0.2f, 0.3f, 0.4f),
            GroundColor = new(0.1f),
            Intensity = 2,
        };

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.None));

        scene.Constants.AmbientSky.Should().Be(Vector4.Zero);
        scene.Constants.AmbientGround.Should().Be(Vector4.Zero);
    }

    [Fact]
    public void WithoutTheMapTheAmbientLightIsTheSnapshotsHemisphere()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Ambient = new()
        {
            SkyColor = new(0.2f, 0.3f, 0.4f),
            GroundColor = new(0.1f),
            Intensity = 2,
        };

        PreparedScene scene = Prepare(snapshot: snapshot, environment: new WithoutMap());

        scene.Ambient.Should().Be(new AmbientSource(new HemisphereAmbient(snapshot.Ambient)));
        scene.Constants.AmbientSky.Should().Be(new Vector4(x: 0.4f, y: 0.6f, z: 0.8f, w: 0));
        scene.Constants.AmbientGround.Should().Be(new Vector4(x: 0.2f, y: 0.2f, z: 0.2f, w: 0));
    }

    [Fact]
    public void WithTheMapTheAmbientLightIsTheMapWithItsLatestReading()
    {
        var texels = new Vector3[Irradiance.Texels];
        texels[0] = new(1, 2, 3);
        var reading = new IrradianceReading(Frame: 7, Irradiance: new(texels));

        PreparedScene scene = Prepare(
            snapshot: Empty(),
            environment: new WithMap(Faces: new[] { FaceMask.None }, Irradiance: new IrradianceReading?[] { reading })
        );

        scene.Ambient.TryGetValue(out MapAmbient map).Should().BeTrue();
        IrradianceReading only = map.Irradiance.Span.ToArray().Should().ContainSingle().Subject!.Value;
        only.Frame.Should().Be(7);
        only.Irradiance[0].Should().Be(new Vector3(1, 2, 3));
    }

    [Fact]
    public void APreparedSceneDropsTheFacesOfTheFrameBefore()
    {
        RenderSnapshot snapshot = Empty();
        snapshot.Instances.Add(Box(new(10, 0, 0), chainDepth: TestChains.CamerasFrame));
        var scene = new PreparedScene();
        SceneBatcher batcher = Batchers.Default();
        batcher.Prepare(
            snapshot: snapshot,
            alpha: 1f,
            highestStops: Batchers.HighestStops,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materials: MaterialSlots.Of(8),
            environment: MapOf(FaceMask.All),
            region: DeformRegion.None,
            frame: Batchers.FirstFrame(),
            scene: scene
        );
        scene.EnvironmentFaces.Should().HaveCount(6);

        batcher.Prepare(
            snapshot: snapshot,
            alpha: 1f,
            highestStops: Batchers.HighestStops,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materials: MaterialSlots.Of(8),
            environment: MapOf(FaceMask.NegativeY),
            region: DeformRegion.None,
            frame: Batchers.FirstFrame(),
            scene: scene
        );

        scene.EnvironmentFaces.Select(face => face.Face).Should().Equal(CubeFace.NegativeY);
    }

    [Fact]
    public void APlacedSlotHoldsNothingOfTheInstanceThatFilledItTheFrameBefore()
    {
        var scene = new PreparedScene();
        scene.AddPlaced(motion: default) = new()
        {
            Depth = 5f,
            Mesh = 2,
            Material = 3,
            Map = 1,
        };
        scene.Reset();

        PreparedInstance slot = scene.AddPlaced(motion: default);

        slot.Should()
            .Be(
                default(PreparedInstance),
                "a caller that sets the fields one by one must not keep the last frame's others"
            );
    }

    // Maps per frame (docs/plans/environment-maps-per-frame.md). The chain of three below is the camera's frame, id 2, its
    // parent, id 1, and the root, id 0, all in the camera's axes: depth 0 takes map 2, depth 1 map 1, and depth 2 map 0.

    /// <summary>An empty snapshot with no light that shines, whose camera's chain has three frames, all in the camera's axes.</summary>
    private static RenderSnapshot ChainOfThree()
    {
        RenderSnapshot snapshot = Dark();
        TestChains.Of(snapshot, (2, Quaternion.Identity), (1, Quaternion.Identity), (0, Quaternion.Identity));
        return snapshot;
    }

    /// <summary>A <see cref="Lamp"/>, whose reach is 10 m at −10 stops, carried by an entity at <paramref name="chainDepth"/>.</summary>
    private static PointRenderLight LampAt(Vector3 position, ushort chainDepth) =>
        Lamp(position) with
        {
            ChainDepth = chainDepth,
        };

    [Fact]
    public void AnInstanceTakesTheMapOfItsChainDepth()
    {
        RenderSnapshot snapshot = ChainOfThree();
        // Side by side ahead of the camera, so the main view draws them in the snapshot's order.
        ushort[] depths = [0, 1, 2, 5];
        for (int i = 0; i < depths.Length; i++)
            snapshot.Instances.Add(Box(new(3 * i - 4, 0, -20), depths[i]));

        PreparedScene scene = Prepare(
            snapshot: snapshot,
            environment: MapOf(FaceMask.None, FaceMask.None, FaceMask.None)
        );

        scene
            .Instances.AsSpan(0, scene.InstanceCount)
            .ToArray()
            .Select(instance => instance.Map)
            .Should()
            .Equal([2u, 1u, 0u, 0u], "a depth past the served chain takes map 0");
    }

    [Fact]
    public void ALightTakesTheMapOfItsChainDepth()
    {
        RenderSnapshot snapshot = ChainOfThree();
        for (ushort depth = 0; depth < 3; depth++)
            snapshot.Lights.Add(LampAt(new(depth, 0, -5), depth));

        PreparedScene scene = Prepare(
            snapshot: snapshot,
            highestStops: TenStopsDown,
            environment: MapOf(FaceMask.None, FaceMask.None, FaceMask.None)
        );

        scene.Lights.AsSpan(0, scene.LightCount).ToArray().Select(light => light.Map).Should().Equal(2, 1, 0);
        scene
            .Lights.AsSpan(0, scene.LightCount)
            .ToArray()
            .Select(light => GpuLight.From(light).Map)
            .Should()
            .Equal([2u, 1u, 0u], "the GPU form carries the map that the faces filter by");
    }

    [Fact]
    public void ALitPuffTakesTheMapOfItsChainDepth()
    {
        RenderSnapshot snapshot = PuffSnapshot(GlowLook with { Shading = PuffShading.Lit(new(0.8f)) });
        TestChains.Of(snapshot, (2, Quaternion.Identity), (1, Quaternion.Identity), (0, Quaternion.Identity));
        // At one distance across the line of sight, so they sort in the snapshot's order.
        for (ushort depth = 0; depth < 3; depth++)
            snapshot.Puffs.Add(Puff(new(4 * depth - 4, 0, -10)) with { ChainDepth = depth });

        PreparedScene scene = Prepare(
            snapshot: snapshot,
            environment: MapOf(FaceMask.None, FaceMask.None, FaceMask.None)
        );

        Drawn(scene).OrderBy(puff => puff.PositionRadius.X).Select(puff => puff.Map).Should().Equal(2u, 1u, 0u);
    }

    [Fact]
    public void WithoutTheMapEveryObjectTakesMapZero()
    {
        RenderSnapshot snapshot = PuffSnapshot(GlowLook);
        TestChains.Of(snapshot, (2, Quaternion.Identity), (1, Quaternion.Identity), (0, Quaternion.Identity));
        snapshot.Instances.Add(Box(new(0, 0, -20), chainDepth: 0));
        snapshot.Lights.Add(LampAt(new(0, 0, -5), chainDepth: 0));
        snapshot.Puffs.Add(Puff(new(4, 0, -10)) with { ChainDepth = 0 });

        PreparedScene scene = Prepare(snapshot: snapshot, highestStops: TenStopsDown, environment: new WithoutMap());

        scene.MapCount.Should().Be(1);
        scene.Instances[0].Map.Should().Be(0u);
        scene.Lights.AsSpan(0, scene.LightCount).ToArray().Should().AllSatisfy(light => light.Map.Should().Be(0));
        Drawn(scene).Should().ContainSingle().Which.Map.Should().Be(0u);
    }

    /// <summary>The boxes of depths 0, 1 and 2 along +X, one above another, which face +X of every map could see.</summary>
    private static RenderSnapshot BoxesAlongPositiveX()
    {
        RenderSnapshot snapshot = ChainOfThree();
        for (ushort depth = 0; depth < 3; depth++)
            snapshot.Instances.Add(Box(new(10, 2 * depth, 0), depth));
        return snapshot;
    }

    [Fact]
    public void AFaceOfTheCamerasFrameMapDrawsEveryOpaqueInstance()
    {
        PreparedScene scene = Prepare(
            snapshot: BoxesAlongPositiveX(),
            environment: MapOf(FaceMask.None, FaceMask.None, FaceMask.PositiveX)
        );

        EnvironmentFaceDraw face = scene.EnvironmentFaces.Should().ContainSingle().Subject;
        face.Map.Should().Be(2);
        Placed(scene, face).Should().BeEquivalentTo([new Vector3(10, 0, 0), new(10, 2, 0), new(10, 4, 0)]);
    }

    [Fact]
    public void AFaceOfAnAncestorsMapLeavesOutTheSubtreeBelowIt()
    {
        PreparedScene scene = Prepare(
            snapshot: BoxesAlongPositiveX(),
            environment: MapOf(FaceMask.PositiveX, FaceMask.PositiveX, FaceMask.None)
        );

        scene.EnvironmentFaces.Select(face => face.Map).Should().Equal(0, 1);
        Placed(scene, scene.EnvironmentFaces[0]).Should().Equal(new Vector3(10, 4, 0));
        Placed(scene, scene.EnvironmentFaces[1])
            .Should()
            .BeEquivalentTo([new Vector3(10, 2, 0), new(10, 4, 0)], "map 1 leaves out the camera's frame, depth 0");
    }

    [Fact]
    public void AFaceOfATurnedMapKeepsWhatLiesAlongItsFramesAxis()
    {
        // The root's axes are turned a quarter turn about +Y in the camera's axes, so its +X lies along the camera's −Z.
        RenderSnapshot snapshot = Empty();
        var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        TestChains.Of(snapshot, (1, Quaternion.Identity), (0, quarter));
        snapshot.Instances.Add(Box(new(0, 0, -10), chainDepth: 1));
        snapshot.Instances.Add(Box(new(10, 0, 0), chainDepth: 1));

        PreparedScene scene = Prepare(snapshot: snapshot, environment: MapOf(FaceMask.PositiveX, FaceMask.None));

        EnvironmentFaceDraw face = scene.EnvironmentFaces.Should().ContainSingle().Subject;
        Vector3.Distance(face.Basis.Forward, -Vector3.UnitZ).Should().BeLessThan(1e-6f);
        Placed(scene, face).Should().Equal(new Vector3(0, 0, -10));
    }

    [Fact]
    public void AFaceKeepsNoLightOfTheSubtreeThatItLeavesOut()
    {
        // The lamp lies along +X, where the main view does not look, in the camera's frame: map 1.
        RenderSnapshot snapshot = Dark();
        TestChains.Of(snapshot, (1, Quaternion.Identity), (0, Quaternion.Identity));
        snapshot.Lights.Add(LampAt(new(50, 0, 0), chainDepth: 0));

        PreparedScene outer = Prepare(
            snapshot: snapshot,
            highestStops: TenStopsDown,
            environment: MapOf(FaceMask.PositiveX, FaceMask.None)
        );
        PreparedScene inner = Prepare(
            snapshot: snapshot,
            highestStops: TenStopsDown,
            environment: MapOf(FaceMask.None, FaceMask.PositiveX)
        );

        outer.LightCount.Should().Be(0, "the root's map leaves out the camera's frame and its lamp");
        inner.LightCount.Should().Be(1, "the camera's frame's map keeps its own lamp");
    }

    [Fact]
    public void TheMainViewKeepsTheLightsOfEveryMap()
    {
        RenderSnapshot snapshot = ChainOfThree();
        for (ushort depth = 0; depth < 3; depth++)
            snapshot.Lights.Add(LampAt(new(depth, 0, -5), depth));

        PreparedScene scene = Prepare(
            snapshot: snapshot,
            highestStops: TenStopsDown,
            environment: MapOf(FaceMask.None, FaceMask.None, FaceMask.None)
        );

        scene
            .Lights.AsSpan(0, scene.LightCount)
            .ToArray()
            .Select(light => light.Position)
            .Should()
            .Equal(new Vector3(0, 0, -5), new(1, 0, -5), new(2, 0, -5));
    }

    [Fact]
    public void TheTestStarSitsInTheRootSoEveryMapOfTheChainThatATestGivesKeepsIt()
    {
        // A real star sits in the root frame, which takes map 0. The test sets the chain after it makes the snapshot.
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        TestChains.Of(snapshot, (2, Quaternion.Identity), (1, Quaternion.Identity), (0, Quaternion.Identity));

        PreparedScene scene = Prepare(
            snapshot: snapshot,
            environment: MapOf(FaceMask.None, FaceMask.None, FaceMask.None)
        );

        scene.LightSpan.ToArray().Select(light => light.Map).Should().Equal(0);
    }

    [Fact]
    public void TheMapAxesAreTheChainsRotationsInterpolatedRootFirst()
    {
        RenderSnapshot snapshot = Empty();
        var parentNow = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f);
        var rootNow = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.2f);
        snapshot.Chain.Add(new(FrameId: 2, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        snapshot.Chain.Add(new(FrameId: 1, Rotation: parentNow, PreviousRotation: Quaternion.Identity));
        snapshot.Chain.Add(new(FrameId: 0, Rotation: rootNow, PreviousRotation: Quaternion.Identity));

        PreparedScene scene = Prepare(
            snapshot: snapshot,
            alpha: 0.5f,
            environment: MapOf(FaceMask.None, FaceMask.None, FaceMask.None)
        );

        Quaternion[] expected =
        [
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.2f),
            Quaternion.Identity,
        ];
        scene.MapCount.Should().Be(3);
        for (int map = 0; map < expected.Length; map++)
        {
            Quaternion axes = scene.MapAxes[map];
            Vector4 constant = scene.Constants.MapAxes[map];
            Vector4.Distance(AsVector(axes), AsVector(expected[map])).Should().BeLessThan(1e-6f, $"map {map}");
            constant.Should().Be(AsVector(axes), $"the constants hold map {map}'s axes");
        }

        scene.MapAxes[2].Should().Be(Quaternion.Identity, "the camera's frame lies in its own axes, bit for bit");
    }

    private static Vector4 AsVector(Quaternion q) => new(x: q.X, y: q.Y, z: q.Z, w: q.W);

    [Fact]
    public void ARefreshOfFourMapsPreparesTwentyFourFaces()
    {
        RenderSnapshot snapshot = Empty();
        TestChains.Of(
            snapshot,
            (3, Quaternion.Identity),
            (2, Quaternion.Identity),
            (1, Quaternion.Identity),
            (0, Quaternion.Identity)
        );

        PreparedScene scene = Prepare(
            snapshot: snapshot,
            environment: MapOf(FaceMask.All, FaceMask.All, FaceMask.All, FaceMask.All)
        );

        scene
            .EnvironmentFaces.Select(face => (face.Map, face.Face))
            .Should()
            .Equal(
                Enumerable
                    .Range(0, 4 * CubeFaces.Count)
                    .Select(slot => (slot / CubeFaces.Count, (CubeFace)(slot % CubeFaces.Count))),
                "the face pass takes the maps in index order, and each map's faces in layer order"
            );
    }

    /// <summary>A still puff: its previous place is its place.</summary>
    private static RenderPuff Puff(Vector3 position, float age = 0.5f, int look = 0) =>
        new()
        {
            Position = position,
            PreviousPosition = position,
            Age = age,
            Look = look,
        };

    /// <summary>A snapshot whose look table holds <paramref name="looks"/>, or only <see cref="GlowLook"/>.</summary>
    private static RenderSnapshot PuffSnapshot(params PuffLook[] looks)
    {
        RenderSnapshot snapshot = Empty();
        snapshot.PuffLooks.AddRange(looks.Length > 0 ? looks : [GlowLook]);
        return snapshot;
    }

    private static GpuPuff[] Drawn(PreparedScene scene) => scene.Puffs.AsSpan(0, scene.PuffCount).ToArray();

    private static Vector3 Center(GpuPuff puff) =>
        new(puff.PositionRadius.X, puff.PositionRadius.Y, puff.PositionRadius.Z);

    private static Vector3 Color(GpuPuff puff) => new(puff.ColorKind.X, puff.ColorKind.Y, puff.ColorKind.Z);

    private static void ShouldBeClose(Vector3 actual, Vector3 expected, float precision = 1e-5f)
    {
        actual.X.Should().BeApproximately(expected.X, precision);
        actual.Y.Should().BeApproximately(expected.Y, precision);
        actual.Z.Should().BeApproximately(expected.Z, precision);
    }

    private static TransparentRun GlassRun(int first, int count) =>
        new GlassCommands(Commands: new(first, count), TwoSided: false);

    private static TransparentRun PuffRun(int first, int count) => new PuffSprites(first, count);

    [Fact]
    public void PuffsInterpolatePositionAndAgeByAlpha()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.PuffAgeStep = 0.2f;
        snapshot.Puffs.Add(
            new()
            {
                Position = new(4, 0, -10),
                PreviousPosition = new(-4, 0, -10),
                Age = 1f,
                Look = 0,
            }
        );
        snapshot.Camera.PreviousPosition = new(0, 2, 0);

        GpuPuff puff = Drawn(Prepare(snapshot: snapshot, alpha: 0.25f)).Should().ContainSingle().Subject;

        // The place is lerp(−4, 4, 0.25) = −2, less the camera at lerp(2, 0, 0.25) = 1.5. The render age is
        // 1 − 0.75 · 0.2 = 0.85 s, so t = 0.425: the radius is 1 + 2 · 0.425 and the own share is 0.575.
        ShouldBeClose(Center(puff), new(-2, -1.5f, -10));
        puff.PositionRadius.W.Should().BeApproximately(1.85f, 1e-5f);
        ShouldBeClose(Color(puff), new Vector3(4, 2, 1) * 0.575f);
    }

    [Fact]
    public void AtAlphaOneAPuffDrawsAtItsCurrentPlaceAndAge()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.PuffAgeStep = 0.2f;
        snapshot.Puffs.Add(
            new()
            {
                Position = new(4, 0, -10),
                PreviousPosition = new(-4, 0, -10),
                Age = 1f,
                Look = 0,
            }
        );
        snapshot.Camera.PreviousPosition = new(0, 2, 0);

        GpuPuff puff = Drawn(Prepare(snapshot: snapshot, alpha: 1f)).Should().ContainSingle().Subject;

        ShouldBeClose(Center(puff), new(4, 0, -10));
        puff.PositionRadius.W.Should().BeApproximately(2f, 1e-5f);
        ShouldBeClose(Color(puff), new(2, 1, 0.5f));
    }

    [Fact]
    public void APuffIsNotDrawnBeforeItsBirthOrAfterItsLife()
    {
        // A newborn of age 0.04 s in a step of 0.1 s was born 0.06 s into the step. Its render age reaches 0 at alpha 0.6.
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.PuffAgeStep = 0.1f;
        snapshot.Puffs.Add(Puff(new(-2, 0, -10), age: 0.04f));
        // A puff at the end of its life: at alpha 1 its render age is its life, 2 s.
        snapshot.Puffs.Add(Puff(new(0, 0, -10), age: 2f));
        // A puff just before the end of its life.
        snapshot.Puffs.Add(Puff(new(2, 0, -10), age: 1.999f));

        Drawn(Prepare(snapshot: snapshot, alpha: 0.5f))
            .Select(p => p.PositionRadius.X)
            .Should()
            .BeEquivalentTo(
                [0f, 2f],
                "at alpha 0.5 the newborn's render age is −0.01 s, and the old puff's is 1.95 s, inside its life"
            );
        Drawn(Prepare(snapshot: snapshot, alpha: 0.7f))
            .Select(p => p.PositionRadius.X)
            .Should()
            .BeEquivalentTo([-2f, 0f, 2f], "at alpha 0.7 the newborn is 0.01 s old");
        Drawn(Prepare(snapshot: snapshot, alpha: 1f))
            .Select(p => p.PositionRadius.X)
            .Should()
            .BeEquivalentTo([-2f, 2f], "at alpha 1 the old puff's render age is its life, so it is dead");
    }

    [Fact]
    public void APuffsRadiusGrowsAndItsWeightFallsLinearlyOverItsLife()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        float[] ages = [0f, 0.5f, 1f, 1.5f];
        for (int i = 0; i < ages.Length; i++)
            snapshot.Puffs.Add(Puff(new(3 * i - 4.5f, 0, -12), age: ages[i]));

        GpuPuff[] drawn = [.. Drawn(Prepare(snapshot: snapshot)).OrderBy(p => p.PositionRadius.X)];

        drawn.Select(p => p.PositionRadius.W).Should().Equal([1f, 1.5f, 2f, 2.5f], (a, b) => MathF.Abs(a - b) < 1e-5f);
        drawn
            .Select(p => p.ColorKind.X / 4)
            .Should()
            .Equal([1f, 0.75f, 0.5f, 0.25f], (a, b) => MathF.Abs(a - b) < 1e-5f);
    }

    [Fact]
    public void APuffOutsideTheFrustumIsCulledByItsRadius()
    {
        // At 1600 × 900, tan(30°) · 16 / 9 = 1.0264, so the right plane passes x = 10.26 at 10 m ahead. A center at x = 12 lies
        // (12 − 10.264) / √(1 + 1.0264²) = 1.21 m outside it.
        RenderSnapshot snapshot = PuffSnapshot(
            GlowLook with
            {
                StartRadius = 1f,
                EndRadius = 1f,
            },
            GlowLook with
            {
                StartRadius = 1.5f,
                EndRadius = 1.5f,
            }
        );
        snapshot.Puffs.Add(Puff(new(12, 0, -10), look: 0));
        snapshot.Puffs.Add(Puff(new(12, 0, -10.0001f), look: 1));
        // Behind the eye: one wholly behind the near plane, and one whose sphere reaches in front of it.
        snapshot.Puffs.Add(Puff(new(0, 0, 5), look: 0));
        snapshot.Puffs.Add(Puff(new(0, 0, 0.5f), look: 0));

        GpuPuff[] drawn = Drawn(Prepare(snapshot: snapshot));

        drawn.Select(p => p.PositionRadius.W).Should().BeEquivalentTo([1.5f, 1f]);
        drawn.Select(Center).Should().BeEquivalentTo([new(12, 0, -10.0001f), new Vector3(0, 0, 0.5f)]);
    }

    [Fact]
    public void APuffWithANumberThatIsNotFiniteOrALookOutsideTheListIsLeftOut()
    {
        RenderSnapshot snapshot = PuffSnapshot(
            GlowLook,
            GlowLook with
            {
                StartRadius = 0f,
                EndRadius = 0f,
            },
            GlowLook with
            {
                StartRadius = -1f,
                EndRadius = -1f,
            },
            GlowLook with
            {
                Life = float.NaN,
            },
            GlowLook with
            {
                EndRadius = float.PositiveInfinity,
            }
        );
        var ahead = new Vector3(0, 0, -10);
        snapshot.Puffs.Add(Puff(ahead)); // drawn
        snapshot.Puffs.Add(Puff(new(float.NaN, 0, -10)));
        snapshot.Puffs.Add(
            new()
            {
                Position = ahead,
                PreviousPosition = new(float.PositiveInfinity, 0, -10),
                Look = 0,
                Age = 0.5f,
            }
        );
        snapshot.Puffs.Add(Puff(ahead, age: float.NaN));
        snapshot.Puffs.Add(Puff(ahead, look: 1)); // radius 0
        snapshot.Puffs.Add(Puff(ahead, look: 2)); // radius below 0
        snapshot.Puffs.Add(Puff(ahead, look: 3)); // life NaN
        snapshot.Puffs.Add(Puff(ahead, look: 4)); // infinite radius
        snapshot.Puffs.Add(Puff(ahead, look: 5)); // outside the list
        snapshot.Puffs.Add(Puff(ahead, look: -1));

        Drawn(Prepare(snapshot: snapshot, alpha: 0.5f))
            .Should()
            .ContainSingle()
            .Which.PositionRadius.W.Should()
            .BeApproximately(1.5f, 1e-5f);
    }

    [Fact]
    public void APuffWhoseLookHoldsNoShadingIsLeftOut()
    {
        // The second look holds no case, and the third is a free entry of the look table, all zero.
        RenderSnapshot snapshot = PuffSnapshot(GlowLook, GlowLook with { Shading = default }, default);
        snapshot.Puffs.Add(Puff(new(0, 0, -10), look: 0));
        snapshot.Puffs.Add(Puff(new(0, 0, -10), look: 1));
        snapshot.Puffs.Add(Puff(new(0, 0, -10), look: 2));

        Drawn(Prepare(snapshot: snapshot)).Should().ContainSingle();
    }

    [Fact]
    public void APuffBehindAPaneDrawsBeforeThePaneAndOneInFrontDrawsAfterIt()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: MaterialSlots.Glass,
                position: new(0, 0, -10),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Puffs.Add(Puff(new(0, 0, -5)));
        snapshot.Puffs.Add(Puff(new(0, 0, -15)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Transparent[DepthRanges.Near].Should().Equal(PuffRun(0, 1), GlassRun(0, 1), PuffRun(1, 1));
        Drawn(scene).Select(p => p.PositionRadius.Z).Should().Equal(-15f, -5f);
        scene.Transparent[DepthRanges.Far].Should().BeEmpty();
        scene.Transparent[DepthRanges.Middle].Should().BeEmpty();
    }

    [Fact]
    public void PuffsBetweenTwoPanesFormOneRun()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: MaterialSlots.Glass,
                position: new(0, 0, -5),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: MaterialSlots.Glass,
                position: new(0, 0, -20),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Puffs.Add(Puff(new(0, 0, -12)));
        snapshot.Puffs.Add(Puff(new(0, 0, -8)));
        snapshot.Puffs.Add(Puff(new(0, 0, -15)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        // The two panes share a mesh, but the puffs between them split their draws into two commands.
        scene.Transparent[DepthRanges.Near].Should().Equal(GlassRun(0, 1), PuffRun(0, 3), GlassRun(1, 1));
        Drawn(scene).Select(p => p.PositionRadius.Z).Should().Equal(-15f, -12f, -8f);
        scene.Commands[0].FirstInstance.Should().Be(0u);
        scene.Instances[0].ModelRow2.W.Should().Be(-20f);
        scene.Commands[1].FirstInstance.Should().Be(1u);
        scene.Instances[1].ModelRow2.W.Should().Be(-5f);
    }

    [Fact]
    public void AtEqualDistancesAPaneDrawsBeforeAPuffAndPuffsKeepTheOrderOfTheSnapshot()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Puffs.Add(Puff(new(0, 0, -10), age: 1.5f));
        snapshot.Puffs.Add(Puff(new(0, 0, -10), age: 0.5f));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: MaterialSlots.Glass,
                position: new(0, 0, -10),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Transparent[DepthRanges.Near].Should().Equal(GlassRun(0, 1), PuffRun(0, 2));
        Drawn(scene).Select(p => p.PositionRadius.W).Should().Equal(2.5f, 1.5f);
    }

    [Fact]
    public void APuffDrawsInTheRangeThatHoldsItsCenter()
    {
        // Each sphere of radius 30 m reaches across the border of 2 km, but a puff draws only in the range of its center,
        // with depth clamping, as a transparent instance does.
        RenderSnapshot snapshot = PuffSnapshot(GlowLook with { StartRadius = 30f, EndRadius = 30f });
        snapshot.Puffs.Add(Puff(new(0, 0, -1_990)));
        snapshot.Puffs.Add(Puff(new(0, 0, -2_010)));
        snapshot.Puffs.Add(Puff(new(0, 0, -3e6f)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Transparent[DepthRanges.Far].Should().Equal(PuffRun(0, 1));
        scene.Transparent[DepthRanges.Middle].Should().Equal(PuffRun(1, 1));
        scene.Transparent[DepthRanges.Near].Should().Equal(PuffRun(2, 1));
        Drawn(scene).Select(p => p.PositionRadius.Z).Should().Equal(-3e6f, -2_010f, -1_990f);
    }

    [Fact]
    public void AGpuPuffIs48BytesAndCarriesTheCaseOfItsShading()
    {
        Unsafe.SizeOf<GpuPuff>().Should().Be(48);
        ((int)Marshal.OffsetOf<GpuPuff>(nameof(GpuPuff.ColorKind))).Should().Be(16);
        RenderSnapshot snapshot = PuffSnapshot(
            GlowLook,
            GlowLook with
            {
                Shading = PuffShading.Lit(new(0.8f, 0.6f, 0.4f)),
            }
        );
        snapshot.Puffs.Add(Puff(new(-2, 0, -10), look: 0));
        snapshot.Puffs.Add(Puff(new(2, 0, -10), look: 1));

        GpuPuff[] drawn = [.. Drawn(Prepare(snapshot: snapshot)).OrderBy(p => p.PositionRadius.X)];

        // Age 0.5 s of a life of 2 s: the own share is 0.75.
        ShouldBeClose(Color(drawn[0]), new Vector3(4, 2, 1) * 0.75f);
        drawn[0].ColorKind.W.Should().Be(GpuPuff.GlowKind).And.Be(0f);
        ShouldBeClose(Color(drawn[1]), new Vector3(0.8f, 0.6f, 0.4f) * 0.75f);
        drawn[1].ColorKind.W.Should().Be(GpuPuff.LitKind).And.Be(1f);
    }

    [Fact]
    public void ManyPuffsGrowThePuffArrayAndDrawBackToFront()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        for (int i = 0; i < 300; i++)
            snapshot.Puffs.Add(Puff(new(0, 0, -5 - i * 37 % 300)));

        PreparedScene scene = Prepare(snapshot: snapshot);

        scene.Transparent[DepthRanges.Near].Should().Equal(PuffRun(0, 300));
        Drawn(scene).Select(p => -p.PositionRadius.Z).Should().BeInDescendingOrder().And.HaveCount(300);
    }

    [Fact]
    public void APreparedSceneDropsThePuffsAndTheRunsOfTheFrameBefore()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Puffs.Add(Puff(new(0, 0, -10)));
        var scene = new PreparedScene();
        SceneBatcher batcher = Batchers.Default();
        batcher.Prepare(
            snapshot: snapshot,
            alpha: 1f,
            highestStops: Batchers.HighestStops,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materials: MaterialSlots.Of(8),
            environment: default,
            region: DeformRegion.None,
            frame: Batchers.FirstFrame(),
            scene: scene
        );
        scene.PuffCount.Should().Be(1);

        batcher.Prepare(
            snapshot: Empty(),
            alpha: 1f,
            highestStops: Batchers.HighestStops,
            width: 1600,
            height: 900,
            meshes: Meshes,
            materials: MaterialSlots.Of(8),
            environment: default,
            region: DeformRegion.None,
            frame: Batchers.FirstFrame(),
            scene: scene
        );

        scene.PuffCount.Should().Be(0);
        scene.Transparent.Should().AllSatisfy(runs => runs.Should().BeEmpty());
    }

    [Fact]
    public void RenderStatsCountsTheDrawnPuffs()
    {
        RenderSnapshot snapshot = PuffSnapshot();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: MaterialSlots.Glass,
                position: new(0, 0, -5),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: new(2),
                material: MaterialSlots.Glass,
                position: new(0, 0, -20),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.Puffs.Add(Puff(new(0, 0, -12)));
        snapshot.Puffs.Add(Puff(new(0, 0, -8)));
        snapshot.Puffs.Add(Puff(new(0, 0, 30))); // behind the eye

        RenderStats stats = VulkanRenderer.StatsOf(
            scene: Prepare(snapshot: snapshot),
            impostors: null,
            indirectDrawCalls: 0,
            gpuTimings: null,
            exposure: null
        );

        stats.Puffs.Should().Be(2);
        stats.TransparentCommands.Should().Be(2, "a run of puffs adds no indirect command");
    }

    [Fact]
    public void TheFrameConstantsMatchTheShadersFrameBlock()
    {
        Unsafe.SizeOf<GpuFrameConstants>().Should().Be(3808);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.RenderParams))).Should().Be(288);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.OutputViewport))).Should().Be(304);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.PreviousJitter))).Should().Be(320);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.Counts))).Should().Be(352);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.RootRotation))).Should().Be(432);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.CascadeSets))).Should().Be(448);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.MapAxes))).Should().Be(1472);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.Atmosphere)))
            .Should()
            .Be(2144, "the atmosphere follows the 42 map axes");
        Unsafe.SizeOf<GpuAtmosphere>().Should().Be(96, "std140 lays out six vec4s in 96 bytes");
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.MotionViewProj)))
            .Should()
            .Be(2240, "the previous camera follows the atmosphere");
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.PreviousEye))).Should().Be(2368);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.PreviousRootRotation))).Should().Be(2448);
        ((int)Marshal.OffsetOf<GpuFrameConstants>(nameof(GpuFrameConstants.MapBoxes))).Should().Be(2464);
        Unsafe.SizeOf<GpuMapBox>().Should().Be(32);
        Unsafe.SizeOf<MapBoxArray>().Should().Be(1344);
    }

    [Fact]
    public void TheScatterKernelMatchesTheShadersKernelBlock()
    {
        ((int)Marshal.OffsetOf<GpuScatterKernel>(nameof(GpuScatterKernel.WidthScale)))
            .Should()
            .Be(528, "std140 puts the float after the 33 vec4");
        Unsafe.SizeOf<GpuScatterKernel>().Should().Be(532);
    }

    [Fact]
    public void GpuStructsMatchTheShaderLayouts()
    {
        Unsafe.SizeOf<GpuInstance>().Should().Be(96, "std430 rounds 92 bytes of members up to the vec4's 16");
        ((int)Marshal.OffsetOf<GpuInstance>(nameof(GpuInstance.Map))).Should().Be(80);
        ((int)Marshal.OffsetOf<GpuInstance>(nameof(GpuInstance.PlacedIndex))).Should().Be(88);
        Unsafe.SizeOf<GpuMaterial>().Should().Be(64, "std430 rounds a struct with a vec4 up to 16 bytes");
        ((int)Marshal.OffsetOf<GpuMaterial>(nameof(GpuMaterial.Metallic))).Should().Be(28);
        ((int)Marshal.OffsetOf<GpuMaterial>(nameof(GpuMaterial.MetallicRoughnessMap))).Should().Be(48);
        ((int)Marshal.OffsetOf<GpuMaterial>(nameof(GpuMaterial.Shading))).Should().Be(52);
        ((int)Marshal.OffsetOf<GpuMaterial>(nameof(GpuMaterial.AlphaCutoff))).Should().Be(56);
        Unsafe.SizeOf<GpuLight>().Should().Be(128, "std430 rounds 116 bytes of members up to the vec4's 16");
        ((int)Marshal.OffsetOf<GpuLight>(nameof(GpuLight.HalfHeight))).Should().Be(64);
        ((int)Marshal.OffsetOf<GpuLight>(nameof(GpuLight.Shadow))).Should().Be(80);
        ((int)Marshal.OffsetOf<GpuLight>(nameof(GpuLight.Occluders))).Should().Be(96);
        ((int)Marshal.OffsetOf<GpuLight>(nameof(GpuLight.Map))).Should().Be(108, "std430 packs a uint after a uvec3");
        ((int)Marshal.OffsetOf<GpuLight>(nameof(GpuLight.InMainView))).Should().Be(112);
        Unsafe.SizeOf<GpuOutline>().Should().Be(48);
        ((int)Marshal.OffsetOf<GpuOutline>(nameof(GpuOutline.Radii))).Should().Be(32);
        Unsafe.SizeOf<OutlinePushConstants>().Should().Be(4);
        Unsafe
            .SizeOf<GpuCascadeSet>()
            .Should()
            .Be(256, "std140 lays out an array of structs at a multiple of 16 bytes");
        Unsafe.SizeOf<GpuPuff>().Should().Be(48);
        ((int)Marshal.OffsetOf<GpuPuff>(nameof(GpuPuff.Map))).Should().Be(32);
        Unsafe.SizeOf<GpuShadowTile>().Should().Be(80, "std430 rounds a struct with a mat4 up to 16 bytes");
        ((int)Marshal.OffsetOf<GpuShadowTile>(nameof(GpuShadowTile.Layer))).Should().Be(64);
        ((int)Marshal.OffsetOf<GpuShadowTile>(nameof(GpuShadowTile.Near))).Should().Be(72);
        Unsafe.SizeOf<DrawPushConstants>().Should().Be(80);
        Unsafe.SizeOf<FaceLightPushConstants>().Should().Be(52);
        ((int)Marshal.OffsetOf<FaceLightPushConstants>(nameof(FaceLightPushConstants.Map))).Should().Be(48);
        Unsafe.SizeOf<EnvironmentPrefilterLevel>().Should().Be(16);
        Unsafe.SizeOf<EnvironmentPrefilterPushConstants>().Should().Be(88);
        ((int)Marshal.OffsetOf<EnvironmentPrefilterPushConstants>(nameof(EnvironmentPrefilterPushConstants.Samples)))
            .Should()
            .Be(64);
        ((int)Marshal.OffsetOf<EnvironmentPrefilterPushConstants>(nameof(EnvironmentPrefilterPushConstants.Map)))
            .Should()
            .Be(76, "the irradiance's two shaders declare it at offset 76");
        ((int)Marshal.OffsetOf<EnvironmentPrefilterPushConstants>(nameof(EnvironmentPrefilterPushConstants.FirstFace)))
            .Should()
            .Be(80, "the convolution reads the run of faces right after the map");
        Unsafe.SizeOf<EnvironmentDownsamplePushConstants>().Should().Be(12);
        ((int)Marshal.OffsetOf<EnvironmentDownsamplePushConstants>(nameof(EnvironmentDownsamplePushConstants.Map)))
            .Should()
            .Be(8);
        Unsafe.SizeOf<DrawIndexedIndirectCommand>().Should().Be(20);
        Unsafe.SizeOf<Vertex>().Should().Be(48);
    }

    [Fact]
    public void GpuVertexIsTheVertexWithItsCardNumberBehindIt()
    {
        Unsafe.SizeOf<GpuVertex>().Should().Be(52, "deform.comp writes 13 words per vertex");
        ((int)Marshal.OffsetOf<GpuVertex>(nameof(GpuVertex.TexCoord)))
            .Should()
            .Be((int)Marshal.OffsetOf<Vertex>(nameof(Vertex.TexCoord)), "the fields of Vertex keep their offsets");
        ((int)Marshal.OffsetOf<GpuVertex>(nameof(GpuVertex.Card)))
            .Should()
            .Be(48, "deform.comp writes the card as word 12");
    }

    [Theory]
    [InlineData("mesh.vert")]
    [InlineData("gbuffer.frag")]
    [InlineData("lighting.vert")]
    [InlineData("direct.frag")]
    [InlineData("accumulate_temporal.comp")]
    [InlineData("accumulate_spatial.comp")]
    [InlineData("composite.frag")]
    [InlineData("transparent.frag")]
    [InlineData("fullscreen.vert")]
    [InlineData("tonemap.frag")]
    [InlineData("shadow.vert")]
    [InlineData("impostor_capture.frag")]
    [InlineData("impostor.vert")]
    [InlineData("impostor.frag")]
    [InlineData("outline.vert")]
    [InlineData("outline.frag")]
    [InlineData("puff.vert")]
    [InlineData("puff.frag")]
    public void ShadersAreCompiledAndEmbedded(string file)
    {
        byte[] spirv = ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, file);
        spirv.Length.Should().BeGreaterThan(100);
        (spirv.Length % 4).Should().Be(0, "SPIR-V is a stream of 32-bit words");
    }
}
