using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The opaque runs of a depth range as the back-face pass draws them (docs/plans/back-face-depth-layer.md, decision 4):
/// the back faces of a closed mesh with a one-sided material, and both faces of everything else, as ranges of the
/// commands that the G-buffer pass draws. No GPU needed. The camera sits at the render origin and looks down −Z.
/// </summary>
public sealed class BackFaceRunsTests
{
    private const int ClosedBox = 0;
    private const int OpenQuad = 1;

    private static readonly MeshInfo[] Meshes =
    [
        new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: 0.866f, Deform: null, Closed: true),
        new(FirstIndex: 36, IndexCount: 6, VertexOffset: 24, Radius: 1.414f, Deform: null, Closed: false),
    ];

    private static PreparedScene Prepare(RenderSnapshot snapshot)
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
                materials: MaterialSlots.Of(10),
                environment: default,
                region: DeformRegion.None,
                frame: Batchers.FirstFrame(),
                scene: scene
            );
        return scene;
    }

    /// <summary>A unit instance of <paramref name="mesh"/> and <paramref name="material"/>, <paramref name="depth"/> m down −Z.</summary>
    private static RenderInstance InstanceOf(int mesh, MaterialId material, float depth) =>
        Gpu.Instance(
            mesh: new(mesh),
            material: material,
            position: new(0, 0, -depth),
            scale: Vector3.One,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>The depths along −Z of the GPU instances that <paramref name="commands"/> draw, in their order.</summary>
    private static List<float> Depths(PreparedScene scene, CommandRange commands)
    {
        var depths = new List<float>();
        for (int c = commands.First; c < commands.First + commands.Count; c++)
        {
            DrawIndexedIndirectCommand command = scene.Commands[c];
            for (uint i = command.FirstInstance; i < command.FirstInstance + command.InstanceCount; i++)
                depths.Add(-scene.Instances[i].ModelRow2.W);
        }

        return depths;
    }

    /// <summary>The indices of the commands of <paramref name="runs"/>, run after run.</summary>
    private static IEnumerable<int> CommandsOf(params CommandRange[] runs) =>
        runs.SelectMany(run => Enumerable.Range(run.First, run.Count));

    [Fact]
    public void AClosedMeshWithAOneSidedMaterialDrawsInTheClosedOneSidedRun()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(InstanceOf(ClosedBox, new(0), depth: 10));

        BackFaceRuns runs = Prepare(snapshot).Opaque[DepthRanges.Near].BackFace;

        runs.Should().Be(new BackFaceRuns(ClosedOneSided: new(Solid: new(0, 1), Cutout: default), BothFaces: default));
    }

    [Fact]
    public void AClosedMeshWithATwoSidedMaterialDrawsInTheBothFacesRun()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(InstanceOf(ClosedBox, MaterialSlots.Hair, depth: 10));

        BackFaceRuns runs = Prepare(snapshot).Opaque[DepthRanges.Near].BackFace;

        runs.Should().Be(new BackFaceRuns(ClosedOneSided: default, BothFaces: new(Solid: new(0, 1), Cutout: default)));
    }

    [Fact]
    public void AnOpenMeshWithAOneSidedMaterialDrawsInTheBothFacesRun()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(InstanceOf(OpenQuad, new(0), depth: 10));

        BackFaceRuns runs = Prepare(snapshot).Opaque[DepthRanges.Near].BackFace;

        runs.Should().Be(new BackFaceRuns(ClosedOneSided: default, BothFaces: new(Solid: new(0, 1), Cutout: default)));
    }

    [Fact]
    public void TheGBufferDrawsAnOpenMeshWithAOneSidedMaterialInItsOneSidedRun()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(InstanceOf(OpenQuad, new(0), depth: 10));
        snapshot.Instances.Add(InstanceOf(ClosedBox, new(0), depth: 12));

        SidedRuns runs = Prepare(snapshot).Opaque[DepthRanges.Near].Sided;

        runs.Should()
            .Be(
                new SidedRuns(OneSided: new(Solid: new(0, 2), Cutout: default), TwoSided: default),
                "the G-buffer pass culls the back faces of every one-sided material"
            );
    }

    [Fact]
    public void EachKindOfRunKeepsItsSolidAndItsCutoutCommandsApart()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(InstanceOf(ClosedBox, MaterialSlots.FringedHair, depth: 10));
        snapshot.Instances.Add(InstanceOf(OpenQuad, MaterialSlots.Cutout, depth: 11));
        snapshot.Instances.Add(InstanceOf(ClosedBox, MaterialSlots.Cutout, depth: 12));
        snapshot.Instances.Add(InstanceOf(ClosedBox, MaterialSlots.Hair, depth: 13));
        snapshot.Instances.Add(InstanceOf(OpenQuad, new(0), depth: 14));
        snapshot.Instances.Add(InstanceOf(ClosedBox, new(0), depth: 15));

        PreparedScene scene = Prepare(snapshot);
        BackFaceRuns runs = scene.Opaque[DepthRanges.Near].BackFace;

        Depths(scene, runs.ClosedOneSided.Solid).Should().Equal(15);
        Depths(scene, runs.BothFaces.Solid).Should().Equal(14, 13);
        Depths(scene, runs.ClosedOneSided.Cutout).Should().Equal(12);
        Depths(scene, runs.BothFaces.Cutout).Should().Equal(11, 10);
    }

    [Fact]
    public void TheGBufferKeepsItsFourRunsByTheMaterialsSideBesideTheSplit()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        snapshot.Instances.Add(InstanceOf(ClosedBox, MaterialSlots.FringedHair, depth: 10));
        snapshot.Instances.Add(InstanceOf(OpenQuad, MaterialSlots.Cutout, depth: 11));
        snapshot.Instances.Add(InstanceOf(ClosedBox, MaterialSlots.Cutout, depth: 12));
        snapshot.Instances.Add(InstanceOf(ClosedBox, MaterialSlots.Hair, depth: 13));
        snapshot.Instances.Add(InstanceOf(OpenQuad, new(0), depth: 14));
        snapshot.Instances.Add(InstanceOf(ClosedBox, new(0), depth: 15));

        PreparedScene scene = Prepare(snapshot);
        SidedRuns runs = scene.Opaque[DepthRanges.Near].Sided;

        Depths(scene, runs.OneSided.Solid).Should().Equal(15, 14);
        Depths(scene, runs.TwoSided.Solid).Should().Equal(13);
        Depths(scene, runs.OneSided.Cutout).Should().Equal(12, 11);
        Depths(scene, runs.TwoSided.Cutout).Should().Equal(10);
    }

    [Fact]
    public void TheBackFaceRunsDrawEveryCommandOfTheGBuffersRunsOnceInEachRange()
    {
        // Every pair of mesh and material, near, middle and far, and one of each that straddles the near and middle ranges.
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
        MaterialId[] materials = [new(0), MaterialSlots.Cutout, MaterialSlots.Hair, MaterialSlots.FringedHair];
        (float Depth, float Scale)[] places = [(10, 1), (50_000, 1_000), (3e7f, 6e6f), (2_000, 50)];
        foreach (int mesh in new[] { ClosedBox, OpenQuad })
        foreach (MaterialId material in materials)
        foreach ((float depth, float scale) in places)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: new(mesh),
                    material: material,
                    position: new(0, 0, -depth),
                    scale: new(scale),
                    chainDepth: TestChains.CamerasFrame
                )
            );

        PreparedScene scene = Prepare(snapshot);

        for (int range = 0; range < DepthRanges.Count; range++)
        {
            RangeRuns opaque = scene.Opaque[range];
            (SidedRuns sided, BackFaceRuns backFace) = (opaque.Sided, opaque.BackFace);
            int[] gBuffer =
            [
                .. CommandsOf(sided.OneSided.Solid, sided.TwoSided.Solid, sided.OneSided.Cutout, sided.TwoSided.Cutout),
            ];
            int[] pass =
            [
                .. CommandsOf(
                    backFace.ClosedOneSided.Solid,
                    backFace.BothFaces.Solid,
                    backFace.ClosedOneSided.Cutout,
                    backFace.BothFaces.Cutout
                ),
            ];

            gBuffer.Should().HaveCount(8, $"range {range} draws every pair of mesh and material");
            pass.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(gBuffer, $"range {range} draws the same commands");
        }
    }
}
