using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The atmosphere record of a render snapshot (docs/plans/atmosphere.md, "The snapshot build"): the planet medium of the
/// nearest frame of the camera's chain that has one, the planet's center relative to the render origin, and the planet's
/// sun, the point light that gives the planet's center the most light. The camera never turns here, so the axes of its
/// frame are the axes of every snapshot position.
/// </summary>
public sealed class RenderSnapshotAtmosphereTests
{
    private const double MarsRadius = 3_389_500;

    /// <summary>A Mars-like air with every part of the look.</summary>
    private static readonly PlanetMedium MarsAir = new(
        Radius: MarsRadius,
        SurfaceDensity: 0.02f,
        ScaleHeight: 11_100,
        Drag: 2,
        SkyRadiance: new(0.6f, 0.4f, 0.25f),
        Haze: new(new(0.5f, 0.35f, 0.2f), Distance: 20_000),
        SunTint: new(1, 0.85f, 0.7f)
    );

    /// <summary>An Earth-like air with only a sky.</summary>
    private static readonly PlanetMedium EarthAir = new(
        Radius: 6_371_000,
        SurfaceDensity: 1.225f,
        ScaleHeight: 8_500,
        Drag: 2,
        SkyRadiance: new(0.2f, 0.35f, 0.6f)
    );

    /// <summary>The air of a cabin, at the density of Earth's air, that fills a box of 6 m.</summary>
    private static readonly MediumField CabinAir = MediumField.Uniform(1.225f, 2, new BoundingBox(new(-3), new(3)));

    /// <summary>The Sun's light: 3.828 × 10²⁶ W from a sphere of 6.957 × 10⁸ m.</summary>
    private static readonly PointLightData Star = new(
        Offset: default,
        Color: Vector3.One,
        Power: 3.828e26f,
        Radius: 6.957e8f,
        CastsShadow: true
    );

    /// <summary>Mars's distance from the Sun, in m: 1.52 AU.</summary>
    private const double SunDistance = 2.28e11;

    /// <summary>A frame of the bubble <paramref name="radius"/> at <paramref name="place"/> in <paramref name="parent"/>, which holds <paramref name="medium"/>.</summary>
    private static FrameHandle SpawnFrame(
        TestWorld test,
        FrameHandle parent,
        Double3 place,
        MediumField medium,
        float radius
    ) =>
        test.World.GetOwnedFrame(
            test.World.Spawn(
                new()
                {
                    Frame = parent,
                    Pose = new(place, DQuaternion.Identity),
                    AsFrame = new(FrameBounds.Sphere(radius), Medium: medium),
                }
            )
        );

    /// <summary>Mars's frame in the root, its center at the origin, with <see cref="MarsAir"/>.</summary>
    private static FrameHandle SpawnMars(TestWorld test) =>
        SpawnFrame(test: test, parent: test.Root, place: Double3.Zero, medium: new(MarsAir), radius: 4e6f);

    /// <summary>An entity at <paramref name="place"/> in <paramref name="frame"/> that carries <paramref name="light"/> and draws nothing.</summary>
    private static Entity SpawnLight(TestWorld test, FrameHandle frame, Double3 place, LightData light) =>
        test.World.Spawn(
            new()
            {
                Frame = frame,
                Pose = new(place, DQuaternion.Identity),
                Render = new RenderData { Light = light },
            }
        );

    /// <summary>A point light of <paramref name="power"/> W, white, of radius 0, that casts no shadow.</summary>
    private static PointLightData Lamp(float power) =>
        new(Offset: default, Color: Vector3.One, Power: power, Radius: 0, CastsShadow: false);

    /// <summary>The index in the snapshot's lights of the light that <paramref name="entity"/> carries.</summary>
    private static int LightIndexOf(RenderSnapshot snapshot, Entity entity) =>
        snapshot.Lights.FindIndex(light =>
            light.TryGetValue(out PointRenderLight point) && point.EntityIndex == entity.Index
        );

    private static void ExpectNear(Vector3 actual, Double3 expected, string because) =>
        Vector3.Distance(actual, expected.ToVector3()).Should().BeLessThan(1f, because);

    [Fact]
    public void ACameraInAPlanetsFrameSeesThatPlanetsAir()
    {
        using var test = new TestWorld();
        FrameHandle mars = SpawnMars(test);
        test.World.Camera = test.SpawnCamera(mars, new(new(0, MarsRadius + 2, 0), DQuaternion.Identity));

        test.Run(1);
        RenderAtmosphere? atmosphere = test.World.Latest().Atmosphere;

        atmosphere.Should().NotBeNull("the camera's own frame holds a planet medium");
        atmosphere!.Value.Medium.Should().Be(MarsAir);
        ExpectNear(atmosphere.Value.Center, new(0, -(MarsRadius + 2), 0), "the planet's center lies below the eye");
        atmosphere.Value.PreviousCenter.Should().Be(atmosphere.Value.Center, "neither the camera nor the planet moved");
    }

    [Fact]
    public void ACabinOnTheSurfaceSeesItsPlanetsAir()
    {
        using var test = new TestWorld();
        FrameHandle mars = SpawnMars(test);
        FrameHandle cabin = SpawnFrame(
            test: test,
            parent: mars,
            place: new(40, MarsRadius + 3, -10),
            medium: CabinAir,
            radius: 20
        );
        test.World.Camera = test.SpawnCamera(cabin, new(new(1, 0.5, 0), DQuaternion.Identity));

        test.Run(1);
        RenderAtmosphere? atmosphere = test.World.Latest().Atmosphere;

        atmosphere.Should().NotBeNull("the cabin's air is not a planet's, and its parent's is");
        atmosphere!.Value.Medium.Should().Be(MarsAir);
        ExpectNear(
            atmosphere.Value.Center,
            new(-41, -(MarsRadius + 3.5), 10),
            "the planet's center lies below the eye in the cabin"
        );
    }

