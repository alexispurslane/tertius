using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// What the batcher makes of a snapshot's atmosphere record (docs/plans/atmosphere.md, "The model"): the sky, the haze's
/// light, the sun's tint, the sun's place and the eye's air in the frame constants, at the interpolated eye. The sky is the
/// painted sky times the day factor and the density share at the eye. The haze's light is the painted haze, which the
/// shaders dim by the day factor at each hazed point. Only the sun's GPU light takes the tint. The camera is at the render
/// origin, and the planet's center lies below it.
/// </summary>
public sealed class AtmosphereTests
{
    private const float MarsRadius = 3_389_500;
    private const float ScaleHeight = 11_100;

    private static readonly Vector3 PaintedSky = new(0.6f, 0.4f, 0.25f);
    private static readonly Vector3 PaintedHaze = new(0.5f, 0.35f, 0.2f);
    private static readonly Vector3 Tint = new(1, 0.85f, 0.7f);

    private static readonly PlanetMedium MarsAir = new(
        Radius: MarsRadius,
        SurfaceDensity: 0.02f,
        ScaleHeight: ScaleHeight,
        Drag: 2,
        SkyRadiance: PaintedSky,
        Haze: new(PaintedHaze, Distance: 20_000),
        SunTint: Tint
    );

    /// <summary>A snapshot whose test star, its first light, shines from <paramref name="sunDirection"/>.</summary>
    private static RenderSnapshot WithSun(Vector3 sunDirection) => Gpu.Snapshot(sunDirection);

    /// <summary>Gives <paramref name="snapshot"/> the air of <paramref name="medium"/>, with the eye <paramref name="height"/> m above its surface and the first light as its sun.</summary>
    private static void Air(RenderSnapshot snapshot, PlanetMedium medium, float height = 0)
    {
        var center = new Vector3(0, -(MarsRadius + height), 0);
        snapshot.Atmosphere = new(Center: center, PreviousCenter: center, Medium: medium, Sun: 0);
    }

    private static PreparedScene Prepare(RenderSnapshot snapshot, float alpha = 1f, PreparedScene? scene = null)
    {
        scene ??= new PreparedScene();
        Batchers
            .Default()
            .Prepare(
                snapshot: snapshot,
                alpha: alpha,
                highestStops: Batchers.HighestStops,
                width: 1600,
                height: 900,
                meshes: [],
                materials: MaterialSlots.Of(1),
                environment: new WithoutMap(),
                region: DeformRegion.None,
                scene: scene
            );
        return scene;
    }

    private static GpuAtmosphere AtmosphereOf(RenderSnapshot snapshot) => Prepare(snapshot).Constants.Atmosphere;

    private static Vector3 Rgb(Vector4 value) => new(value.X, value.Y, value.Z);

    private static void ExpectNear(Vector3 actual, Vector3 expected, string because) =>
        Vector3.Distance(actual, expected).Should().BeLessThan(1e-4f, "{0}, but it is {1}", because, actual);

    [Fact]
    public void WithoutAnAtmosphereTheSkyIsBlackTheHazeLetsEverythingThroughAndTheTintIsWhite()
    {
        GpuAtmosphere atmosphere = AtmosphereOf(WithSun(Vector3.UnitY));

        atmosphere.Sky.Should().Be(Vector4.Zero);
        atmosphere.Haze.Should().Be(Vector4.Zero, "a haze distance of 1/0 lets everything through");
        Rgb(atmosphere.SunTint).Should().Be(Vector3.One);
    }

    [Fact]
    public void TheSkyIsThePaintedSkyAtTheSurfaceWhenTheSunStandsHigh()
    {
        RenderSnapshot snapshot = WithSun(new(0.3f, 1, -0.4f));
        Air(snapshot, MarsAir);

        ExpectNear(Rgb(AtmosphereOf(snapshot).Sky), PaintedSky, "the full day at the surface shows the painted sky");
    }

