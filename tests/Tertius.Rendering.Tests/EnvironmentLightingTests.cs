using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The readers of the scene environment map (docs/plans/environment-map.md, "Reading the map"): with the map on, the
/// ambient light of the lighting pass, the glass, the impostor captures and the lit puffs comes from the map, and the
/// hemisphere lights nothing. The camera is at the render origin with identity rotation, so it looks down −Z with +Y up.
/// A headless draw at an unchanged snapshot time is a long step, so each draw refreshes every face of the map.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class EnvironmentLightingTests
{
    private const int Width = 320,
        Height = 180;

    /// <summary>The largest difference of one channel between two images that must agree: rounding to 8 bits and a step.</summary>
    private const float Tolerance = 2f / 255;

    /// <summary>The map of every test: small, because no test here depends on the face size.</summary>
    private static readonly SceneEnvironmentMap SmallMap = SceneEnvironmentMap.Default with { FaceSize = 32 };

    /// <summary>A square of the plane primitive, which faces +Y, turned to face −Z, toward the camera from behind it.</summary>
    private static readonly Quaternion FacingMinusZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2);

    /// <summary>A square turned to face +Z, toward the camera from ahead of it.</summary>
    private static readonly Quaternion FacingPlusZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    private static readonly Vector2 Center = new(Width / 2, Height / 2);

    private static VulkanRenderer Renderer(EnvironmentMapSetting map, bool impostors = false) =>
        Gpu.Headless(width: Width, height: Height, impostors: impostors, environmentMap: map);

    /// <summary>A snapshot with no sun and no hemisphere light, so that only what a test adds lights its scene.</summary>
    private static RenderSnapshot Dark()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    private static MaterialId Metal(VulkanRenderer renderer, float roughness) =>
        renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: roughness, Metallic: 1));

    private static MaterialId Matte(VulkanRenderer renderer) =>
        renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 1));

    private static MeshId Sphere(VulkanRenderer renderer) => renderer.CreateMesh(Primitives.Sphere(1f, 64, 32));

    private static void AddSphere(
        RenderSnapshot snapshot,
        MeshId mesh,
        MaterialId material,
        Vector3 position,
        float radius,
        RenderFlags flags = RenderFlags.None,
        uint entity = 0
    ) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: mesh,
                material: material,
                position: position,
                scale: new(radius),
                flags: flags,
                entity: entity
            )
        );

    /// <summary>A red emissive square of 6 m behind the camera, facing it: what the tests' mirrors reflect.</summary>
    private static void AddRedSquareBehind(VulkanRenderer renderer, RenderSnapshot snapshot, float radiance) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(3f)),
                material: BrdfTesting.Glow(renderer, new(radiance, 0, 0)),
                position: new(0, 0, 4),
                scale: Vector3.One,
                rotation: FacingMinusZ
            )
        );

    /// <summary>Half the width of the dome's box, in m.</summary>
    private const float DomeHalfWidth = 50;

    /// <summary>The mesh of one panel of the dome.</summary>
    private static MeshId DomePanel(VulkanRenderer renderer) => renderer.CreateMesh(Primitives.Plane(DomeHalfWidth));

    /// <summary>
    /// An emissive dome of <paramref name="glow"/> over the camera's horizon: the ceiling and the upper halves of the four
    /// walls of a box 100 m wide, each a <paramref name="panel"/> facing in. Below the horizon the map holds the black sky.
    /// </summary>
    private static void AddDome(RenderSnapshot snapshot, MeshId panel, MaterialId glow)
    {
        const float half = DomeHalfWidth;
        // The plane's local z becomes the world's −y on the walls along x, and its local x on the walls along z, so
        // the scale halves each wall to the part above the horizon.
        (Vector3 Position, Quaternion Rotation, Vector3 Scale)[] panels =
        [
            (new(0, half, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI), Vector3.One),
            (new(0, half / 2, -half), FacingPlusZ, new(1, 1, 0.5f)),
            (new(0, half / 2, half), FacingMinusZ, new(1, 1, 0.5f)),
            (new(-half, half / 2, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2), new(0.5f, 1, 1)),
            (new(half, half / 2, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), new(0.5f, 1, 1)),
        ];
        foreach ((Vector3 position, Quaternion rotation, Vector3 scale) in panels)
            snapshot.Instances.Add(
                Gpu.Instance(mesh: panel, material: glow, position: position, scale: scale, rotation: rotation)
            );
    }

    /// <summary>Renders <paramref name="snapshot"/> twice and captures the second draw.</summary>
    private static Capture Shown(VulkanRenderer renderer, RenderSnapshot snapshot, string name)
    {
        renderer.Render(snapshot, 1f);
        return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
    }

    /// <summary>What a white metal sphere of <paramref name="roughness"/> 4 m ahead shows with a red square behind the camera.</summary>
    private static Capture SphereBeforeTheRedSquare(EnvironmentMapSetting map, float roughness, string name)
    {
        VulkanRenderer renderer = Renderer(map);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
            AddRedSquareBehind(renderer, snapshot, radiance: 1);
            AddSphere(
                snapshot: snapshot,
                mesh: Sphere(renderer),
                material: Metal(renderer, roughness),
                position: new(0, 0, -4),
                radius: 1
            );
            return Shown(renderer, snapshot, name);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>How far red stands out of a shown color: its red channel above the larger of the other two.</summary>
    private static float Redness(Vector3 color) => color.X - MathF.Max(color.Y, color.Z);

    [Fact]
    public void AMirrorSphereReflectsABrightObjectBehindTheCamera()
    {
        Vector3 withMap = SphereBeforeTheRedSquare(SmallMap, roughness: 0, "environment-mirror-sphere").Color(Center);
        Vector3 withoutMap = SphereBeforeTheRedSquare(
                new NoEnvironmentMap(),
                roughness: 0,
                "environment-mirror-hemisphere"
            )
            .Color(Center);

        Redness(withMap)
            .Should()
            .BeGreaterThan(
                0.3f,
                "the sphere's center reflects the red square behind the camera, but shows {0}",
                withMap
            );
        Redness(withoutMap)
            .Should()
            .BeLessThan(0.02f, "without the map the sphere reflects the hemisphere's gray, but shows {0}", withoutMap);
    }

    [Fact]
    public void ARoughSphereShowsItBlurred()
    {
        Capture mirror = SphereBeforeTheRedSquare(SmallMap, roughness: 0, "environment-blur-mirror");
        Capture rough = SphereBeforeTheRedSquare(SmallMap, roughness: 0.6f, "environment-blur-rough");

        static int RedPixels(Capture image)
        {
            int count = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                count += Redness(image.Color(x, y)) > 4f / 255 ? 1 : 0;
            return count;
        }

        Redness(rough.Color(Center))
            .Should()
            .BeLessThan(Redness(mirror.Color(Center)), "a rough lobe spreads the square's light over more directions");
        RedPixels(rough).Should().BeGreaterThan(RedPixels(mirror), "the rough sphere shows the red blurred, wider");
    }

    [Fact]
    public void UnderAWhiteSkyAndABlackGroundAMatteSphereLooksAsUnderTheHemisphere()
    {
        static Capture DomeLit(EnvironmentMapSetting map, string name)
        {
            VulkanRenderer renderer = Renderer(map);
            try
            {
                RenderSnapshot snapshot = Dark();
                // The hemisphere of the same light, which only the renderer without the map uses.
                snapshot.Ambient = new()
                {
                    SkyColor = Vector3.One,
                    GroundColor = Vector3.Zero,
                    Intensity = 1,
                };
                AddDome(snapshot, DomePanel(renderer), BrdfTesting.Glow(renderer, Vector3.One));
                AddSphere(
                    snapshot: snapshot,
                    mesh: Sphere(renderer),
                    material: Matte(renderer),
                    position: new(0, 0, -3),
                    radius: 0.5f
                );
                return Shown(renderer, snapshot, name);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Capture withMap = DomeLit(SmallMap, "environment-dome-map");
        Capture withHemisphere = DomeLit(new NoEnvironmentMap(), "environment-dome-hemisphere");

        // The sphere spans about 29 px around the center, so this square lies inside it.
        for (int y = -18; y <= 18; y++)
        for (int x = -18; x <= 18; x++)
        {
            Vector3 map = withMap.Color((int)Center.X + x, (int)Center.Y + y);
            Vector3 hemisphere = withHemisphere.Color((int)Center.X + x, (int)Center.Y + y);
            BrdfTesting
                .LargestChannelDifference(map, hemisphere)
                .Should()
                .BeLessThanOrEqualTo(
                    Tolerance,
                    "a white upper half and a black lower half light a matte sphere as the hemisphere of one and zero, "
                        + "but at offset ({0}, {1}) the map shows {2} and the hemisphere {3}",
                    x,
                    y,
                    map,
                    hemisphere
                );
        }
    }

    [Fact]
    public void WithTheMapOnTheHemisphereLightsNothing()
    {
        static Vector3 SphereWithAmbient(float intensity, string name)
        {
            VulkanRenderer renderer = Renderer(SmallMap);
            try
            {
                RenderSnapshot snapshot = Dark();
                snapshot.Ambient = new()
                {
                    SkyColor = new(1, 0, 0),
                    GroundColor = new(1, 0, 0),
                    Intensity = intensity,
                };
                AddSphere(
                    snapshot: snapshot,
                    mesh: Sphere(renderer),
                    material: Matte(renderer),
                    position: new(0, 0, -3),
                    radius: 0.5f
                );
                return Shown(renderer, snapshot, name).Color(Center);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Vector3 underRed = SphereWithAmbient(intensity: 2, "environment-red-hemisphere");
        Vector3 underNone = SphereWithAmbient(intensity: 0, "environment-no-hemisphere");

        BrdfTesting
            .LargestChannelDifference(underRed, underNone)
            .Should()
            .BeLessThanOrEqualTo(
                1f / 255,
                "the map is the ambient light, so a bright red hemisphere adds nothing, but the sphere shows {0} under it",
                underRed
            );
    }

    [Fact]
    public void GlassReflectsTheMap()
    {
        static Vector3 PaneCenter(EnvironmentMapSetting map, string name)
        {
            VulkanRenderer renderer = Renderer(map);
            try
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
                // The pane passes 0.8 of what lies behind it and blends its own light by 0.2, so the square is bright.
                AddRedSquareBehind(renderer, snapshot, radiance: 40);
                MaterialId glass = renderer.CreateMaterial(
                    new(BaseColor: new(x: 1, y: 1, z: 1, w: 0.2f), Roughness: 0, Reflectance: 0.5f)
                );
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: renderer.CreateMesh(Primitives.Plane(1f)),
                        material: glass,
                        position: new(0, 0, -3),
                        scale: Vector3.One,
                        rotation: FacingPlusZ,
                        flags: RenderFlags.Transparent
                    )
                );
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: renderer.CreateMesh(Primitives.Plane(3f)),
                        material: BrdfTesting.Glow(renderer, Vector3.Zero),
                        position: new(0, 0, -5),
                        scale: Vector3.One,
                        rotation: FacingPlusZ
                    )
                );
                return Shown(renderer, snapshot, name).Color(Center);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Vector3 withMap = PaneCenter(SmallMap, "environment-glass-map");
        Vector3 withoutMap = PaneCenter(new NoEnvironmentMap(), "environment-glass-hemisphere");

        Redness(withMap)
            .Should()
            .BeGreaterThan(0.1f, "the pane reflects the red square behind the camera: {0}", withMap);
        Redness(withoutMap).Should().BeLessThan(0.02f, "without the map it reflects the hemisphere: {0}", withoutMap);
    }

    /// <summary>The meshes and the material of the impostor tests, made once so that the impostor keeps its key.</summary>
    private sealed class ImpostorParts(VulkanRenderer renderer)
    {
        public MeshId Panel { get; } = DomePanel(renderer);
        public MeshId Sphere { get; } = EnvironmentLightingTests.Sphere(renderer);

        public MaterialId Clay { get; } =
            renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.7f, y: 0.5f, z: 0.3f, w: 1), roughness: 0.5f));

        public ImpostorKey Key => new(EntityIndex: 1, Part: 0, Mesh: Sphere.Value, Material: MaterialStore.Slot(Clay));

        /// <summary>
        /// The dome of <paramref name="dome"/> and the clay sphere 12 m ahead, about 26 px high, so it is a billboard
        /// unless <paramref name="flags"/> keep it geometry.
        /// </summary>
        public RenderSnapshot Scene(MaterialId dome, RenderFlags flags)
        {
            RenderSnapshot snapshot = Dark();
            AddDome(snapshot, Panel, dome);
            AddSphere(
                snapshot: snapshot,
                mesh: Sphere,
                material: Clay,
                position: new(0, 0, -12),
                radius: 1,
                flags: flags,
                entity: 1
            );
            return snapshot;
        }
    }

    [Fact]
    public void AnImpostorShowsWhatItsGeometryShowsUnderTheMap()
    {
        VulkanRenderer renderer = Renderer(SmallMap, impostors: true);
        try
        {
            var parts = new ImpostorParts(renderer);
            MaterialId dome = BrdfTesting.Glow(renderer, Vector3.One);
            Vector3 geometry = Shown(
                    renderer,
                    parts.Scene(dome, RenderFlags.NoImpostor),
                    "environment-impostor-geometry"
                )
                .Color(Center);
            for (int frame = 0; frame < 3; frame++)
                renderer.Render(parts.Scene(dome, RenderFlags.None), 1f);
            Vector3 billboard = Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: parts.Scene(dome, RenderFlags.None),
                    name: "environment-impostor-billboard"
                )
                .Color(Center);
            renderer.LastFrameStats.Impostors.Should().Be(1, "the sphere is small enough to be a billboard");

            geometry.Should().NotBe(Vector3.Zero, "the dome lights the sphere");
            BrdfTesting
                .LargestChannelDifference(billboard, geometry)
                .Should()
                .BeLessThanOrEqualTo(
                    Tolerance,
                    "the capture bakes the map's light, so the billboard shows {0} as the geometry shows {1}",
                    billboard,
                    geometry
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnImpostorIsRecapturedWhenTheMapsLightChanges()
    {
        // Gpu.Headless's default: a reading of the irradiance reaches the CPU this many frames after its frame.
        const int framesInFlight = 2;
        VulkanRenderer renderer = Renderer(SmallMap, impostors: true);
        try
        {
            var parts = new ImpostorParts(renderer);
            MaterialId bright = BrdfTesting.Glow(renderer, Vector3.One);
            MaterialId dim = BrdfTesting.Glow(renderer, new(0.25f));
            for (int frame = 0; frame < 6; frame++)
                renderer.Render(parts.Scene(bright, RenderFlags.None), 1f);
            long before = renderer.Impostors!.Status(parts.Key)!.Value.CapturedFrame;

            for (int frame = 0; frame < framesInFlight + 1; frame++)
                renderer.Render(parts.Scene(dim, RenderFlags.None), 1f);
            Vector3 billboard = Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: parts.Scene(dim, RenderFlags.None),
                    name: "environment-recapture-billboard"
                )
                .Color(Center);
            long after = renderer.Impostors!.Status(parts.Key)!.Value.CapturedFrame;
            Vector3 geometry = Shown(
                    renderer,
                    parts.Scene(dim, RenderFlags.NoImpostor),
                    "environment-recapture-geometry"
                )
                .Color(Center);

            after.Should().BeGreaterThan(before, "the dimmer dome changes the irradiance that the capture baked");
            BrdfTesting
                .LargestChannelDifference(billboard, geometry)
                .Should()
                .BeLessThanOrEqualTo(
                    Tolerance,
                    "the new capture follows the map: {0} against {1}",
                    billboard,
                    geometry
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALitPuffTakesTheMapsMeanRadiance()
    {
        VulkanRenderer renderer = Renderer(SmallMap);
        try
        {
            var albedo = new Vector3(0.8f, 0.6f, 0.4f);
            RenderSnapshot snapshot = Dark();
            AddDome(snapshot, DomePanel(renderer), BrdfTesting.Glow(renderer, new(0.6f, 0.8f, 1)));
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(4f)),
                    material: BrdfTesting.Glow(renderer, Vector3.Zero),
                    position: new(0, 0, -10),
                    scale: Vector3.One,
                    rotation: FacingPlusZ
                )
            );
            var look = new PuffLook(
                Life: 2f,
                StartRadius: 0.5f,
                EndRadius: 0.5f,
                TemperatureRatio: 0.5f,
                Shading: PuffShading.Lit(albedo)
            );
            snapshot.PuffLooks.Add(look);
            var center = new Vector3(0, 0, -6);
            snapshot.Puffs.Add(
                new()
                {
                    Position = center,
                    PreviousPosition = center,
                    Age = 0f,
                    Look = 0,
                }
            );

            Capture lit = Shown(renderer, snapshot, "environment-lit-puff");
            Vector3 mean = renderer.ReadEnvironmentIrradiance(map: 0).MeanRadiance;
            snapshot.PuffLooks[0] = look with { Shading = PuffShading.Glow(albedo * mean) };
            Capture glow = Shown(renderer, snapshot, "environment-glow-puff");

            mean.Should().NotBe(Vector3.Zero, "the dome fills half of the map");
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                BrdfTesting
                    .LargestChannelDifference(lit.Color(x, y), glow.Color(x, y))
                    .Should()
                    .BeLessThanOrEqualTo(
                        Tolerance,
                        "a lit puff scatters the map's mean radiance {0}, but pixel ({1}, {2}) differs",
                        mean,
                        x,
                        y
                    );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ANewRendererHasTheMapOn()
    {
        var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = 160,
                Height = 90,
            }
        );
        try
        {
            if (!renderer.TimesPasses)
                Assert.Skip($"{renderer.DeviceName} writes no timestamps on its graphics queue.");
            for (int frame = 0; frame < 3; frame++)
                renderer.Render(Dark(), 1f);

            renderer
                .LastFrameStats.GpuTimings!.Value.Passes.ToArray()
                .Select(pass => pass.Pass)
                .Should()
                .Contain(["environment faces", "environment prefilter"], "the map is on by default");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheSkyLooksTheSameWithTheMapOnAndOff()
    {
        static Capture Sky(EnvironmentMapSetting map, string name)
        {
            VulkanRenderer renderer = Renderer(map);
            try
            {
                RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 0.8f, 0.5f));
                AddSphere(
                    snapshot: snapshot,
                    mesh: Sphere(renderer),
                    material: Matte(renderer),
                    position: new(0, 0, -5),
                    radius: 1
                );
                return Shown(renderer, snapshot, name);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Capture withMap = Sky(SmallMap, "environment-sky-map");
        Capture withoutMap = Sky(new NoEnvironmentMap(), "environment-sky-hemisphere");

        // The sphere spans asin(1 / 5) around the view axis; a pixel whose ray passes 1° beyond that shows the sky.
        float sphereCosine = MathF.Cos(MathF.Asin(1f / 5) + MathF.PI / 180);
        int skyPixels = 0;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            Double3 ray = BrdfTesting.Ray(new(x, y), Width, Height);
            if (-ray.Z > sphereCosine)
                continue;
            skyPixels++;
            int i = (y * Width + x) * 4;
            (withMap.Rgba[i], withMap.Rgba[i + 1], withMap.Rgba[i + 2])
                .Should()
                .Be((withoutMap.Rgba[i], withoutMap.Rgba[i + 1], withoutMap.Rgba[i + 2]), "sky pixel ({0}, {1})", x, y);
        }

        skyPixels.Should().BeGreaterThan(Width * Height / 2);
    }

    [Fact]
    public void ACurvedMirrorCutByTheScreensEdgeShowsTheStarsMeanInItsFirstAndLastColumnsAndRows()
    {
        // Four mirror ellipsoids 20 m long: tall ones cut by the left and the right edge, and wide ones cut by the top and
        // the bottom edge. The d of each moves by several star cells per pixel across its long axis and by less than one
        // along it. The first column and row have no previous pixel and the last no next one, so their footprint takes
        // the step to the one neighbor there, and the stars there stay at their mean, which a quarter turn of the root
        // axes keeps.
        (float[] still, float[] distance) = RenderMirrorsCutByTheScreensEdges(Quaternion.Identity);
        float[] turned = RenderMirrorsCutByTheScreensEdges(
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)
        ).Hdr;

        int[] edge =
        [
            .. Enumerable.Range(0, Height).SelectMany(y => (int[])[y * Width, y * Width + Width - 1]),
            .. Enumerable.Range(0, Width).SelectMany(x => (int[])[x, (Height - 1) * Width + x]),
        ];
        edge.Where(pixel => distance[pixel] <= 0)
            .Should()
            .BeEmpty("the ellipsoids fill the first and last columns and rows");
        int[] differing =
        [
            .. edge.Where(pixel =>
                Enumerable.Range(4 * pixel, 3).Max(channel => MathF.Abs(turned[channel] - still[channel]))
                > still[4 * pixel + 1] / 1024
            ),
        ];
        differing
            .Should()
            .BeEmpty(
                "the footprint there holds the step to the one neighbor across the long axis; the first pixel that differs is ({0}, {1})",
                differing.FirstOrDefault() % Width,
                differing.FirstOrDefault() / Width
            );
    }

    /// <summary>The four mirror ellipsoids of the screen-edge test under a root turned by <paramref name="root"/>.</summary>
    private static (float[] Hdr, float[] Distance) RenderMirrorsCutByTheScreensEdges(Quaternion root)
    {
        VulkanRenderer renderer = Renderer(SmallMap);
        try
        {
            RenderSnapshot snapshot = Dark();
            TestChains.UnderTurnedRoot(snapshot, root);
            MeshId sphere = Sphere(renderer);
            MaterialId mirror = Metal(renderer, roughness: 0);
            foreach (float side in (ReadOnlySpan<float>)[-1, 1])
            {
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: sphere,
                        material: mirror,
                        position: new(side * 4.1f, 0, -4),
                        scale: new(1, 20, 1)
                    )
                );
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: sphere,
                        material: mirror,
                        position: new(0, side * 2.3f, -4),
                        scale: new(20, 1, 1)
                    )
                );
            }
            renderer.Render(snapshot, 1f);
            return (renderer.ReadHdr(), renderer.ReadDistance());
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The reflected stars (docs/plans/environment-map.md, "The reflected stars"), which render many frames each
    /// (tests/STYLE.md, "Long tests"). The view is 10° high at 320 × 180, so a pixel spans about 9.7 · 10⁻⁴ rad, as at
    /// 1080p and 60°. Hable's curve shows the HDR image, so that each pixel turns back into its radiance.
    /// </summary>
    [Collection(GpuCollection.Name)]
    public sealed class ReflectedStars
    {
        private static readonly float Fov = 10f * MathF.PI / 180;

        /// <summary>p, the angle of one pixel in rad, as PreparedScene.PixelAngle gives it.</summary>
        private static readonly float PixelAngle = 2 * MathF.Tan(Fov / 2) / Height;

        private static VulkanRenderer StarRenderer(float exposureStops, int width = Width) =>
            VulkanRenderer.CreateHeadless(
                new()
                {
                    Validation = true,
                    Width = width,
                    Height = Height,
                    Impostors = false,
                    ToneCurve = ToneCurve.Hable,
                    Exposure = new FixedExposure(exposureStops),
                    Bloom = new NoBloom(),
                    EnvironmentMap = SmallMap,
                }
            );

        /// <summary>A snapshot of the 10° view with no light, whose root frame's axes are turned by <paramref name="root"/>.</summary>
        private static RenderSnapshot OpenSky(Quaternion root)
        {
            RenderSnapshot snapshot = Dark();
            snapshot.Camera.VerticalFov = Fov;
            TestChains.UnderTurnedRoot(snapshot, root);
            return snapshot;
        }

        /// <summary>A flat white metal mirror of roughness 0, 3 m ahead and facing the camera, which fills the view.</summary>
        private static void AddMirror(VulkanRenderer renderer, RenderSnapshot snapshot) =>
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(1f)),
                    material: Metal(renderer, roughness: 0),
                    position: new(0, 0, -3),
                    scale: Vector3.One,
                    rotation: FacingPlusZ
                )
            );

        /// <summary>
        /// The brightest star near the root axes' +Z with no other star within 2.5 cells, so that its reflection stands
        /// alone. A white metal surface has E · c = 1, so the mirror shows the star at b (p / w)².
        /// </summary>
        private static Star LoneBrightStar()
        {
            Star? brightest = null;
            for (int y = -40; y < 40; y++)
            for (int x = -40; x < 40; x++)
            {
                float z = MathF.Floor(
                    MathF.Sqrt(
                        StarField.CellsPerUnit * StarField.CellsPerUnit
                            - (x + 0.5f) * (x + 0.5f)
                            - (y + 0.5f) * (y + 0.5f)
                    )
                );
                var cell = new Vector3(x, y, z);
                if (!StarField.SkyReads(cell) || StarField.OfCell(cell) is not { } star)
                    continue;
                if (brightest is { } best && Luma(best.Peak) >= Luma(star.Peak))
                    continue;
                if (Alone(cell, star))
                    brightest = star;
            }

            return brightest ?? throw new InvalidOperationException("No lone star lies near +Z.");
        }

        private static bool Alone(Vector3 cell, Star star)
        {
            for (int z = -3; z <= 3; z++)
            for (int y = -3; y <= 3; y++)
            for (int x = -3; x <= 3; x++)
            {
                Vector3 other = cell + new Vector3(x, y, z);
                if (other == cell || !StarField.SkyReads(other) || StarField.OfCell(other) is not { } neighbor)
                    continue;
                if (Vector3.Distance(neighbor.Direction, star.Direction) < 2.5f * StarField.CellWidth)
                    return false;
            }

            return true;
        }

        /// <summary>The root rotation that turns <paramref name="star"/> to world +Z, which the mirror's center reflects.</summary>
        private static Quaternion Toward(Star star) => Toward(star.Direction);

        /// <summary>The root rotation that turns the unit <paramref name="root"/> direction of the root axes to world +Z.</summary>
        private static Quaternion Toward(Vector3 root)
        {
            var axis = Vector3.Cross(root, Vector3.UnitZ);
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(root, Vector3.UnitZ), -1, 1));
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
        }

        /// <summary>The pixel at which the mirror shows the world direction <paramref name="direction"/>, which has z above 0.</summary>
        private static Vector2 Reflected(Vector3 direction) =>
            Gpu.Project(point: direction with { Z = -direction.Z }, width: Width, height: Height, fov: Fov);

        private static float Luma(Vector3 color) => 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z;

        /// <summary>The radiance of the HDR image at a pixel, before the exposure of <paramref name="exposureStops"/>.</summary>
        private static Vector3 RadianceAt(Capture image, int x, int y, float exposureStops)
        {
            Vector3 shown = image.Color(x, y);
            double scale = Math.Pow(2, -exposureStops);
            double Channel(float value) =>
                HableCurve.Radiance(BrdfTesting.Decoded((byte)MathF.Round(value * 255))) * scale;
            return new((float)Channel(shown.X), (float)Channel(shown.Y), (float)Channel(shown.Z));
        }

        /// <summary>
        /// Where the mirror shows the brightest light within 16 px of <paramref name="pixel"/>. The default flat normal map
        /// stores 128 for 0, which tilts the mirror by about 0.3°, so the star lands a few pixels from where an exact
        /// mirror shows it.
        /// </summary>
        private static Vector2 BrightestNear(Capture image, Vector2 pixel)
        {
            float brightest = -1;
            Vector2 at = pixel;
            for (int y = -16; y <= 16; y++)
            for (int x = -16; x <= 16; x++)
            {
                var candidate = new Vector2(pixel.X + x, pixel.Y + y);
                if (image.Luma(candidate) > brightest)
                {
                    brightest = image.Luma(candidate);
                    at = candidate;
                }
            }

            return at;
        }

        /// <summary>The blob of a reflected star in a window around a pixel: its light in luma, above the window's edge.</summary>
        private readonly record struct Blob(float Sum, Vector2 Center, float Peak, float HalfPeakWidth);

        private static Blob BlobAround(Capture image, Vector2 pixel, float exposureStops)
        {
            const int radius = 8;
            int cx = (int)MathF.Floor(pixel.X),
                cy = (int)MathF.Floor(pixel.Y);
            float[,] luma = new float[2 * radius + 1, 2 * radius + 1];
            float edge = 0;
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                luma[y + radius, x + radius] = Luma(
                    RadianceAt(image: image, x: cx + x, y: cy + y, exposureStops: exposureStops)
                );
                if (Math.Max(Math.Abs(x), Math.Abs(y)) == radius)
                    edge += luma[y + radius, x + radius] / (16 * radius);
            }

            float sum = 0,
                peak = 0;
            Vector2 weighted = Vector2.Zero;
            (int X, int Y) top = (0, 0);
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                float light = MathF.Max(luma[y + radius, x + radius] - edge, 0);
                sum += light;
                weighted += light * new Vector2(cx + x + 0.5f, cy + y + 0.5f);
                if (light > peak)
                {
                    peak = light;
                    top = (x, y);
                }
            }

            return new(
                Sum: sum,
                Center: weighted / sum,
                Peak: peak,
                HalfPeakWidth: HalfPeakWidth(luma: luma, top: top, edge: edge, peak: peak, radius: radius)
            );
        }

        /// <summary>The width, in pixels, of the row through the peak where the light is above half the peak, interpolated.</summary>
        private static float HalfPeakWidth(float[,] luma, (int X, int Y) top, float edge, float peak, int radius)
        {
            float Light(int x) => luma[top.Y + radius, x + radius] - edge;
            float Crossing(int step)
            {
                int x = top.X;
                while (Math.Abs(x + step) <= radius && Light(x + step) > peak / 2)
                    x += step;
                if (Math.Abs(x + step) > radius)
                    return x;
                float share = (Light(x) - peak / 2) / (Light(x) - Light(x + step));
                return x + step * share;
            }

            return Crossing(1) - Crossing(-1);
        }

        [Fact]
        public void AMirrorReflectsAStarSharpAndSteadyWhileTheCamerasFrameTurns()
        {
            const float stops = 2;
            Star star = LoneBrightStar();
            float width = StarField.LobeWidth(PixelAngle, roughness: 0);
            float expectedPeak = Luma(star.Peak) * PixelAngle * PixelAngle / (width * width);
            Quaternion start = Toward(star);
            VulkanRenderer renderer = StarRenderer(stops);
            try
            {
                var blobs = new List<Blob>();
                var expected = new List<Vector2>();
                Vector2 tilt = Vector2.Zero;
                for (int frame = 0; frame <= 20; frame++)
                {
                    // A fifth of a pixel per frame, about world +Y.
                    var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, frame * PixelAngle / 5);
                    var root = Quaternion.Concatenate(start, turn);
                    RenderSnapshot snapshot = OpenSky(root);
                    AddMirror(renderer, snapshot);
                    Capture image = Gpu.RenderAndCapture(
                        renderer: renderer,
                        snapshot: snapshot,
                        name: "environment-star"
                    );
                    Vector2 pixel = Reflected(Vector3.Transform(star.Direction, root));
                    if (frame == 0)
                        tilt = BrightestNear(image, pixel) - pixel;
                    expected.Add(pixel);
                    blobs.Add(BlobAround(image, pixel + tilt, stops));
                }

                blobs[0].HalfPeakWidth.Should().BeLessThan(8, "the mirror shows the star sharp");
                blobs[0]
                    .Peak.Should()
                    .BeInRange(0.8f * expectedPeak, 1.2f * expectedPeak, "the peak is b (p / w)² times E · c = 1");
                float mean = blobs.Average(blob => blob.Sum);
                for (int frame = 0; frame < blobs.Count; frame++)
                {
                    blobs[frame]
                        .Sum.Should()
                        .BeInRange(0.98f * mean, 1.02f * mean, "frame {0} keeps the star's light", frame);
                    Vector2 moved = blobs[frame].Center - blobs[0].Center;
                    Vector2 starMoved = expected[frame] - expected[0];
                    Vector2
                        .Distance(moved, starMoved)
                        .Should()
                        .BeLessThan(
                            0.2f,
                            "the blob moves with the star in frame {0}: {1} against {2}",
                            frame,
                            moved,
                            starMoved
                        );
                }
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void AMirrorShowsTheSameStarsInTheLastColumnOfAnOddWidthImageAsInsideAWiderOne()
        {
            // 10 stops show M in the middle of the curve, so a footprint that fades the stars into their mean shows.
            const float stops = 10;
            static Capture Mirror(int width)
            {
                VulkanRenderer renderer = StarRenderer(exposureStops: stops, width: width);
                try
                {
                    RenderSnapshot snapshot = OpenSky(Quaternion.Identity);
                    AddMirror(renderer, snapshot);
                    return Gpu.RenderAndCapture(
                        renderer: renderer,
                        snapshot: snapshot,
                        name: $"environment-mirror-{width}"
                    );
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }

            // The last column of the odd image shares its 2 × 2 quads with helper invocations past the image's edge. Two
            // more pixels of width keep the height and the pixel angle and shift the view by one column, so its column
            // Width + 1 shows the same rays inside the image.
            Capture odd = Mirror(Width + 1);
            Capture wider = Mirror(Width + 3);

            for (int y = 0; y < Height; y++)
                BrdfTesting
                    .LargestChannelDifference(odd.Color(Width, y), wider.Color(Width + 1, y))
                    .Should()
                    .BeLessThanOrEqualTo(
                        Tolerance,
                        "the mirror's footprint at the image's edge is the one inside, row {0}",
                        y
                    );
        }

        [Fact]
        public void AWallInFrontOfTheSkyHidesTheStarsInTheMirror()
        {
            Star star = LoneBrightStar();
            Quaternion start = Toward(star);
            // At 0 stops the star shows at about half the range in the mirror, and the GGX tail of the specular map's
            // level 1, which reaches past the wall's edge, stays under half a step of 8 bits.
            VulkanRenderer renderer = StarRenderer(exposureStops: 0);
            try
            {
                Capture WithWall(Quaternion root, string name)
                {
                    RenderSnapshot snapshot = OpenSky(root);
                    AddMirror(renderer, snapshot);
                    // Behind the camera, across every direction that the mirror reflects; no light reaches it.
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: renderer.CreateMesh(Primitives.Plane(100f)),
                            material: BrdfTesting.Glow(renderer, Vector3.Zero),
                            position: new(0, 0, 3),
                            scale: Vector3.One,
                            rotation: FacingMinusZ
                        )
                    );
                    return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
                }

                Capture hidden = WithWall(start, "environment-star-behind-wall");
                Capture elsewhere = WithWall(
                    Quaternion.Concatenate(start, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.05f)),
                    "environment-star-turned-away"
                );

                for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    BrdfTesting
                        .LargestChannelDifference(hidden.Color(x, y), elsewhere.Color(x, y))
                        .Should()
                        .BeLessThanOrEqualTo(
                            1f / 255,
                            "the wall closes the sky that the mirror reflects at ({0}, {1})",
                            x,
                            y
                        );
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        /// <summary>
        /// The radiance of a white metal sphere 4 m ahead under open sky, at <paramref name="radius"/> 2 m by default,
        /// which fills the 10° view. Its curve is gentle, so f is about 2p at its center, and roughness 0 shows single stars.
        /// The <paramref name="root"/> rotation is the identity by default, so the center reflects +Z of the root axes.
        /// </summary>
        private static Vector3[,] Sphere(
            float roughness,
            float exposureStops,
            string name,
            float radius = 2,
            Quaternion? root = null
        )
        {
            VulkanRenderer renderer = StarRenderer(exposureStops);
            try
            {
                RenderSnapshot snapshot = OpenSky(root ?? Quaternion.Identity);
                AddSphere(
                    snapshot: snapshot,
                    mesh: EnvironmentLightingTests.Sphere(renderer),
                    material: Metal(renderer, roughness),
                    position: new(0, 0, -4),
                    radius: radius
                );
                Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
                var radiance = new Vector3[Height, Width];
                for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    radiance[y, x] = RadianceAt(image: image, x: x, y: y, exposureStops: exposureStops);
                return radiance;
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        /// <summary>
        /// The largest share by which a pixel's luma differs from the mean luma of the 5 × 5 pixels around it, over the
        /// pixels within <paramref name="reach"/> of the center, by default the whole image.
        /// </summary>
        private static float LargestLocalDeviation(Vector3[,] radiance, int reach = Width)
        {
            float largest = 0;
            for (int y = Math.Max(2, Height / 2 - reach); y < Math.Min(Height - 2, Height / 2 + reach); y++)
            for (int x = Math.Max(2, Width / 2 - reach); x < Math.Min(Width - 2, Width / 2 + reach); x++)
            {
                float mean = 0;
                for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    mean += Luma(radiance[y + dy, x + dx]) / 25;
                largest = MathF.Max(largest, MathF.Abs(Luma(radiance[y, x]) - mean) / mean);
            }

            return largest;
        }

        [Fact]
        public void ARoughSurfaceShowsNoSingleStars()
        {
            // 10 stops show M, about 4 · 10⁻⁴, in the middle of the curve, where 8 bits resolve it to well under 1 %.
            const float stops = 10;
            Vector3[,] rough = Sphere(roughness: 0.5f, exposureStops: stops, name: "environment-rough-stars");
            Vector3[,] mirror = Sphere(roughness: 0, exposureStops: stops, name: "environment-mirror-stars");

            float center = 0;
            for (int y = -10; y <= 10; y++)
            for (int x = -10; x <= 10; x++)
                center += Luma(rough[Height / 2 + y, Width / 2 + x]) / (21 * 21);
            // The center reflects about +Z in root axes, under open sky.
            float expected = Luma(StarField.MeanRadiance(Vector3.UnitZ, PixelAngle));

            LargestLocalDeviation(rough)
                .Should()
                .BeLessThanOrEqualTo(0.02f, "the rough lobe shows the stars' mean alone");
            center.Should().BeInRange(0.9f * expected, 1.1f * expected, "the mean is E · c · M with E · c = 1");
            LargestLocalDeviation(mirror)
                .Should()
                .BeGreaterThan(0.02f, "the mirror shows single stars, so the rule can fail");
        }

        [Fact]
        public void ACurvedMirrorShowsTheStarsMeanWhereAPixelSpansMoreThanACell()
        {
            // A sphere of radius 0.25 m, 4 m ahead: d moves by about 30 p per pixel at its center, 4.4 cells, so its
            // footprint fades the stars into their mean even at roughness 0. The center reflects the diagonal of the root
            // axes, where M = 450 |ω|₁ ... has no crease, so the mean changes evenly between pixels.
            Vector3[,] curved = Sphere(
                roughness: 0,
                exposureStops: 10,
                name: "environment-curved-mirror-stars",
                radius: 0.25f,
                root: Toward(Vector3.Normalize(Vector3.One))
            );

            // Within 8 px of the center, d stays within 14° of the diagonal, away from the creases of |ω|₁ at 35°.
            LargestLocalDeviation(curved, reach: 8)
                .Should()
                .BeLessThanOrEqualTo(
                    0.02f,
                    "a pixel that covers more than a star cell of the sky holds the stars' mean"
                );
        }
    }
}
