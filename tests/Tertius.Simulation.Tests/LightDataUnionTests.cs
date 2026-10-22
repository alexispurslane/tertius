using System.Numerics;
using System.Reflection;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// <see cref="LightData"/> as a custom C# 15 union of two cases: <see cref="PointLightData"/> and
/// <see cref="SpotLightData"/>. The default value holds no case: it is no light, and an entity whose render row holds it
/// carries none. These tests pin what a union promises. Each case goes in through its constructor and comes out through
/// its <c>TryGetValue</c>. No other <c>TryGetValue</c> answers true, and a view of the other case is empty, so the cone
/// angles of a spot light never show up as fields of a point light. Equality needs the same case and the same fields. A
/// <c>switch</c> over the light takes the arm of the case that it holds. The snapshot tests in
/// <see cref="RenderSnapshotLightingTests"/> pin what the snapshot builder makes of each case.
/// </summary>
public class LightDataUnionTests
{
    private static readonly Pose Above = new(new(0, 1, 0), Quaternion.Identity);
    private static readonly Pose Turned = new(new(0, 1, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f));
    private static readonly Vector3 Warm = new(0.9f, 0.8f, 0.7f);

    /// <summary>A point light and a spot light that share every field that both cases have, so that only the case tells them apart.</summary>
    private static readonly PointLightData Lamp = new(Above, Warm, 4, 10);

    private static readonly SpotLightData Beam = new(Above, Warm, 4, 10, 0.3f, 0.5f);

    /// <summary>The names of the cases whose <c>TryGetValue</c> answers true for the light.</summary>
    private static string[] CasesHeld(LightData light)
    {
        var held = new List<string>();
        if (light.TryGetValue(out PointLightData _))
            held.Add(nameof(PointLightData));
        if (light.TryGetValue(out SpotLightData _))
            held.Add(nameof(SpotLightData));
        return [.. held];
    }

    /// <summary>The value that each <c>TryGetValue</c> hands out, whatever it answers.</summary>
    private static (PointLightData Point, SpotLightData Spot) Views(LightData light)
    {
        light.TryGetValue(out PointLightData point);
        light.TryGetValue(out SpotLightData spot);
        return (point, spot);
    }

    private static string ArmOf(LightData light) => light switch
    {
        PointLightData(_, _, var intensity, var range) => $"point {intensity} {range}",
        SpotLightData(_, _, _, _, var inner, var outer) => $"spot {inner} {outer}",
        _ => "none"
    };

    // ---- The default value ---------------------------------------------------------------------------------------

    [Fact]
    public void TheDefaultLightHoldsNoCase()
    {
        LightData light = default;

        light.HasValue.Should().BeFalse();
        light.Value.Should().BeNull();
        CasesHeld(light).Should().BeEmpty();
        Views(light).Should().Be((default, default));
    }

    [Fact]
    public void ARenderRowMadeWithoutALightCarriesNone()
    {
        new RenderData().Light.HasValue.Should().BeFalse("the parameterless constructor sets no light");
        default(RenderData).Light.HasValue.Should().BeFalse("a zeroed row carries no light either");
        new RenderData { Mesh = new(1) }.Light.HasValue.Should().BeFalse("setting other fields leaves the light out");
    }

    // ---- One constructor and one TryGetValue per case ------------------------------------------------------------

    [Fact]
    public void APointLightComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var light = new LightData(Lamp);

