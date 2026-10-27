using System.Diagnostics;
using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Pre-exposure (docs/plans/auto-exposure.md, "Pre-exposure"): every pass that writes the HDR image or the emissive
/// target multiplies its light by 2^S, the impostor atlas keeps radiance, and the tone map multiplies by nothing. Each
/// test fixes the exposure. The camera is at the render origin with identity rotation, so it looks down −Z.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class PreExposureTests
{
    private const int Width = 160,
        Height = 90;

    /// <summary>The largest difference of one channel between two images that hold equal HDR values: none, so 1 of 255.</summary>
    private const float Tolerance = 1f / 255;

    /// <summary>A point on the view axis 3 m ahead, where each scene's writer covers the middle of the image.</summary>
    private static readonly Vector3 Ahead = new(0, 0, -3);

    /// <summary>Where the sphere of the impostor scenes stands: on the view axis, far enough to be a billboard.</summary>
    private static readonly Vector3 FarAhead = new(0, 0, -12);

    /// <summary>The base color of the lit squares.</summary>
    private static readonly Vector4 Gray = new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1);

    /// <summary>A square facing the camera, as the plane primitive faces +Y.</summary>
    private static readonly Quaternion FacingTheCamera = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>Each kind of writer of the HDR image or the emissive target that "Every writer" lists.</summary>
    public enum Writer
    {
        LitOpaqueSurface,
        EmissiveSurface,
        TransparentSurface,
        GlowPuff,
        LitPuff,
        ImpostorBillboard,
        MapLitSurface,
    }

    private static Vector2 Middle => Gpu.Project(point: Ahead, width: Width, height: Height);

    /// <summary>
    /// A scene in which only <paramref name="writer"/> writes the middle of the image, with its light or its emission
    /// times <paramref name="light"/>. The sun shines from behind the camera, and there is no ambient light.
    /// </summary>
    private static RenderSnapshot Scene(VulkanRenderer renderer, Writer writer, float light)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        MeshId square = renderer.CreateMesh(Primitives.Plane(0.5f));
        switch (writer)
        {
            case Writer.LitOpaqueSurface:
                Gpu.Star(snapshot: snapshot, irradiance: 0.3f * light);
                AddMatteSquare(renderer, snapshot, square);
                break;
            case Writer.EmissiveSurface:
                snapshot.Instances.Add(
                    Facing(mesh: square, material: BrdfTesting.Glow(renderer, new(0.05f * light)), position: Ahead)
                );
                break;
            case Writer.TransparentSurface:
                Gpu.Star(snapshot: snapshot, irradiance: 0.3f * light);
                // The glass reflects the sun and glows, so both of its terms must take the exposure.
                MaterialId glass = renderer.CreateMaterial(
                    new(
                        BaseColor: Gray with
                        {
                            W = 0.5f,
                        },
                        Roughness: 1,
                        Emissive: new(0.03f * light),
                        Alpha: new GlassAlpha()
                    )
                );
                snapshot.Instances.Add(Facing(mesh: square, material: glass, position: Ahead));
                AddBlackBackdrop(renderer, snapshot);
                break;
            case Writer.GlowPuff:
                AddPuff(snapshot, PuffShading.Glow(new(0.1f * light)));
                AddBlackBackdrop(renderer, snapshot);
                break;
            case Writer.LitPuff:
                Gpu.Star(snapshot: snapshot, irradiance: light);
                AddPuff(snapshot, PuffShading.Lit(new(0.8f)));
                AddBlackBackdrop(renderer, snapshot);
                break;
            case Writer.ImpostorBillboard:
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: renderer.CreateMesh(Primitives.Sphere(1f, 48, 24)),
                        material: BrdfTesting.Glow(renderer, new(0.05f * light)),
                        position: FarAhead,
                        scale: Vector3.One,
                        entity: 1,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                break;
            case Writer.MapLitSurface:
                // Lit only by an emissive square behind the camera, which reaches it through the map.
                AddMatteSquare(renderer, snapshot, square);
                AddGlowBehind(renderer, snapshot, radiance: 0.3f * light);
                break;
            default:
                throw new UnreachableException();
        }

        return snapshot;
    }

    /// <summary>A gray matte square ahead, facing the camera.</summary>
    private static void AddMatteSquare(VulkanRenderer renderer, RenderSnapshot snapshot, MeshId square) =>
        snapshot.Instances.Add(
            Facing(mesh: square, material: renderer.CreateMaterial(MaterialDesc.Color(Gray, 1)), position: Ahead)
        );

    /// <summary>An emissive square of <paramref name="radiance"/> 4 m behind the camera, facing it.</summary>
    private static void AddGlowBehind(VulkanRenderer renderer, RenderSnapshot snapshot, float radiance) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(3f)),
                material: BrdfTesting.Glow(renderer, new(radiance)),
                position: new(0, 0, 4),
                scale: Vector3.One,
                rotation: Quaternion.Conjugate(FacingTheCamera),
                chainDepth: TestChains.CamerasFrame
            )
        );

    private static RenderInstance Facing(
        MeshId mesh,
        MaterialId material,
        Vector3 position,
        RenderFlags flags = RenderFlags.None
    ) =>
        Gpu.Instance(
            mesh: mesh,
            material: material,
            position: position,
            scale: Vector3.One,
            rotation: FacingTheCamera,
            flags: flags,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>A black square of reflectance 0 behind the middle of the image, so that no star shines through.</summary>
    private static void AddBlackBackdrop(VulkanRenderer renderer, RenderSnapshot snapshot) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(2f)),
                material: BrdfTesting.Glow(renderer, Vector3.Zero),
                position: new(0, 0, -5),
                scale: Vector3.One,
                rotation: FacingTheCamera,
                chainDepth: TestChains.CamerasFrame
            )
        );

    private static void AddPuff(RenderSnapshot snapshot, PuffShading shading)
    {
        snapshot.PuffLooks.Add(
            new(Life: 2f, StartRadius: 0.5f, EndRadius: 0.5f, TemperatureRatio: 0.5f, Shading: shading)
        );
        snapshot.Puffs.Add(
            new()
            {
                Position = Ahead,
                PreviousPosition = Ahead,
                Age = 0f,
                Look = 0,
            }
        );
    }

    /// <summary>The color in the middle of the image of <paramref name="writer"/>'s scene, drawn by a new renderer.</summary>
    private static Vector3 ShownInTheMiddle(Writer writer, ExposureSetting exposure, float light, string name)
    {
        bool impostors = writer == Writer.ImpostorBillboard;
        EnvironmentMapSetting map =
            writer == Writer.MapLitSurface
                ? SceneEnvironmentMap.Default with
                {
                    FaceSize = 32,
                }
                : new NoEnvironmentMap();
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            impostors: impostors,
            exposure: exposure,
            environmentMap: map
        );
        try
        {
            RenderSnapshot snapshot = Scene(renderer, writer, light);
            // An impostor is captured in its first frame and drawn as a billboard from then on.
            for (int frame = 0; frame < 3; frame++)
                renderer.Render(snapshot, 1f);
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
            if (impostors)
                renderer.LastFrameStats.Impostors.Should().Be(1, "the sphere is far enough to be a billboard");
            return image.Color(Middle);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(Writer.LitOpaqueSurface)]
    [InlineData(Writer.EmissiveSurface)]
    [InlineData(Writer.TransparentSurface)]
    [InlineData(Writer.GlowPuff)]
    [InlineData(Writer.LitPuff)]
    [InlineData(Writer.ImpostorBillboard)]
    [InlineData(Writer.MapLitSurface)]
    public void EachWriterOfTheHdrImageMultipliesItsLightByTheExposure(Writer writer)
    {
        Vector3 exposed = ShownInTheMiddle(
            writer: writer,
            exposure: new FixedExposure(2),
            light: 1,
            name: $"pre-exposure-{writer}-two-stops"
        );
        Vector3 brighter = ShownInTheMiddle(
            writer: writer,
            exposure: new FixedExposure(0),
            light: 4,
            name: $"pre-exposure-{writer}-four-times"
        );
        Vector3 unexposed = ShownInTheMiddle(
            writer: writer,
            exposure: new FixedExposure(0),
            light: 1,
            name: $"pre-exposure-{writer}-zero-stops"
        );

        BrdfTesting
            .LargestChannelDifference(exposed, unexposed)
            .Should()
            .BeGreaterThan(20f / 255, "the scene shows its writer in the middle, and 4 times the light shows brighter");
        BrdfTesting
            .LargestChannelDifference(exposed, brighter)
            .Should()
            .BeLessThanOrEqualTo(
                Tolerance,
                "2 stops multiply the light by exactly 4, but the exposed scene shows {0} and the brighter one {1}",
                exposed,
                brighter
            );
    }

    [Fact]
    public void TheStarsBrightenWithTheExposure()
    {
        static float SkyLuma(ExposureSetting exposure)
        {
            VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180, exposure: exposure);
            try
            {
                RenderSnapshot sky = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
                sky.Ambient.Intensity = 0;
                Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: sky, name: "pre-exposure-stars");
                float sum = 0;
                for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    sum += image.Luma(x, y);
                return sum;
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        // The stars are physical, at most about 3 · 2⁻¹⁰ W/(m²·sr) at their peaks, so 10 stops show them.
        float unexposed = SkyLuma(new FixedExposure(10));
        float exposed = SkyLuma(new FixedExposure(12));

        unexposed.Should().BePositive("some stars show in the view");
        exposed.Should().BeGreaterThan(1.5f * unexposed, "the stars are radiance, which the exposure multiplies");
    }

    [Fact]
    public void AnImpostorCaptureHoldsRadianceThatDoesNotDependOnTheExposure()
    {
        static float[] TileAt(ExposureSetting exposure)
        {
            VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, impostors: true, exposure: exposure);
            try
            {
                MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24));
                MaterialId material = renderer.CreateMaterial(
                    new(
                        BaseColor: new(x: 0.6f, y: 0.5f, z: 0.4f, w: 1),
                        Roughness: 0.6f,
                        Emissive: new(0.02f, 0.01f, 0)
                    )
                );
                // The cutoff irradiance follows the highest exposure, so the star's window differs between the two
                // renderers. At 4 W/m² the star is so far inside both reaches that both windows round to exactly 1.
                RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 4);
                snapshot.Ambient.Intensity = 0;
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: sphere,
                        material: material,
                        position: FarAhead,
                        scale: Vector3.One,
                        entity: 1,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                for (int frame = 0; frame < 3; frame++)
                    renderer.Render(snapshot, 1f);
                ImpostorStatus status = renderer
                    .Impostors!.Status(
                        new(EntityIndex: 1, Part: 0, Mesh: sphere.Value, Material: MaterialStore.Slot(material))
                    )!
                    .Value;
                status.Impostor.Should().BeTrue("the sphere is far enough to be a billboard");
                return renderer.ReadImpostorTile(status.Tile).Rgba;
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        float[] unexposed = TileAt(new FixedExposure(0));
        float[] exposed = TileAt(new FixedExposure(3));

        unexposed.Max().Should().BePositive("the tile holds the lit and glowing sphere");
        exposed.Should().Equal(unexposed, "the atlas stores radiance, and the billboard pre-exposes it when it draws");
    }

    [Fact]
    public void AnImpostorBillboardShowsWhatItsGeometryShowsAtTheSameExposure()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            impostors: true,
            exposure: new FixedExposure(3)
        );
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24));
            MaterialId material = renderer.CreateMaterial(
                new(BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), Roughness: 0.8f, Emissive: new(0.004f))
            );

            RenderSnapshot Scene(RenderFlags flags)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0.05f);
                snapshot.Ambient.Intensity = 0;
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: sphere,
                        material: material,
                        position: FarAhead,
                        scale: Vector3.One,
                        flags: flags,
                        entity: 1,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                return snapshot;
            }

            Vector3 geometry = Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: Scene(RenderFlags.NoImpostor),
                    name: "pre-exposure-impostor-geometry"
                )
                .Color(Middle);
            for (int frame = 0; frame < 3; frame++)
                renderer.Render(Scene(RenderFlags.None), 1f);
            Vector3 billboard = Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: Scene(RenderFlags.None),
                    name: "pre-exposure-impostor-billboard"
                )
                .Color(Middle);
            renderer.LastFrameStats.Impostors.Should().Be(1, "the sphere is far enough to be a billboard");

            geometry.Length().Should().BeGreaterThan(0.3f, "3 stops show the dim sphere well above black");
            BrdfTesting
                .LargestChannelDifference(billboard, geometry)
                .Should()
                .BeLessThanOrEqualTo(
                    2f / 255,
                    "the billboard pre-exposes the capture's radiance as the geometry pre-exposes its light"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