    [Fact]
    public void TheSkyIsBlackAtNight()
    {
        RenderSnapshot snapshot = WithSun(new(0.2f, -1, 0.3f));
        Air(snapshot, MarsAir);

        Rgb(AtmosphereOf(snapshot).Sky).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void TheSkyIsHalfItsDayValueWhenTheSunStandsOnTheHorizon()
    {
        RenderSnapshot snapshot = WithSun(new(1, 0, -0.2f));
        Air(snapshot, MarsAir);

        ExpectNear(Rgb(AtmosphereOf(snapshot).Sky), PaintedSky / 2, "the sun on the horizon is the middle of the dusk");
    }

    [Fact]
    public void TheSkyDarkensAsTheSunSinksAndBrightensAsItRises()
    {
        float[] elevations = [-0.1f, -0.05f, 0, 0.05f, 0.1f];
        float[] reds =
        [
            .. elevations.Select(elevation =>
            {
                RenderSnapshot snapshot = WithSun(new(MathF.Cos(elevation), MathF.Sin(elevation), 0));
                Air(snapshot, MarsAir);
                return AtmosphereOf(snapshot).Sky.X;
            }),
        ];

        reds.Should()
            .BeInAscendingOrder()
            .And.OnlyHaveUniqueItems("the dusk fades the sky smoothly across the horizon");
        reds[0].Should().BeGreaterThan(0, "the sky keeps a glow at 0.1 rad, about 6°, below the horizon");
        reds[^1].Should().BeLessThan(PaintedSky.X, "the sky is not yet full at 0.1 rad above the horizon");
    }

    [Fact]
    public void TheSkyFadesWithTheDensityOfTheAirAtTheEye()
    {
        RenderSnapshot snapshot = WithSun(new(0.3f, 1, -0.4f));
        Air(snapshot, MarsAir, height: ScaleHeight);

        ExpectNear(
            Rgb(AtmosphereOf(snapshot).Sky),
            PaintedSky / MathF.E,
            "one scale height up, the air is 1/e as dense"
        );
    }

    [Fact]
    public void TheSkyIsBlackFarAboveTheAir()
    {
        RenderSnapshot snapshot = WithSun(new(0.3f, 1, -0.4f));
        Air(snapshot, MarsAir, height: 300_000);

        Rgb(AtmosphereOf(snapshot).Sky).Length().Should().BeLessThan(1e-9f, "27 scale heights up, the air is gone");
    }

    [Fact]
    public void APlanetWithoutASunHasNight()
    {
        RenderSnapshot snapshot = WithSun(new(0.3f, 1, -0.4f));
        Air(snapshot, MarsAir);
        snapshot.Atmosphere = snapshot.Atmosphere!.Value with { Sun = null };
        GpuAtmosphere atmosphere = AtmosphereOf(snapshot);

        Rgb(atmosphere.Sky).Should().Be(Vector3.Zero);
        atmosphere.Sun.Should().Be(Vector4.Zero, "the shaders find no sun, so the haze sends no light either");
    }

    [Fact]
    public void ASunThatNamesASpotLightGivesNight()
    {
        RenderSnapshot snapshot = WithSun(new(0.3f, 1, -0.4f));
        Air(snapshot, MarsAir);
        snapshot.Lights.Add(
            Gpu.Spot(
                position: new(0, 10, 0),
                direction: -Vector3.UnitY,
                color: Vector3.One,
                intensity: 100,
                innerConeAngle: 0.3f,
                outerConeAngle: 0.5f,
                chainDepth: 0
            )
        );
        snapshot.Atmosphere = snapshot.Atmosphere!.Value with { Sun = snapshot.Lights.Count - 1 };
        GpuAtmosphere atmosphere = AtmosphereOf(snapshot);

        Rgb(atmosphere.Sky)
            .Should()
            .Be(Vector3.Zero, "a spot light lights only its cone, so it is never a planet's sun");
        atmosphere.Sun.Should().Be(Vector4.Zero);
    }

    [Fact]
    public void ASunPastTheSnapshotsLightsGivesNight()
    {
        RenderSnapshot snapshot = WithSun(new(0.3f, 1, -0.4f));
        Air(snapshot, MarsAir);
        snapshot.Atmosphere = snapshot.Atmosphere!.Value with { Sun = snapshot.Lights.Count };
        GpuAtmosphere atmosphere = AtmosphereOf(snapshot);

        Rgb(atmosphere.Sky).Should().Be(Vector3.Zero, "the record names no light of the snapshot");
        atmosphere.Sun.Should().Be(Vector4.Zero);
    }

    [Fact]
    public void AnEyeAtThePlanetsCenterCountsTheSunAsOverhead()
    {
        // The sun shines from below the eye, which is night for an eye on the surface above the center.
        RenderSnapshot snapshot = WithSun(new(0.2f, -1, 0.3f));
        Air(snapshot, MarsAir, height: -MarsRadius);

        ExpectNear(
            Rgb(AtmosphereOf(snapshot).Sky),
            PaintedSky,
            "the center has no horizon, so the sun stands overhead and the air is the surface's"
        );
    }

    [Fact]
    public void APlanetBeyondTheShadersRangeGivesTheViewNoAir()
    {
        RenderSnapshot snapshot = WithSun(new(0.3f, 1, -0.4f));
        var center = new Vector3(0, -1e19f, 0);
        snapshot.Atmosphere = new(Center: center, PreviousCenter: center, Medium: MarsAir, Sun: 0);
        GpuAtmosphere atmosphere = AtmosphereOf(snapshot);

        atmosphere.Sky.Should().Be(Vector4.Zero, "a center past ShaderInput.PositionLimit cannot reach the shaders");
        atmosphere.Haze.Should().Be(Vector4.Zero);
        Rgb(atmosphere.SunTint).Should().Be(Vector3.One);
    }

    [Fact]
    public void TheHazesLightReachesTheShaderAtItsFullDayValueWhateverTheTimeOfDayAndTheHeight()
    {
        RenderSnapshot day = WithSun(new(0.3f, 1, -0.4f));
        Air(day, MarsAir, height: ScaleHeight);
        RenderSnapshot dusk = WithSun(new(1, 0, -0.2f));
        Air(dusk, MarsAir, height: ScaleHeight);

        ExpectNear(Rgb(AtmosphereOf(day).Haze), PaintedHaze, "the haze's density enters through the transmittance");
        ExpectNear(
            Rgb(AtmosphereOf(dusk).Haze),
            PaintedHaze,
            "the shaders dim the haze by the day at each hazed point"
        );
    }

    [Fact]
    public void TheSunsPlaceReachesTheShaderRelativeToTheInterpolatedEye()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);
        snapshot.Camera.PreviousPosition = new(0, -10, 0);
        Air(snapshot, MarsAir);
        Vector4 sun = Prepare(snapshot, alpha: 0.5f).Constants.Atmosphere.Sun;