        light.HasValue.Should().BeTrue();
        light.TryGetValue(out PointLightData held).Should().BeTrue();
        held.Should().Be(Lamp);
        CasesHeld(light).Should().Equal(nameof(PointLightData));
        (light is PointLightData).Should().BeTrue("a pattern match reads the same case");
    }

    [Fact]
    public void ASpotLightComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var light = new LightData(Beam);

        light.HasValue.Should().BeTrue();
        light.TryGetValue(out SpotLightData held).Should().BeTrue();
        held.Should().Be(Beam);
        CasesHeld(light).Should().Equal(nameof(SpotLightData));
        (light is SpotLightData).Should().BeTrue("a pattern match reads the same case");
    }

    [Fact]
    public void ATryGetValueThatAnswersFalseHandsOutTheDefaultValueNotTheFieldsOfAnotherCase()
    {
        // The spot light has every field set, so a case that read a shared field without checking would show it.
        Views(new(Beam)).Should().Be((default, Beam));
        Views(new(Lamp)).Should().Be((Lamp, default));
    }

    [Fact]
    public void EveryFieldOfEveryCaseSurvivesTheRoundTrip()
    {
        // The numbers of each case differ from each other, so a swap of two fields shows.
        var point = new PointLightData(Turned, new(4, 5, 6), 7, 8);
        var spot = new SpotLightData(Turned, new(4, 5, 6), 7, 8, 9, 10);

        new LightData(point).TryGetValue(out PointLightData heldPoint).Should().BeTrue();
        heldPoint.Offset.Should().Be(Turned);
        heldPoint.Color.Should().Be(new Vector3(4, 5, 6));
        heldPoint.Intensity.Should().Be(7);
        heldPoint.Range.Should().Be(8);
        new LightData(spot).TryGetValue(out SpotLightData heldSpot).Should().BeTrue();
        heldSpot.Offset.Should().Be(Turned);
        heldSpot.Color.Should().Be(new Vector3(4, 5, 6));
        heldSpot.Intensity.Should().Be(7);
        heldSpot.Range.Should().Be(8);
        heldSpot.InnerConeAngle.Should().Be(9);
        heldSpot.OuterConeAngle.Should().Be(10);
    }

    [Fact]
    public void ValueBoxesTheCaseThatTheLightHolds()
    {
        new LightData(Lamp).Value.Should().Be(Lamp);
        new LightData(Beam).Value.Should().Be(Beam);
    }

    [Fact]
    public void EveryCaseConvertsImplicitlyToTheLightThatItsConstructorBuilds()
    {
        LightData fromPoint = Lamp;
        LightData fromSpot = Beam;

        fromPoint.Value.Should().Be(Lamp);
        fromSpot.Value.Should().Be(Beam);
        fromPoint.Should().Be(new LightData(Lamp));
        fromSpot.Should().Be(new LightData(Beam));
    }

    [Fact]
    public void ACaseGoesStraightIntoTheLightOfARenderRow()
    {
        var pointRow = new RenderData { Light = Lamp };
        var spotRow = new RenderData { Light = Beam };

        pointRow.Light.Value.Should().Be(Lamp);
        spotRow.Light.Value.Should().Be(Beam);
    }

    // ---- Pattern matching ----------------------------------------------------------------------------------------

    [Fact]
    public void ASwitchOverALightTakesTheArmOfTheCaseItHoldsAndTheLastArmForTheDefault()
    {
        ArmOf(new(Lamp)).Should().Be("point 4 10");
        ArmOf(new(Beam)).Should().Be("spot 0.3 0.5");
        ArmOf(default).Should().Be("none");
    }

    [Fact]
    public void ReadingALightByPatternAllocatesNothing()
    {
        // A pattern match reads the fields of the case in place. Boxing the case, as Value does, would allocate.
        LightData[] lights = [Lamp, Beam, default];
        float total = 0;
        foreach (LightData light in lights)
            total += ReadRange(light);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 100; round++)
            foreach (LightData light in lights)
                total += ReadRange(light);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
        total.Should().Be(2 * 10 * 101, "a point and a spot light of range 10 each, over 101 rounds");
    }

    private static float ReadRange(in LightData light)
    {
        return light switch
        {
            PointLightData(_, _, _, var range) => range,
            SpotLightData(_, _, _, var range, _, _) => range,
            _ => 0
        };
    }

    // ---- Equality ------------------------------------------------------------------------------------------------

    [Fact]
    public void TwoLightsOfTheSameCaseWithTheSameFieldsAreEqualAndHashAlike()
    {
        LightData[] first =
            [new PointLightData(Above, Warm, 4, 10), new SpotLightData(Above, Warm, 4, 10, 0.3f, 0.5f), default];
        LightData[] second =
            [new PointLightData(Above, Warm, 4, 10), new SpotLightData(Above, Warm, 4, 10, 0.3f, 0.5f), default];

        for (int i = 0; i < first.Length; i++)
        {
            first[i].Equals(second[i]).Should().BeTrue($"case {i}");
            first[i].Equals((object)second[i]).Should().BeTrue($"case {i} as an object");
            (first[i] == second[i]).Should().BeTrue($"case {i} by ==");
            (first[i] != second[i]).Should().BeFalse($"case {i} by !=");
            first[i].GetHashCode().Should().Be(second[i].GetHashCode(), $"case {i}");
        }
    }

    [Fact]
    public void TwoPointLightsThatDifferInOneFieldAreNotEqual()
    {
        LightData light = Lamp;

        light.Should().NotBe(new LightData(Lamp with { Offset = Turned }), "another offset");
        light.Should().NotBe(new LightData(Lamp with { Color = new(0.9f, 0.8f, 0.6f) }), "another color");
        light.Should().NotBe(new LightData(Lamp with { Intensity = 5 }), "another intensity");
        light.Should().NotBe(new LightData(Lamp with { Range = 11 }), "another range");
    }

    [Fact]
    public void TwoSpotLightsThatDifferInOneFieldAreNotEqual()
    {
        LightData light = Beam;

        light.Should().NotBe(new LightData(Beam with { Offset = Turned }), "another offset");
        light.Should().NotBe(new LightData(Beam with { Color = new(0.9f, 0.8f, 0.6f) }), "another color");
        light.Should().NotBe(new LightData(Beam with { Intensity = 5 }), "another intensity");
        light.Should().NotBe(new LightData(Beam with { Range = 11 }), "another range");
        light.Should().NotBe(new LightData(Beam with { InnerConeAngle = 0.31f }), "another inner cone angle");
        light.Should().NotBe(new LightData(Beam with { OuterConeAngle = 0.51f }), "another outer cone angle");
    }

    [Fact]
    public void LightsOfDifferentCasesAreNotEqualEvenWhenTheirSharedFieldsMatch()
    {
        // The point light and the spot light share offset, color, intensity and range, so only the case can tell them apart.
        LightData point = Lamp;
        LightData spot = Beam;
        LightData spotWithoutCone = new SpotLightData(Above, Warm, 4, 10, 0, 0);

        (point == spot).Should().BeFalse();
        (point != spot).Should().BeTrue();
        point.Equals(spot).Should().BeFalse();
        spot.Equals(point).Should().BeFalse();
        point.Equals((object)spot).Should().BeFalse();
        point.Should().NotBe(spotWithoutCone,
            "the cone angles of the spot light are all zero, as the point light has none");
    }

    [Fact]
    public void ALightWithACaseIsNeverEqualToTheEmptyLightEvenWhenEveryFieldIsZero()
    {
        LightData empty = default;

        new LightData(new PointLightData(default, default, 0, 0)).Should().NotBe(empty);
        new LightData(new SpotLightData(default, default, 0, 0, 0, 0)).Should().NotBe(empty);
        (empty != new LightData(new PointLightData(default, default, 0, 0))).Should().BeTrue();
    }

    [Fact]
    public void ALightIsNotEqualToAnObjectThatIsNotALight()
    {
        new LightData(Lamp).Equals((object)Lamp).Should().BeFalse();
        new LightData(Lamp).Equals(null).Should().BeFalse();
    }

    [Fact]
    public void ALightThatHoldsANaNEqualsItselfAndAnyCopyAndHashesTheSame()
    {
        // Equals must be reflexive, even for a field that is not a number: a value that is not equal to itself breaks
        // every set and every dictionary that holds it. So a NaN equals a NaN here, as it does in Vector3.Equals.
        var notANumberPose = new Pose(new(float.NaN, 0, 0), Quaternion.Identity);
        LightData[] lights =
        [
            Lamp with { Intensity = float.NaN }, Beam with { Offset = notANumberPose },
            Beam with { OuterConeAngle = float.NaN }
        ];

        foreach (LightData light in lights)
        {
            LightData copy = light.Value switch
            {
                PointLightData point => point,
                SpotLightData spot => spot,
                _ => default
            };

            light.Equals(light).Should().BeTrue($"{light} equals itself");
            (light == copy).Should().BeTrue($"{light} equals a copy of itself");
            (light != copy).Should().BeFalse($"{light} does not differ from a copy of itself");
            light.GetHashCode().Should()
                .Be(copy.GetHashCode(), $"{light} and its copy are equal, so they hash the same");
        }
    }

    // ---- ToString ------------------------------------------------------------------------------------------------

    [Fact]
    public void ToStringNamesTheCaseAndItsFieldsAndSaysNoneForTheDefault()
    {
        default(LightData).ToString().Should().Be("LightData(none)");
        new LightData(Lamp).ToString().Should().StartWith("LightData(").And.Contain(nameof(PointLightData)).And
            .Contain("Range = 10");
        new LightData(Beam).ToString().Should().StartWith("LightData(").And.Contain(nameof(SpotLightData)).And
            .Contain("OuterConeAngle");
    }

    // ---- Shape ---------------------------------------------------------------------------------------------------

    [Fact]
    public void NoPublicMemberOfTheUnionExposesTheFieldsOfOneCase()
    {
        // The fields are reachable only through TryGetValue and a pattern match, which name the case. A public property
        // or field of the union could be read whichever case the light holds.
        const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;
        Type union = typeof(LightData);

        union.GetFields(publicInstance).Should().BeEmpty();
        union.GetProperties(publicInstance).Select(property => property.PropertyType).Should()
            .OnlyContain(type => type == typeof(bool) || type == typeof(object));
        union.GetConstructors().Select(constructor => constructor.GetParameters().Single().ParameterType)
            .Should().BeEquivalentTo([typeof(PointLightData), typeof(SpotLightData)],
                "a light is made from one case and nothing else");
    }
}
