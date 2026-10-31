using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="FarShadows.Choose"/>: which far lights get a cascade set, which get far shadows only and which get no slot,
/// by the irradiance at which they rank (<see cref="LightUnits.RankIrradiance"/>), here at the eye, with ties in list order
/// (docs/plans/local-light-shadows.md, "Shadows of each light"), the main view's lights before the lights that only a face
/// of a map keeps (docs/plans/environment-map.md, decision 20).
/// </summary>
public sealed class FarShadowsTests
{
    /// <summary>A far light 10^8 m from the eye: beyond FarDistance, so every caster sees it as one direction.</summary>
    private const double FarAway = 1e8;

    /// <summary>
    /// A white far light of <paramref name="intensity"/> W/sr, FarAway from the eye, that casts shadows, in the main view.
    /// </summary>
    private static PreparedLight Far(float intensity, bool castsShadow = true) =>
        new()
        {
            RadiantIntensity = new(intensity),
            Distance = FarAway,
            RankDistance = FarAway,
            CastsShadow = castsShadow,
            InMainView = true,
        };

    /// <summary>The far light of <see cref="Far"/> that only a drawn face of a map keeps.</summary>
    private static PreparedLight FaceOnly(float intensity) => Far(intensity) with { InMainView = false };

    /// <summary>A white near light of <paramref name="intensity"/> W/sr, 10 m from the eye, that casts shadows, in the main view.</summary>
    private static PreparedLight Near(float intensity) =>
        new()
        {
            RadiantIntensity = new(intensity),
            Distance = 10,
            RankDistance = 10,
            CastsShadow = true,
            InMainView = true,
        };

    private static LightShadow Set(int set) => new(new CascadeSet(Set: set, Occluders: default, Mapped: 0));

    private static LightShadow FarOnly => new(new FarShadowsOnly(default));

    private static LightShadow[] Slots(PreparedLight[] lights, int cascadedLights)
    {
        FarShadows.Choose(lights, cascadedLights);
        return [.. lights.Select(light => light.Shadow)];
    }

    [Fact]
    public void TheBrightestFarLightsAtTheEyeGetTheCascadeSets() =>
        Slots([Far(1), Far(5), Far(3), Far(4)], cascadedLights: 2).Should().Equal(FarOnly, Set(0), FarOnly, Set(1));

    [Fact]
    public void FarLightsOfEqualIrradianceKeepTheOrderOfTheList() =>
        Slots([Far(2), Far(2), Far(2)], cascadedLights: 2).Should().Equal(Set(0), Set(1), FarOnly);

    [Fact]
    public void AFarLightOfAtLeastAShareOfTheBrightestGetsFarShadowsOnly() =>
        Slots([Far(256), Far(1)], cascadedLights: 1)
            .Should()
            .Equal([Set(0), FarOnly], "1 W/sr is exactly a 256th of the brightest");

    [Fact]
    public void ADimmerFarLightGetsNoSlot() =>
        Slots([Far(256), Far(0.99f)], cascadedLights: 1).Should().Equal(Set(0), default);

    [Fact]
    public void AFarLightNearerTheEyeOutranksAMoreIntenseOneFartherAway() =>
        Slots([Far(8) with { Distance = 4 * FarAway, RankDistance = 4 * FarAway }, Far(1)], cascadedLights: 1)
            .Should()
            .Equal([FarOnly, Set(0)], "8 W/sr at four times the distance gives half the irradiance at the eye");

    [Fact]
    public void AFarLightRanksByTheLuminanceOfItsColor() =>
        Slots([Far(2) with { RadiantIntensity = new(2, 0, 0) }, Far(1)], cascadedLights: 1)
            .Should()
            .Equal(
                [FarOnly, Set(0)],
                "red's luminance weight is 0.2126, so 2 W/sr of red is dimmer than 1 W/sr of white"
            );

    [Fact]
    public void AFarLightBelowTheShareGetsNoCascadeSet() =>
        Slots([Far(256), Far(0.99f)], cascadedLights: 2)
            .Should()
            .Equal(
                [Set(0), default],
                "its shadow cannot show beside the brightest, so a set of four maps would be wasted"
            );