        // The test star stands still, StarDistance up, and halfway the eye is 5 m below the render origin.
        sun.Should().Be(new Vector4(x: 0, y: (float)Gpu.StarDistance + 5, z: 0, w: 1));
    }

    [Fact]
    public void TheEyesHeightAndDensityShareReachTheShader()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);
        Air(snapshot, MarsAir, height: ScaleHeight);
        Vector4 eye = AtmosphereOf(snapshot).Eye;

        eye.X.Should().BeApproximately(ScaleHeight, 0.5f, "the eye stands one scale height above the surface");
        eye.Y.Should().BeApproximately(1 / MathF.E, 1e-5f, "one scale height up, the air is 1/e as dense");
    }

    [Fact]
    public void AnEyeBelowTheSurfaceBreathesTheSurfacesAir()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);
        Air(snapshot, MarsAir, height: -50);

        AtmosphereOf(snapshot)
            .Eye.Should()
            .Be(new Vector4(x: 0, y: 1, z: 0, w: 0), "the density stays at the surface's below it");
    }

    [Fact]
    public void TheHazeDistanceReachesTheShaderAsItsInverseAndNoHazeAsZero()
    {
        RenderSnapshot hazy = WithSun(Vector3.UnitY);
        Air(hazy, MarsAir);
        RenderSnapshot clear = WithSun(Vector3.UnitY);
        Air(clear, MarsAir with { Haze = null });

        AtmosphereOf(hazy).Haze.W.Should().BeApproximately(1f / 20_000, 1e-10f);
        AtmosphereOf(clear).Haze.Should().Be(Vector4.Zero, "a planet that sets no haze has none");
    }

    [Fact]
    public void ThePlanetsShapeReachesTheShaderRelativeToTheEye()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);
        Air(snapshot, MarsAir, height: 500);
        GpuAtmosphere atmosphere = AtmosphereOf(snapshot);

        atmosphere.CenterRadius.Should().Be(new Vector4(x: 0, y: -(MarsRadius + 500), z: 0, w: MarsRadius));
        atmosphere.Sky.W.Should().BeApproximately(1f / ScaleHeight, 1e-10f);
        Rgb(atmosphere.SunTint).Should().Be(Tint);
    }

    [Fact]
    public void ThePlanetsCenterFollowsTheInterpolatedEye()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);
        snapshot.Camera.PreviousPosition = new(0, -10, 0);
        snapshot.Atmosphere = new(
            Center: new(0, -MarsRadius, 0),
            PreviousCenter: new(0, -MarsRadius - 40, 0),
            Medium: MarsAir,
            Sun: 0
        );

        // Halfway, the center is at −R − 20 and the eye at −5, so the center lies R + 15 below the eye.
        Prepare(snapshot, alpha: 0.5f).Constants.Atmosphere.CenterRadius.Y.Should().Be(-(MarsRadius + 15));
    }

    [Fact]
    public void ThePaintedSkyAndHazeReachTheShaderCappedAtTheSurfacesShare()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);
        Air(snapshot, MarsAir with { SkyRadiance = new(1e30f, 1, 0), Haze = new(new(1e30f, 2, 0), Distance: 100) });
        GpuAtmosphere atmosphere = AtmosphereOf(snapshot);

        Rgb(atmosphere.Sky).Should().Be(new Vector3(ShaderInput.MaxRadiance, 1, 0));
        Rgb(atmosphere.Haze).Should().Be(new Vector3(ShaderInput.MaxRadiance, 2, 0));
    }

    /// <summary>A white lamp of intensity 10 W/sr 3 m ahead and 2 m up.</summary>
    private static PointRenderLight Lamp(Vector3? color = null) =>
        Gpu.Lamp(
            position: new(0, 2, -3),
            color: color ?? Vector3.One,
            intensity: 10,
            chainDepth: TestChains.CamerasFrame
        );

    [Fact]
    public void TheSceneNamesTheSunsPlaceInItsLightList()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);
        snapshot.Lights.Insert(0, Lamp());
        Air(snapshot, MarsAir);
        snapshot.Atmosphere = snapshot.Atmosphere!.Value with { Sun = 1 };
        PreparedScene scene = Prepare(snapshot);

        scene.AirSunLight.Should().Be(1);
        scene.Lights[1].EntityIndex.Should().Be(Gpu.StarEntity);
    }

    [Fact]
    public void TheSunsPlaceCountsOnlyTheLightsThatTheSceneKeeps()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);
        // A black lamp shines no light, so the scene drops it, and the snapshot's second light becomes the scene's first.
        snapshot.Lights.Insert(0, Lamp(color: Vector3.Zero));
        Air(snapshot, MarsAir);
        snapshot.Atmosphere = snapshot.Atmosphere!.Value with { Sun = 1 };

        Prepare(snapshot).AirSunLight.Should().Be(0);
    }

    [Fact]
    public void NoLightTakesTheTintWithoutAnAtmosphere()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);

        Prepare(snapshot).AirSunLight.Should().BeNull();
    }

    [Fact]
    public void TheSunsPlaceLastsOnlyForItsFrame()
    {
        RenderSnapshot withAir = WithSun(Vector3.UnitY);
        Air(withAir, MarsAir);
        PreparedScene scene = Prepare(withAir);

        Prepare(WithSun(Vector3.UnitY), scene: scene).AirSunLight.Should().BeNull();
    }

    [Fact]
    public void TheFrameNamesTheSunsPlaceInTheLightList()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);
        snapshot.Lights.Insert(0, Lamp());
        Air(snapshot, MarsAir);
        snapshot.Atmosphere = snapshot.Atmosphere!.Value with { Sun = 1 };
        PreparedScene scene = Prepare(snapshot);

        scene.LightSpan.ToArray().Select(light => light.EntityIndex).Should().Equal(Gpu.LampEntity, Gpu.StarEntity);
        scene.Constants.Counts.Y.Should().Be(1u, "the sun is the second light of the list");
    }

    [Fact]
    public void TheFrameNamesNoSunWithoutAnAtmosphere()
    {
        RenderSnapshot snapshot = WithSun(Vector3.UnitY);

        Prepare(snapshot).Constants.Counts.Y.Should().Be(GpuFrameConstants.NoAirSun);
    }
}
