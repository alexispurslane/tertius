using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The light that the coarse grids of the directional light volume inherit from surfaces in a finer grid: each surface's
/// direct light is evaluated and restrained once, in the finest grid that holds it, and a coarse receiver sees it with
/// its own color. The camera looks along -Z from the origin. The receiver is a white panel 3 m ahead, outside the
/// 0.25 m grid (which ends 2 m ahead), so it reads the 0.5 m grid.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class LightVolumeInheritanceTests
{
    /// <summary>A star up and ahead of the camera: it lights faces that look along -Z and +Y.</summary>
    private static readonly Vector3 StarAheadAndAbove = Vector3.Normalize(new(0, 1, -0.5f));

    private static readonly Vector4 Red = new(x: 1, y: 0, z: 0, w: 1);

    /// <summary>The receiver's center: 3 m ahead, past the 0.25 m grid.</summary>
    private const float ReceiverZ = -3;

    private readonly record struct Panel(Vector3 Center, Vector3 Size, Vector4 Color)
    {
        public Vector3 Emission { get; init; }
    }

    /// <summary>
    /// The receiver's red minus green at the center pixel, a measure of red light alone: neutral light adds the same to
    /// both. The panels are boxes of the given sizes, opaque and fully rough. Without the star, only emission lights the
    /// scene.
    /// </summary>
    private static float CenterRedTint(IEnumerable<Panel> panels, Vector3 camera = default, bool star = true)
    {
        var renderer = VulkanRenderer.CreateHeadless(
            new RendererOptions
            {
                Width = 64,
                Height = 64,
                WarmUpFrames = 0,
                Validation = true,
                Upscaling = new NoUpscaling(),
                Exposure = new FixedExposure(0),
                EnvironmentMap = new NoEnvironmentMap(),
                ScreenTrace = new NoScreenTrace(),
                LightVolume = true,
                Impostors = false,
            }
        );
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            RenderSnapshot scene = Gpu.Snapshot(StarAheadAndAbove, starIrradiance: 20);
            scene.Ambient = default;
            if (!star)
                scene.Lights.Clear();
            scene.Camera.Position = camera;
            scene.Camera.PreviousPosition = camera;
            uint entity = 1;
            Panel receiver = new(new(camera.X, camera.Y, ReceiverZ), new(3, 3, 0.08f), Vector4.One);
            foreach (Panel panel in panels.Prepend(receiver))
            {
                MaterialId material = renderer.CreateMaterial(
                    new(BaseColor: panel.Color, Roughness: 1, Emissive: panel.Emission)
                );
                scene.Instances.Add(
                    Gpu.Instance(
                        mesh: box,
                        material: material,
                        position: panel.Center,
                        scale: panel.Size,
                        chainDepth: TestChains.CamerasFrame,
                        flags: RenderFlags.CastsShadow,
                        radius: 0.5f * panel.Size.Length(),
                        entity: entity++
                    )
                );
            }

            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1f);
            float[] hdr = renderer.ReadHdr();
            int center = 4 * (32 * 64 + 32);
            return hdr[center] - hdr[center + 1];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Three strips 0.23 m wide and 1 m tall, 1.1 m ahead and below the view's center, each in its own 0.25 m column
    /// starting at <paramref name="firstLeft"/>, every 0.5 m: one strip in each 0.5 m cell of a row.
    /// </summary>
    private static IEnumerable<Panel> Strips(float firstLeft, Vector4 color) =>
        Enumerable
            .Range(0, 3)
            .Select(i => new Panel(new(firstLeft + 0.125f + 0.5f * i, -1, -1.1f), new(0.23f, 0.98f, 0.02f), color));

    [Fact]
    public void ANeutralNeighbourInTheSameCoarseCellKeepsAColoredReflectorsTintOnACoarseReceiver()
    {
        float alone = CenterRedTint(Strips(firstLeft: 0.5f, Red));
        float withNeighbours = CenterRedTint(
            Strips(firstLeft: 0.5f, Red).Concat(Strips(firstLeft: 0.75f, Vector4.One))
        );

        alone.Should().BeGreaterThan(1e-4f, "sunlit red strips light the receiver beyond the fine grid");
        withNeighbours
            .Should()
            .BeGreaterThan(
                alone * 0.8f,
                "each strip is restrained in its own 0.25 m cell, not after it mixes with white in the 0.5 m cell"
            );
    }

    [Fact]
    public void AShadowedNeighbourInTheSameCoarseTileDoesNotDarkenASunlitReflectorForACoarseReceiver()
    {
        Panel[] red = [new(new(0.75f, -1, -1.1f), new(0.48f, 0.98f, 0.02f), Red)];
        // A white panel 0.5 m to the side, in the next 1 m tile of the fine grid but the same 2 m tile of the coarse grid,
        // under a horizontal sheet that keeps the star off it.
        Panel[] shadowed =
        [
            new(new(1.5f, -1, -1.1f), new(0.8f, 0.98f, 0.02f), Vector4.One),
            new(new(1.5f, -0.3f, -1.52f), new(0.9f, 0.02f, 0.75f), Vector4.One),
        ];

        float alone = CenterRedTint(red);
        float beside = CenterRedTint(red.Concat(shadowed));

        alone.Should().BeGreaterThan(1e-4f, "a sunlit red panel lights the receiver beyond the fine grid");
        beside
            .Should()
            .BeGreaterThan(alone * 0.8f, "the red panel's direct light comes from its own 1 m tile of the fine grid");
    }

    [Fact]
    public void AReflectorThatCrossesTheFineGridsFaceAsTheCameraMovesKeepsItsLightOnACoarseReceiver()
    {
        // The grids snap to whole cells of the world: the 0.25 m grid spans the camera's x, rounded to 0.25 m, ± 2 m, and the
        // 0.5 m grid the camera's x, rounded to 0.5 m, ± 4 m. Between these camera positions the 0.5 m grid stays put and
        // the 0.25 m grid's +X face moves from 2 m to 2.25 m: the reflector at x 2 to 2.5 lies outside the fine grid, then
        // across its face. Across the face, one 0.5 m cell holds inherited fine surfaces and the coarse grid's own.
        Panel[] red = [new(new(2.25f, -1, -1.1f), new(0.48f, 0.98f, 0.02f), Red)];
        float outside = CenterRedTint(red, camera: new(0.05f, 0, 0));
        float across = CenterRedTint(red, camera: new(0.2f, 0, 0));

        outside.Should().BeGreaterThan(1e-4f);
        across
            .Should()
            .BeInRange(outside * 0.75f, outside * 1.25f, "each surface counts once, whichever grid holds it");
    }

    [Fact]
    public void AnOpaqueSheetInsideTheFineGridStillBlocksAReflectorFromACoarseReceiver()
    {
        // A sheet faces the camera between the strips and the receiver, wholly inside the fine grid, so only the surfaces
        // that the coarse grid inherits can block the light there. It clears the camera's view of the receiver's center and
        // the strips' sunlit faces.
        Panel[] red = [.. Strips(firstLeft: 0.5f, Red)];
        Panel sheet = new(new(1.1f, -1.05f, -1.9f), new(1.7f, 1.9f, 0.02f), Vector4.One);

        float open = CenterRedTint(red);
        float blocked = CenterRedTint(red.Append(sheet));

        open.Should().BeGreaterThan(1e-4f);
        blocked.Should().BeLessThan(open * 0.05f, "the coarse grid keeps the fine surfaces' area for occlusion");
    }

    [Fact]
    public void AnInheritedEmitterLightsACoarseReceiverWithoutDirectLights()
    {
        Panel emitter = new(new(0.75f, -1, -1.1f), new(0.48f, 0.98f, 0.02f), Vector4.UnitW) { Emission = new(8, 0, 0) };

        float tint = CenterRedTint([emitter], star: false);

        tint.Should()
            .BeGreaterThan(1e-4f, "the emitter lies inside the fine grid and the receiver samples the coarse grid");
    }

    [Fact]
    public void AnInheritedEmitterLightsACoarseReceiverWithTheRadianceThatItsSizeAndDistanceGive()
    {
        // The emitter's face toward the receiver is a 0.48 × 0.98 m rectangle of red radiance 8 at z = -1.11. The receiver's
        // center, at z = -2.96 and the white panel's albedo 1, takes irradiance L A cos θ cos θ' / d² from it and reflects
        // E / π. The capture, the shading, the transfer and the transport keep that radiance within a factor of two.
        Panel emitter = new(new(0.75f, -1, -1.1f), new(0.48f, 0.98f, 0.02f), Vector4.UnitW) { Emission = new(8, 0, 0) };
        Vector3 toEmitter = new Vector3(0.75f, -1, -1.11f) - new Vector3(0, 0, -2.96f);
        float cosine = toEmitter.Z / toEmitter.Length();
        float expected = 8 * 0.48f * 0.98f * cosine * cosine / toEmitter.LengthSquared() / MathF.PI;

        float tint = CenterRedTint([emitter], star: false);

        tint.Should()
            .BeInRange(
                expected * 0.5f,
                expected * 2,
                "emission keeps its physical scale through the raster, the shading and the transfer to the 0.5 m grid"
            );
    }

    [Fact]
    public void AReflectorLitOnlyByBouncedLightStillTintsACoarseReceiver()
    {
        // No star: a yellow emitter lies flat below and ahead of the red strips. Red light reaches the receiver only when
        // the strips reflect the emitter's light, in the second sweep.
        Panel yellow = new(new(1.25f, -1.6f, -1.52f), new(1.5f, 0.02f, 0.75f), Vector4.UnitW)
        {
            Emission = new(8, 8, 0),
        };
        Panel[] red = [.. Strips(firstLeft: 0.5f, Red)];

        float without = CenterRedTint([yellow], star: false);
        float with = CenterRedTint(red.Prepend(yellow), star: false);

        with.Should().BeGreaterThan(without + 1e-5f, "the second bounce carries the red strips' color");
    }
}