    [Fact]
    public void ALightThatCastsNoShadowGetsNoSlot() =>
        Slots([Far(1, castsShadow: false), Far(1)], cascadedLights: 1).Should().Equal(default, Set(0));

    [Fact]
    public void TheShareCountsFromTheBrightestFarLightEvenWhenItCastsNoShadow() =>
        Slots([Far(1_000, castsShadow: false), Far(3), Far(1)], cascadedLights: 1)
            .Should()
            .Equal(
                [default, default, default],
                "the light that casts no shadow lights the same surfaces, so shadows of under a 256th of it cannot show"
            );

    [Fact]
    public void ANearLightGetsNoFarSlot() =>
        Slots([Near(1e6f), Far(1), Far(0.5f)], cascadedLights: 1)
            .Should()
            .Equal(
                [default, Set(0), FarOnly],
                "the near light neither takes the set nor is the brightest that the share counts from"
            );

    [Fact]
    public void ANearLightKeepsItsSlot()
    {
        PreparedLight near = Near(1);
        near.Shadow = new(new AtlasTiles(First: 0, Count: 6));

        Slots([near, Far(1)], cascadedLights: 1).Should().Equal(near.Shadow, Set(0));
    }

    [Fact]
    public void WithNoCascadedLightsEveryShadowedFarLightGetsFarShadowsOnly() =>
        Slots([Far(3), Far(1)], cascadedLights: 0).Should().Equal(FarOnly, FarOnly);

    [Fact]
    public void AFarLightOfTheMainViewOutranksABrighterOneThatOnlyAFaceKeeps() =>
        Slots([FaceOnly(5), Far(1)], cascadedLights: 1)
            .Should()
            .Equal([FarOnly, Set(0)], "a light that only a face keeps must not change the main view's pixels");

    [Fact]
    public void AFarLightThatOnlyAFaceKeepsTakesACascadeSetThatTheMainViewLeavesFree() =>
        Slots([FaceOnly(5), Far(1)], cascadedLights: 2).Should().Equal(Set(1), Set(0));

    [Fact]
    public void FarLightsThatOnlyAFaceKeepsRankAmongThemselvesByIrradianceWithTiesInListOrder() =>
        Slots([FaceOnly(2), FaceOnly(3), FaceOnly(3), Far(1)], cascadedLights: 3)
            .Should()
            .Equal(FarOnly, Set(1), Set(2), Set(0));

    [Fact]
    public void TheShareOfTheMainViewsLightsCountsFromTheBrightestFarLightOfTheMainView() =>
        Slots([FaceOnly(1_000), Far(3), Far(1)], cascadedLights: 1)
            .Should()
            .Equal(
                [FarOnly, Set(0), FarOnly],
                "the main view's lights get the slots that they would get if no face kept the brighter light"
            );

    [Fact]
    public void TheShareOfALightThatOnlyAFaceKeepsCountsFromABrighterFarLightOfTheMainView() =>
        Slots([Far(1_000), FaceOnly(1)], cascadedLights: 2)
            .Should()
            .Equal(
                [Set(0), default],
                "past the main view's lights the choice is as before, with the share counted from every far light"
            );

    [Fact]
    public void TheShareOfALightThatOnlyAFaceKeepsCountsFromABrighterFarLightThatOnlyAFaceKeeps() =>
        Slots([FaceOnly(1_000), Far(10), FaceOnly(1)], cascadedLights: 3)
            .Should()
            .Equal(
                [Set(1), Set(0), default],
                "past the main view's lights the choice is as before, with the share counted from every far light"
            );

    [Fact]
    public void ChoosingAgainGivesTheSameSlots()
    {
        PreparedLight[] lights = [Far(256), Far(0.99f), Far(4)];
        LightShadow[] first = Slots(lights, cascadedLights: 1);

        Slots(lights, cascadedLights: 1).Should().Equal(first, "the slots of the choice before do not count");
    }

    [Fact]
    public void ChoosingAllocatesNothing()
    {
        PreparedLight[] lights = [Far(1), Far(5), Far(3), Far(4), Near(2)];
        FarShadows.Choose(lights, cascadedLights: 2);

        Allocations.Fewest(() => FarShadows.Choose(lights, cascadedLights: 2)).Should().Be(0);
    }
}
