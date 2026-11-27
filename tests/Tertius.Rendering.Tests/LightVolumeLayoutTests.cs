using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>Every cell keeps its transport storage and a lighting group aligned to the grid's center.</summary>
[Collection(GpuCollection.Name)]
public sealed class LightVolumeLayoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryCellHasItsOwnTransportStorageAndALightingGroupOnTheSameLattice(int level)
    {
        float[] values = Probe();

        // The three image channels check levels 0, 1 and 2. Every coordinate, including the shell's last cell,
        // executes the production shader functions. The remaining pixels lie outside the checked range.
        Enumerable
            .Range(0, level == 2 ? 5832 : 4096)
            .Where(cell => values[4 * cell + level] != 1)
            .Should()
            .BeEmpty("every cell must address its own storage and a group anchored to the grid's center");
    }

    [Fact]
    public void OpenSkyReachesEveryTransportDirectionAndFilteredLobeIncludingTheLastDispatchCells()
    {
        float[] values = Probe();

        Enumerable
            .Range(0, 5832)
            .Where(cell => values[4 * cell + 3] != 1)
            .Should()
            .BeEmpty("an empty volume transmits the whole sky through every cell, including the final dispatch tail");
    }

    [Theory]
    [InlineData(3.75f)]
    [InlineData(8.5f)]
    public void ASmallSurfaceAtALevelsEdgeContributesItsCoverageToTheOuterGrid(float distance)
    {
        float[] values = Probe(
            shader: "volume_surface_probe.comp",
            surface: (new(0.125f, 0.125f, distance), new(0.25f))
        );

        Enumerable
            .Range(0, 5832)
            .Sum(cell => values[4 * cell])
            .Should()
            .BeGreaterThan(
                0,
                "the outer grid keeps inherited coverage at 3.75 m and directly captured coverage at 8.5 m"
            );
    }

    private static float[] Probe(
        string shader = "volume_layout_probe.comp",
        (Vector3 Position, Vector3 Scale)? surface = null
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 128,
            height: 48,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            lightVolume: true
        );
        try
        {
            var probe = ScreenTraceProbe.On(renderer, shader);
            RenderSnapshot scene = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
            if (surface is { } panel)
            {
                MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
                MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
                scene.Instances.Add(
                    Gpu.Instance(
                        mesh: box,
                        material: matte,
                        position: panel.Position,
                        scale: panel.Scale,
                        chainDepth: TestChains.CamerasFrame,
                        entity: 1
                    )
                );
            }
            renderer.Render(scene, 1f);
            return probe.Read();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
