using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The painted atmosphere on screen (docs/plans/atmosphere.md): the sky behind the ground, the haze over far surfaces and
/// the tint of the sun's light, in the main view and in the environment faces. The camera is at the render origin with
/// identity rotation, so it looks down −Z with +Y up, and it stands on the surface of a Mars-like planet whose center lies
/// below it. The exposure is fixed at 0 stops.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class AtmosphereRenderTests
{
    private const int Width = 320,
        Height = 180;

    private const float MarsRadius = 3_389_500;

    /// <summary>The largest difference of one channel between two shown colors that must agree.</summary>
    private const float Tolerance = 3f / 255;

    private static readonly SceneEnvironmentMap SmallMap = SceneEnvironmentMap.Default with { FaceSize = 32 };

    /// <summary>A Mars-like air: a butterscotch sky, and neither a haze nor a tint.</summary>
    private static readonly PlanetMedium SkyOnly = new(
        Radius: MarsRadius,
        SurfaceDensity: 0.02f,
        ScaleHeight: 11_100,
        Drag: 2,
        SkyRadiance: new(0.6f, 0.4f, 0.25f)
    );

    /// <summary>A Mars-like air with a black sky and an orange haze that lets through 1/e of the light per kilometer.</summary>
    private static readonly PlanetMedium HazeOnly = SkyOnly with
    {
        SkyRadiance = Vector3.Zero,
        Haze = new(new(0.6f, 0.3f, 0.1f), Distance: 1000),
    };

    /// <summary>A Mars-like air with a black sky and a tint that keeps half the green and a quarter of the blue.</summary>
    private static readonly PlanetMedium TintOnly = SkyOnly with
    {
        SkyRadiance = Vector3.Zero,
        SunTint = new(1, 0.5f, 0.25f),
    };

    /// <summary>A square of the plane primitive, which faces +Y, turned to face +Z, toward the camera from ahead of it.</summary>
    private static readonly Quaternion FacingPlusZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>A square turned to face −Z, toward the camera from behind it.</summary>
    private static readonly Quaternion FacingMinusZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2);

    /// <summary>A pixel near the top of the view, where the sky shows above the ground.</summary>
    private static readonly Vector2 SkyPixel = new(Width / 2, 8);

    private static readonly Vector2 Center = new(Width / 2, Height / 2);

    private static VulkanRenderer Renderer(EnvironmentMapSetting? map = null) =>
        Gpu.Headless(width: Width, height: Height, environmentMap: map);

    /// <summary>A snapshot whose sun, its first light, shines from <paramref name="sunDirection"/>, with no hemisphere light.</summary>
    private static RenderSnapshot Scene(Vector3 sunDirection)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(sunDirection);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    /// <summary>Gives <paramref name="snapshot"/> the air of <paramref name="medium"/>, with the eye <paramref name="height"/> m above its surface and the first light as its sun.</summary>
    private static void Air(RenderSnapshot snapshot, PlanetMedium medium, float height = 0)
    {
        var center = new Vector3(0, -((float)medium.Radius + height), 0);
        snapshot.Atmosphere = new(Center: center, PreviousCenter: center, Medium: medium, Sun: 0);
    }

    /// <summary>A gray matte ground 1.7 m below the eye, 10 km wide.</summary>
    private static void AddGround(VulkanRenderer renderer, RenderSnapshot snapshot) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(5000f)),
                material: renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.4f, y: 0.3f, z: 0.2f, w: 1), roughness: 1)
                ),
                position: new(0, -1.7f, 0),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );

    /// <summary>A white glowing square of <paramref name="halfSize"/> at <paramref name="position"/>, turned by <paramref name="rotation"/>.</summary>
    private static void AddGlow(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        Vector3 position,
        float halfSize,
        Quaternion rotation
    ) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(halfSize)),
                material: BrdfTesting.Glow(renderer, new(0.5f)),
                position: position,
                scale: Vector3.One,
                rotation: rotation,
                chainDepth: TestChains.CamerasFrame
            )
        );

    /// <summary>Renders <paramref name="snapshot"/> twice, so that the environment map holds this frame's faces, and captures the second draw.</summary>
    private static Capture Shown(VulkanRenderer renderer, RenderSnapshot snapshot, string name)
    {
        renderer.Render(snapshot, 1f);
        return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
    }

    /// <summary>
    /// Builds a scene whose sun shines from <paramref name="sunDirection"/> with a fresh renderer without the environment
    /// map, shows it, and disposes the renderer.
    /// </summary>
    private static Capture Show(string name, Vector3 sunDirection, Action<VulkanRenderer, RenderSnapshot> build) =>
        ShowWith(map: new NoEnvironmentMap(), name: name, sunDirection: sunDirection, build: build);

    /// <summary>As <see cref="Show"/>, with the environment map on, so that the ambient light and the mirrors read its faces.</summary>
    private static Capture ShowWithMap(
        string name,
        Vector3 sunDirection,
        Action<VulkanRenderer, RenderSnapshot> build
    ) => ShowWith(map: SmallMap, name: name, sunDirection: sunDirection, build: build);

    private static Capture ShowWith(
        EnvironmentMapSetting map,
        string name,
        Vector3 sunDirection,
        Action<VulkanRenderer, RenderSnapshot> build
    )
    {
        VulkanRenderer renderer = Renderer(map);
        try
        {
            RenderSnapshot snapshot = Scene(sunDirection);
            build(renderer, snapshot);
            return Shown(renderer, snapshot, name);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static void ExpectSame(Vector3 actual, Vector3 expected, string because) =>
        Vector3
            .Abs(actual - expected)
            .Should()
            .Match<Vector3>(
                d => d.X <= Tolerance && d.Y <= Tolerance && d.Z <= Tolerance,
                "{0}: {1} against {2}",
                because,
                actual,
                expected
            );

    private static readonly Vector3 HighSun = new(0.3f, 1, 0.4f);

    // ---- The sky --------------------------------------------------------------------------------------------------

    [Fact]
    public void APaintedSkyShowsItsColorBehindTheGroundByDay()
    {
        Vector3 sky = Show(
                "atmosphere-sky-day",
                HighSun,
                (renderer, snapshot) =>
                {
                    AddGround(renderer, snapshot);
                    Air(snapshot, SkyOnly);
                }
            )
            .Color(SkyPixel);

        sky.X.Should().BeGreaterThan(0.3f, "the butterscotch sky is bright by day, but it shows {0}", sky);
        sky.X.Should().BeGreaterThan(sky.Y, "the sky is butterscotch: more red than green");
        sky.Y.Should().BeGreaterThan(sky.Z, "the sky is butterscotch: more green than blue");
    }

    [Fact]
    public void TheSkyIsBlackAtNight()
    {
        Vector3 sky = Show(
                "atmosphere-sky-night",
                new(0.3f, -1, 0.4f),
                (renderer, snapshot) =>
                {
                    AddGround(renderer, snapshot);
                    Air(snapshot, SkyOnly);
                }
            )
            .Color(SkyPixel);

        sky.Length().Should().BeLessThan(2f / 255, "the sun stands below the horizon, but the sky shows {0}", sky);
    }

    [Fact]
    public void TheSkyIsBlackAboveTheAir()
    {
        Vector3 sky = Show("atmosphere-sky-above", HighSun, (_, snapshot) => Air(snapshot, SkyOnly, height: 300_000))
            .Color(SkyPixel);

        sky.Length().Should().BeLessThan(2f / 255, "300 km up, the air is gone, but the sky shows {0}", sky);
    }

    // ---- The haze -------------------------------------------------------------------------------------------------

    /// <summary>A near square 10 m ahead, on the left, and a far square 3 km ahead, on the right, of one angular size.</summary>
    private static void AddNearAndFar(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        AddGlow(
            renderer: renderer,
            snapshot: snapshot,
            position: new(-3, 0, -10),
            halfSize: 1.5f,
            rotation: FacingPlusZ
        );
        AddGlow(
            renderer: renderer,
            snapshot: snapshot,
            position: new(900, 0, -3000),
            halfSize: 450,
            rotation: FacingPlusZ
        );
    }

    private static readonly Vector2 NearPixel = Gpu.Project(point: new(-3, 0, -10), width: Width, height: Height);
    private static readonly Vector2 FarPixel = Gpu.Project(point: new(900, 0, -3000), width: Width, height: Height);

    /// <summary>How far a shown color leans to red: its red channel less its blue.</summary>
    private static float Warmth(Vector3 color) => color.X - color.Z;

    [Fact]
    public void HazeGrowsWithDistance()
    {
        Capture image = Show(
            "atmosphere-haze",
            HighSun,
            (renderer, snapshot) =>
            {
                AddNearAndFar(renderer, snapshot);
                Air(snapshot, HazeOnly);
            }
        );
        Vector3 near = image.Color(NearPixel),
            far = image.Color(FarPixel);

        Warmth(near).Should().BeLessThan(0.05f, "10 m of air hardly tints a white square, but it shows {0}", near);
        Warmth(far)
            .Should()
            .BeGreaterThan(0.2f, "3 km of air hides the white square in orange haze, but it shows {0}", far);
    }

    [Fact]
    public void APlanetThatSetsNoHazeLeavesAFarSurfaceAsItIs()
    {
        Vector3 withAir = Show(
                "atmosphere-no-haze",
                HighSun,
                (renderer, snapshot) =>
                {
                    AddNearAndFar(renderer, snapshot);
                    Air(snapshot, SkyOnly with { SkyRadiance = Vector3.Zero });
                }
            )
            .Color(FarPixel);
        Vector3 withoutAir = Show("atmosphere-no-air", HighSun, AddNearAndFar).Color(FarPixel);

        ExpectSame(withAir, withoutAir, "an air without a haze lets a far surface through unchanged");
    }

    [Fact]
    public void HazeSendsNoLightAtNight()
    {
        Vector3 dusk = Show(
                "atmosphere-haze-night",
                new(0.3f, -1, 0.4f),
                (renderer, snapshot) =>
                {
                    AddNearAndFar(renderer, snapshot);
                    Air(snapshot, HazeOnly);
                }
            )
            .Color(FarPixel);

        Warmth(dusk)
            .Should()
            .BeLessThan(
                0.02f,
                "at night the haze sends no light, so it only dims the white square, but it shows {0}",
                dusk
            );
    }

    /// <summary>
    /// <see cref="HazeOnly"/> on a planet 1 km in radius, so that its horizon bends within the view: the far square's up,
    /// away from the center, leans 72° from the eye's.
    /// </summary>
    private static readonly PlanetMedium SmallHazyPlanet = HazeOnly with { Radius = 1000 };

    [Fact]
    public void ASurfaceOnTheNightSideTakesNoHazeLightWhileTheEyeIsInDaylight()
    {
        // The sun stands 11° above the eye's horizon, from −X, and 12° below the far square's.
        Vector3 far = Show(
                "atmosphere-haze-night-side",
                new(-1, 0.2f, 0),
                (renderer, snapshot) =>
                {
                    AddNearAndFar(renderer, snapshot);
                    Air(snapshot, SmallHazyPlanet);
                }
            )
            .Color(FarPixel);

        Warmth(far)
            .Should()
            .BeLessThan(
                0.02f,
                "night has fallen at the far square, so the haze there only dims it, but it shows {0}",
                far
            );
    }

    [Fact]
    public void ASurfaceOnTheDaySideKeepsItsHazeLightWhileTheEyeIsAtNight()
    {
        // The sun stands 6° below the eye's horizon, from +X, and 14° above the far square's.
        Vector3 far = Show(
                "atmosphere-haze-day-side",
                new(1, -0.1f, 0),
                (renderer, snapshot) =>
                {
                    AddNearAndFar(renderer, snapshot);
                    Air(snapshot, SmallHazyPlanet);
                }
            )
            .Color(FarPixel);

        Warmth(far)
            .Should()
            .BeGreaterThan(0.2f, "the day goes on at the far square, so its haze glows orange, but it shows {0}", far);
    }

    // ---- The haze over glass, fringes and puffs -------------------------------------------------------------------

    /// <summary>The HDR light, pre-exposed at 0 stops, at <paramref name="pixel"/> of one frame of the scene that <paramref name="build"/> makes, without the environment map.</summary>
    private static Vector3 HdrAt(Vector2 pixel, Action<VulkanRenderer, RenderSnapshot> build)
    {
        VulkanRenderer renderer = Renderer(new NoEnvironmentMap());
        try
        {
            RenderSnapshot snapshot = Scene(HighSun);
            build(renderer, snapshot);
            renderer.Render(snapshot, 1f);
            float[] hdr = renderer.ReadHdr();
            int i = ((int)pixel.Y * Width + (int)pixel.X) * 4;
            return new(hdr[i], hdr[i + 1], hdr[i + 2]);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Asserts that each channel of two HDR colors agrees within the rounding of half floats.</summary>
    private static void ExpectSameLight(Vector3 actual, Vector3 expected, string because)
    {
        float tolerance = 4f / 1024 * MathF.Max(MathF.Max(expected.X, expected.Y), MathF.Max(expected.Z, 1e-3f));
        var difference = Vector3.Abs(actual - expected);
        MathF
            .Max(difference.X, MathF.Max(difference.Y, difference.Z))
            .Should()
            .BeLessThanOrEqualTo(tolerance, "{0}: {1} against {2}", because, actual, expected);
    }

    /// <summary>A black card of reflectance 0 that emits <paramref name="radiance"/>, of base alpha <paramref name="alpha"/> under <paramref name="mode"/>.</summary>
    private static MaterialId Card(VulkanRenderer renderer, Vector3 radiance, float alpha, AlphaMode mode) =>
        renderer.CreateMaterial(
            new(
                BaseColor: new(x: 0, y: 0, z: 0, w: alpha),
                Roughness: 0.5f,
                Reflectance: 0,
                Emissive: radiance,
                Alpha: mode
            )
        );

    /// <summary>Adds a square of <paramref name="material"/> 3 km ahead, where the far square of <see cref="AddNearAndFar"/> stands.</summary>
    private static void AddFarCard(VulkanRenderer renderer, RenderSnapshot snapshot, MaterialId material) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(450)),
                material: material,
                position: new(900, 0, -3000),
                scale: Vector3.One,
                rotation: FacingPlusZ,
                chainDepth: TestChains.CamerasFrame
            )
        );

    [Fact]
    public void AClearPaneLeavesTheHazedSurfaceBehindItAsTheHazeAloneShowsIt()
    {
        Vector3 alone = HdrAt(
            FarPixel,
            (renderer, snapshot) =>
            {
                AddNearAndFar(renderer, snapshot);
                Air(snapshot, HazeOnly);
            }
        );
        Vector3 behindPane = HdrAt(
            FarPixel,
            (renderer, snapshot) =>
            {
                AddNearAndFar(renderer, snapshot);
                // A pane that neither reflects nor scatters, 2.5 km ahead on the ray to the far square.
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: renderer.CreateMesh(Primitives.Plane(400)),
                        material: Card(renderer: renderer, radiance: Vector3.Zero, alpha: 0, mode: new GlassAlpha()),
                        position: new(750, 0, -2500),
                        scale: Vector3.One,
                        rotation: FacingPlusZ,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                Air(snapshot, HazeOnly);
            }
        );

        Warmth(alone).Should().BeGreaterThan(0.2f, "3 km of haze hides the far square, as {0}", alone);
        ExpectSameLight(behindPane, alone, "a clear pane adds no haze of its own to the haze over the whole ray");
    }

    [Fact]
    public void AnOpaquePaneFadesIntoTheHazeAsAnOpaqueSurfaceDoes()
    {
        var glow = new Vector3(0.5f);
        Vector3 opaque = HdrAt(
            FarPixel,
            (renderer, snapshot) =>
            {
                AddFarCard(
                    renderer,
                    snapshot,
                    Card(renderer: renderer, radiance: glow, alpha: 1, mode: new OpaqueAlpha())
                );
                Air(snapshot, HazeOnly);
            }
        );
        Vector3 pane = HdrAt(
            FarPixel,
            (renderer, snapshot) =>
            {
                AddFarCard(
                    renderer,
                    snapshot,
                    Card(renderer: renderer, radiance: glow, alpha: 1, mode: new GlassAlpha())
                );
                Air(snapshot, HazeOnly);
            }
        );

        Warmth(opaque).Should().BeGreaterThan(0.2f, "3 km of haze hides the opaque square, as {0}", opaque);
        ExpectSameLight(
            pane,
            opaque,
            "glass that lets nothing through hides the haze's light behind it as a wall does"
        );
    }

    [Fact]
    public void AFringeFadesIntoTheHazeByTheShareThatItCovers()
    {
        const float coverage = 0.4f;
        var glow = new Vector3(0.5f);
        Vector3 opaque = HdrAt(
            FarPixel,
            (renderer, snapshot) =>
            {
                AddFarCard(
                    renderer,
                    snapshot,
                    Card(renderer: renderer, radiance: glow, alpha: 1, mode: new OpaqueAlpha())
                );
                Air(snapshot, HazeOnly);
            }
        );
        // Every texel's alpha lies below the cutoff, so the whole card is fringe, over the black sky.
        Vector3 fringe = HdrAt(
            FarPixel,
            (renderer, snapshot) =>
            {
                AddFarCard(
                    renderer,
                    snapshot,
                    Card(renderer: renderer, radiance: glow, alpha: coverage, mode: new FringedAlpha(0.5f))
                );
                Air(snapshot, HazeOnly);
            }
        );

        ExpectSameLight(fringe, coverage * opaque, "the fringe stands in for its share of the hazed surface");
    }

    [Fact]
    public void AGlowPuffDimsInTheHazeAndTakesNoneOfItsColor()
    {
        static void AddFarPuff(RenderSnapshot snapshot)
        {
            snapshot.PuffLooks.Add(
                new(
                    Life: 2f,
                    StartRadius: 300f,
                    EndRadius: 300f,
                    TemperatureRatio: 0.5f,
                    Shading: PuffShading.Glow(new(0.5f))
                )
            );
            var position = new Vector3(900, 0, -3000);
            snapshot.Puffs.Add(
                new()
                {
                    Position = position,
                    PreviousPosition = position,
                    Age = 0f,
                    Look = 0,
                }
            );
        }

        Vector3 inVacuum = HdrAt(FarPixel, (_, snapshot) => AddFarPuff(snapshot));
        Vector3 inHaze = HdrAt(
            FarPixel,
            (_, snapshot) =>
            {
                AddFarPuff(snapshot);
                Air(snapshot, HazeOnly);
            }
        );

        inVacuum.X.Should().BeGreaterThan(0.05f, "the puff glows at the pixel, as {0}", inVacuum);
        inHaze.X.Should().BeLessThan(0.2f * inVacuum.X, "3 km of haze lets through about 5% of the puff's light");
        Warmth(inHaze)
            .Should()
            .BeLessThan(0.002f, "a puff hides nothing, so it shows none of the haze's light: {0}", inHaze);
    }

    // ---- The haze in the screen-space reflections ----------------------------------------------------------------

    [Fact]
    public void AScreenSpaceReflectionShowsAFarSurfaceThroughTheHazeAsTheEnvironmentMapDoes()
    {
        static Vector3 Reflection(ReflectionSetting reflections)
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: Width,
                height: Height,
                environmentMap: SmallMap,
                reflections: reflections
            );
            try
            {
                RenderSnapshot snapshot = Scene(HighSun);
                snapshot.Instances.Add(ReflectionTesting.Floor(renderer, ReflectionTesting.Mirror()));
                // A wide glowing wall 3 km ahead, which the floor mirrors on screen.
                AddGlow(
                    renderer: renderer,
                    snapshot: snapshot,
                    position: new(0, 200, -3000),
                    halfSize: 1500,
                    rotation: FacingPlusZ
                );
                Air(snapshot, HazeOnly);
                // The floor pixel that mirrors the wall's point 100 m up, 44 m ahead on the floor.
                Vector2 mirrored = Gpu.Project(
                    point: new(0, 2 * ReflectionTesting.FloorHeight - 100, -3000),
                    width: Width,
                    height: Height
                );
                return Shown(renderer, snapshot, "atmosphere-reflection-haze").Color(mirrored);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Vector3 traced = Reflection(ReflectionTesting.Reflections);
        Vector3 mapped = Reflection(new NoReflections());

        Warmth(mapped)
            .Should()
            .BeGreaterThan(0.2f, "the map's face sees the wall through 3 km of haze, as {0}", mapped);
        Vector3
            .Abs(traced - mapped)
            .Should()
            .Match<Vector3>(
                d => d.X <= 0.05f && d.Y <= 0.05f && d.Z <= 0.05f,
                "the ray's hit shows the wall through the haze as the map does: {0} against {1}",
                traced,
                mapped
            );
    }

    // ---- The sun's tint -------------------------------------------------------------------------------------------

    /// <summary>A white matte square 5 m ahead that faces the camera, lit by the sun from behind the camera.</summary>
    private static void AddLitSquare(VulkanRenderer renderer, RenderSnapshot snapshot) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(2f)),
                material: renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 1)
                ),
                position: new(0, 0, -5),
                scale: Vector3.One,
                rotation: FacingPlusZ,
                chainDepth: TestChains.CamerasFrame
            )
        );

    private static readonly Vector3 SunBehind = new(0.2f, 0.5f, 1);

    [Fact]
    public void TheSunsLightTakesTheTintInsideTheAir()
    {
        Vector3 tinted = Show(
                "atmosphere-tint-surface",
                SunBehind,
                (renderer, snapshot) =>
                {
                    AddLitSquare(renderer, snapshot);
                    Air(snapshot, TintOnly);
                }
            )
            .Color(Center);
        Vector3 plain = Show("atmosphere-tint-none", SunBehind, AddLitSquare).Color(Center);

        (plain.X - plain.Z).Should().BeLessThan(0.1f, "the test sun is nearly white, and shows {0}", plain);
        Warmth(tinted)
            .Should()
            .BeGreaterThan(0.2f, "the air keeps a quarter of the sun's blue, but the square shows {0}", tinted);
    }

    [Fact]
    public void TheSunsLightTakesNoTintAboveTheAir()
    {
        Vector3 inOrbit = Show(
                "atmosphere-tint-orbit",
                SunBehind,
                (renderer, snapshot) =>
                {
                    AddLitSquare(renderer, snapshot);
                    Air(snapshot, TintOnly, height: 500_000);
                }
            )
            .Color(Center);
        Vector3 plain = Show("atmosphere-tint-plain", SunBehind, AddLitSquare).Color(Center);

        ExpectSame(inOrbit, plain, "500 km up, the sun's light crosses no air before the square");
    }

    // ---- The environment faces ------------------------------------------------------------------------------------

    /// <summary>A white mirror sphere of radius 1, 4 m ahead.</summary>
    private static void AddMirror(VulkanRenderer renderer, RenderSnapshot snapshot) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Sphere(1f, 64, 32)),
                material: renderer.CreateMaterial(
                    new(BaseColor: Vector4.One, Roughness: 0, Model: new StandardShading(Metallic: 1))
                ),
                position: new(0, 0, -4),
                scale: Vector3.One,
                chainDepth: TestChains.CamerasFrame
            )
        );

    [Fact]
    public void AMirrorReflectsThePaintedSkyThroughTheEnvironmentMap()
    {
        Capture image = ShowWithMap(
            "atmosphere-mirror-sky",
            HighSun,
            (renderer, snapshot) =>
            {
                AddMirror(renderer, snapshot);
                Air(snapshot, SkyOnly);
            }
        );

        // The sphere's center reflects the sky behind the camera, with the map's own rounding.
        ExpectSame(image.Color(Center), image.Color(SkyPixel), "a mirror shows the sky that the eye sees");
    }

    [Fact]
    public void AMirrorReflectsTheHazeOverAFarSurfaceAsTheEyeSeesIt()
    {
        // A wide glowing square 3 km behind the camera, which the mirror's center reflects.
        Vector3 reflected = ShowWithMap(
                "atmosphere-mirror-haze",
                HighSun,
                (renderer, snapshot) =>
                {
                    AddMirror(renderer, snapshot);
                    AddGlow(
                        renderer: renderer,
                        snapshot: snapshot,
                        position: new(0, 0, 3000),
                        halfSize: 1500,
                        rotation: FacingMinusZ
                    );
                    Air(snapshot, HazeOnly);
                }
            )
            .Color(Center);
        Vector3 direct = ShowWithMap(
                "atmosphere-direct-haze",
                HighSun,
                (renderer, snapshot) =>
                {
                    snapshot.Camera.Rotation = snapshot.Camera.PreviousRotation = Quaternion.CreateFromAxisAngle(
                        Vector3.UnitY,
                        MathF.PI
                    );
                    AddGlow(
                        renderer: renderer,
                        snapshot: snapshot,
                        position: new(0, 0, 3000),
                        halfSize: 1500,
                        rotation: FacingMinusZ
                    );
                    Air(snapshot, HazeOnly);
                }
            )
            .Color(Center);

        Warmth(direct).Should().BeGreaterThan(0.2f, "the eye sees the far square through 3 km of haze, as {0}", direct);
        ExpectSame(reflected, direct, "the map's face hazes the square by the same rule as the main view");
    }
}