    [Fact]
    public void TheNearestPlanetMediumOnTheChainGivesTheAir()
    {
        using var test = new TestWorld();
        FrameHandle mars = SpawnMars(test);
        FrameHandle inner = SpawnFrame(
            test: test,
            parent: mars,
            place: new(0, 2e5, 0),
            medium: new(EarthAir),
            radius: 1e5f
        );
        test.World.Camera = test.SpawnCamera(inner, new(Double3.Zero, DQuaternion.Identity));

        test.Run(1);

        test.World.Latest().Atmosphere!.Value.Medium.Should().Be(EarthAir);
    }

    [Fact]
    public void ACameraWhoseChainHoldsNoPlanetMediumSeesNoAir()
    {
        using var test = new TestWorld();
        SpawnMars(test);
        FrameHandle cabin = SpawnFrame(
            test: test,
            parent: test.Root,
            place: new(0, 1e7, 0),
            medium: CabinAir,
            radius: 20
        );
        test.World.Camera = test.SpawnCamera(cabin, new(Double3.Zero, DQuaternion.Identity));

        test.Run(1);

        test.World.Latest().Atmosphere.Should().BeNull("a cabin in space with its own air lies in no planet's frame");
    }

    [Fact]
    public void ASnapshotWithoutACameraHasNoAir()
    {
        using var test = new TestWorld();
        FrameHandle mars = SpawnMars(test);
        test.World.Camera = test.SpawnCamera(mars, new(new(0, MarsRadius, 0), DQuaternion.Identity));
        test.Run(1);
        test.World.Latest();
        test.World.Camera = Entity.Null;

        test.Run(1);

        test.World.Latest().Atmosphere.Should().BeNull();
    }

    [Fact]
    public void TheSunIsThePointLightThatGivesThePlanetsCenterTheMostLight()
    {
        using var test = new TestWorld();
        FrameHandle mars = SpawnMars(test);
        Entity lamp = SpawnLight(test: test, frame: mars, place: new(0, MarsRadius + 1, 0), light: Lamp(1e4f));
        Entity sun = SpawnLight(test: test, frame: test.Root, place: new(SunDistance, 0, 0), light: Star);
        Entity dimStar = SpawnLight(
            test: test,
            frame: test.Root,
            place: new(0, 0, SunDistance),
            light: Star with
            {
                Power = 1e24f,
            }
        );
        test.World.Camera = test.SpawnCamera(mars, new(new(0, MarsRadius + 2, 0), DQuaternion.Identity));

        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        snapshot.Atmosphere!.Value.Sun.Should().Be(LightIndexOf(snapshot, sun));
        LightIndexOf(snapshot, lamp).Should().BeGreaterThanOrEqualTo(0, "the lamp is a light of the snapshot too");
        LightIndexOf(snapshot, dimStar)
            .Should()
            .BeGreaterThanOrEqualTo(0, "the dim star is a light of the snapshot too");
    }

    [Fact]
    public void ALightNearThePlanetsCenterDoesNotWinByItsNearness()
    {
        using var test = new TestWorld();
        FrameHandle mars = SpawnMars(test);
        // 10⁶ W from 1 m gives the center 10⁶ W/m² over 4π, more than the Sun's 7 × 10³. Seen from the surface, it gives
        // 10⁶ / R², far less.
        SpawnLight(test: test, frame: mars, place: new(1, 0, 0), light: Lamp(1e6f));
        Entity sun = SpawnLight(test: test, frame: test.Root, place: new(SunDistance, 0, 0), light: Star);
        test.World.Camera = test.SpawnCamera(mars, new(new(0, MarsRadius + 2, 0), DQuaternion.Identity));

        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        snapshot.Atmosphere!.Value.Sun.Should().Be(LightIndexOf(snapshot, sun));
    }

    [Fact]
    public void OfTwoLightsThatGiveTheCenterTheSameLightTheEarlierOneIsTheSun()
    {
        using var test = new TestWorld();
        FrameHandle mars = SpawnMars(test);
        Entity first = SpawnLight(test: test, frame: test.Root, place: new(SunDistance, 0, 0), light: Star);
        SpawnLight(test: test, frame: test.Root, place: new(-SunDistance, 0, 0), light: Star);
        test.World.Camera = test.SpawnCamera(mars, new(new(0, MarsRadius + 2, 0), DQuaternion.Identity));

        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        snapshot
            .Atmosphere!.Value.Sun.Should()
            .Be(LightIndexOf(snapshot, first))
            .And.Be(0, "the first light comes first in slot order");
    }

    [Fact]
    public void ASpotLightIsNeverTheSun()
    {
        using var test = new TestWorld();
        FrameHandle mars = SpawnMars(test);
        SpawnLight(
            test: test,
            frame: mars,
            place: new(0, MarsRadius + 1, 0),
            light: new SpotLightData(
                Offset: default,
                Color: Vector3.One,
                Power: 1e30f,
                Radius: 0,
                CastsShadow: false,
                InnerConeAngle: 0.3f,
                OuterConeAngle: 0.5f
            )
        );
        test.World.Camera = test.SpawnCamera(mars, new(new(0, MarsRadius + 2, 0), DQuaternion.Identity));

        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        snapshot.Lights.Should().ContainSingle("the spot light is a light of the snapshot");
        snapshot.Atmosphere!.Value.Sun.Should().BeNull("a spot light lights only its cone, not a planet's day");
    }
}
