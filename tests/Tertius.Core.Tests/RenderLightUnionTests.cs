using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Tertius.Testing;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="RenderLight"/> as a custom C# 15 union of two cases: <see cref="PointRenderLight"/> and
/// <see cref="SpotRenderLight"/>. The default value holds no case: it is no light. These tests pin what a union promises.
/// Each case goes in through its constructor and comes out through its <c>TryGetValue</c>. No other <c>TryGetValue</c>
/// answers true, and a view of the other case is empty, so the direction and the cone angles of a spot light never show
/// up as fields of a point light. Equality needs the same case and the same fields. A <c>switch</c> over the light takes the
/// arm of the case that it holds. The batcher tests in the rendering module pin what the renderer does with each case.
/// </summary>
public sealed class RenderLightUnionTests
{
    private static readonly Vector3 Here = new(1, 2, 3);
    private static readonly Vector3 Down = new(0, -1, 0);
    private static readonly Vector3 Warm = new(0.9f, 0.8f, 0.7f);

    /// <summary>A point light and a spot light that share every field that both cases have, so that only the case tells them apart.</summary>
    private static readonly PointRenderLight Lamp = new(Position: Here, Color: Warm, Intensity: 4, Range: 10);

    private static readonly SpotRenderLight Beam = new(
        Position: Here,
        Direction: Down,
        Color: Warm,
        Intensity: 4,
        Range: 10,
        InnerConeAngle: 0.3f,
        OuterConeAngle: 0.5f
    );

    /// <summary>The names of the cases whose <c>TryGetValue</c> answers true for the light.</summary>
    private static string[] CasesHeld(RenderLight light)
    {
        var held = new List<string>();
        if (light.TryGetValue(out PointRenderLight _))
            held.Add(nameof(PointRenderLight));
        if (light.TryGetValue(out SpotRenderLight _))
            held.Add(nameof(SpotRenderLight));
        return [.. held];
    }

    /// <summary>The value that each <c>TryGetValue</c> hands out, whatever it answers.</summary>
    private static (PointRenderLight Point, SpotRenderLight Spot) Views(RenderLight light)
    {
        light.TryGetValue(out PointRenderLight point);
        light.TryGetValue(out SpotRenderLight spot);
        return (point, spot);
    }

    private static string ArmOf(RenderLight light) =>
        light switch
        {
            PointRenderLight { Position: var position, Intensity: var intensity, Range: var range } =>
                $"point {position.X} {intensity} {range}",
            SpotRenderLight { Direction: var direction, InnerConeAngle: var inner, OuterConeAngle: var outer } =>
                $"spot {direction.Y} {inner} {outer}",
            null => "none",
        };

    // ---- The default value ---------------------------------------------------------------------------------------

    [Fact]
    public void TheDefaultLightHoldsNoCase()
    {
        RenderLight light = default;

        light.HasValue.Should().BeFalse();
        light.Value.Should().BeNull();
        CasesHeld(light).Should().BeEmpty();
        Views(light).Should().Be((default, default));
    }

    // ---- One constructor and one TryGetValue per case ------------------------------------------------------------

    [Fact]
    public void APointLightComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var light = new RenderLight(Lamp);

        light.HasValue.Should().BeTrue();
        light.TryGetValue(out PointRenderLight held).Should().BeTrue();
        held.Should().Be(Lamp);
        CasesHeld(light).Should().Equal(nameof(PointRenderLight));
        (light is PointRenderLight).Should().BeTrue("a pattern match reads the same case");
    }

    [Fact]
    public void ASpotLightComesBackThroughItsTryGetValueAndNoOtherCaseAnswers()
    {
        var light = new RenderLight(Beam);

        light.HasValue.Should().BeTrue();
        light.TryGetValue(out SpotRenderLight held).Should().BeTrue();
        held.Should().Be(Beam);
        CasesHeld(light).Should().Equal(nameof(SpotRenderLight));
        (light is SpotRenderLight).Should().BeTrue("a pattern match reads the same case");
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
        // Every field of each case differs from every other field, so a swap of two fields shows.
        var point = new PointRenderLight(Position: new(1, 2, 3), Color: new(4, 5, 6), Intensity: 7, Range: 8);
        var spot = new SpotRenderLight(
            Position: new(1, 2, 3),
            Direction: new(4, 5, 6),
            Color: new(7, 8, 9),
            Intensity: 10,
            Range: 11,
            InnerConeAngle: 12,
            OuterConeAngle: 13
        );

        new RenderLight(point).TryGetValue(out PointRenderLight heldPoint).Should().BeTrue();
        heldPoint.Position.Should().Be(new Vector3(1, 2, 3));
        heldPoint.Color.Should().Be(new Vector3(4, 5, 6));
        heldPoint.Intensity.Should().Be(7);
        heldPoint.Range.Should().Be(8);
        new RenderLight(spot).TryGetValue(out SpotRenderLight heldSpot).Should().BeTrue();
        heldSpot.Position.Should().Be(new Vector3(1, 2, 3));
        heldSpot.Direction.Should().Be(new Vector3(4, 5, 6));
        heldSpot.Color.Should().Be(new Vector3(7, 8, 9));
        heldSpot.Intensity.Should().Be(10);
        heldSpot.Range.Should().Be(11);
        heldSpot.InnerConeAngle.Should().Be(12);
        heldSpot.OuterConeAngle.Should().Be(13);
    }

    [Fact]
    public void ValueBoxesTheCaseThatTheLightHolds()
    {
        new RenderLight(Lamp).Value.Should().Be(Lamp);
        new RenderLight(Beam).Value.Should().Be(Beam);
    }

    [Fact]
    public void EveryCaseConvertsImplicitlyToTheLightThatItsConstructorBuilds()
    {
        RenderLight fromPoint = Lamp;
        RenderLight fromSpot = Beam;

        fromPoint.Value.Should().Be(Lamp);
        fromSpot.Value.Should().Be(Beam);
        fromPoint.Should().Be(new RenderLight(Lamp));
        fromSpot.Should().Be(new RenderLight(Beam));
    }

    // ---- Pattern matching ----------------------------------------------------------------------------------------

    [Fact]
    public void ASwitchOverALightTakesTheArmOfTheCaseItHoldsAndTheLastArmForTheDefault()
    {
        ArmOf(new(Lamp)).Should().Be("point 1 4 10");
        ArmOf(new(Beam)).Should().Be("spot -1 0.3 0.5");
        ArmOf(default).Should().Be("none");
    }

    [Fact]
    public void ReadingALightByPatternAllocatesNothing()
    {
        // A pattern match reads the fields of the case in place. Boxing the case, as Value does, would allocate.
        RenderLight[] lights = [Lamp, Beam, default];
        float total = 0;
        foreach (RenderLight light in lights)
            total += ReadRange(light);
        long allocated = Allocations.Fewest(() =>
        {
            total = 0;
            for (int round = 0; round < 100; round++)
                foreach (RenderLight light in lights)
                    total += ReadRange(light);
        });

        allocated.Should().Be(0);
        total.Should().Be(2 * 10 * 100, "a point and a spot light of range 10 each, over 100 rounds");
    }

    private static float ReadRange(in RenderLight light) =>
        light switch
        {
            PointRenderLight { Range: var range } => range,
            SpotRenderLight { Range: var range } => range,
            null => 0,
        };

    // ---- Equality ------------------------------------------------------------------------------------------------

    [Fact]
    public void TwoLightsOfTheSameCaseWithTheSameFieldsAreEqualAndHashAlike()
    {
        RenderLight[] first =
        [
            new PointRenderLight(Position: Here, Color: Warm, Intensity: 4, Range: 10),
            new SpotRenderLight(
                Position: Here,
                Direction: Down,
                Color: Warm,
                Intensity: 4,
                Range: 10,
                InnerConeAngle: 0.3f,
                OuterConeAngle: 0.5f
            ),
            default,
        ];
        RenderLight[] second =
        [
            new PointRenderLight(Position: Here, Color: Warm, Intensity: 4, Range: 10),
            new SpotRenderLight(
                Position: Here,
                Direction: Down,
                Color: Warm,
                Intensity: 4,
                Range: 10,
                InnerConeAngle: 0.3f,
                OuterConeAngle: 0.5f
            ),
            default,
        ];

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
        RenderLight light = Lamp;

        light.Should().NotBe(new RenderLight(Lamp with { Position = new(1, 2, 4) }), "another position");
        light.Should().NotBe(new RenderLight(Lamp with { Color = new(0.9f, 0.8f, 0.6f) }), "another color");
        light.Should().NotBe(new RenderLight(Lamp with { Intensity = 5 }), "another intensity");
        light.Should().NotBe(new RenderLight(Lamp with { Range = 11 }), "another range");
    }

    [Fact]
    public void TwoSpotLightsThatDifferInOneFieldAreNotEqual()
    {
        RenderLight light = Beam;

        light.Should().NotBe(new RenderLight(Beam with { Position = new(1, 2, 4) }), "another position");
        light.Should().NotBe(new RenderLight(Beam with { Direction = new(0, 0, -1) }), "another direction");
        light.Should().NotBe(new RenderLight(Beam with { Color = new(0.9f, 0.8f, 0.6f) }), "another color");
        light.Should().NotBe(new RenderLight(Beam with { Intensity = 5 }), "another intensity");
        light.Should().NotBe(new RenderLight(Beam with { Range = 11 }), "another range");
        light.Should().NotBe(new RenderLight(Beam with { InnerConeAngle = 0.31f }), "another inner cone angle");
        light.Should().NotBe(new RenderLight(Beam with { OuterConeAngle = 0.51f }), "another outer cone angle");
    }

    [Fact]
    public void LightsOfDifferentCasesAreNotEqualEvenWhenTheirSharedFieldsMatch()
    {
        // The point light and the spot light share position, color, intensity and range, so only the case can tell them apart.
        RenderLight point = Lamp;
        RenderLight spot = Beam;
        RenderLight spotWithoutDirectionOrCone = new SpotRenderLight(
            Position: Here,
            Direction: default,
            Color: Warm,
            Intensity: 4,
            Range: 10,
            InnerConeAngle: 0,
            OuterConeAngle: 0
        );

        (point == spot).Should().BeFalse();
        (point != spot).Should().BeTrue();
        point.Equals(spot).Should().BeFalse();
        spot.Equals(point).Should().BeFalse();
        point.Equals((object)spot).Should().BeFalse();
        point
            .Should()
            .NotBe(
                spotWithoutDirectionOrCone,
                "the spot fields of the spot light are all zero, as the point light has none"
            );
    }

    [Fact]
    public void ALightWithACaseIsNeverEqualToTheEmptyLightEvenWhenEveryFieldIsZero()
    {
        RenderLight empty = default;

        new RenderLight(new PointRenderLight(Position: default, Color: default, Intensity: 0, Range: 0))
            .Should()
            .NotBe(empty);
        new RenderLight(
            new SpotRenderLight(
                Position: default,
                Direction: default,
                Color: default,
                Intensity: 0,
                Range: 0,
                InnerConeAngle: 0,
                OuterConeAngle: 0
            )
        )
            .Should()
            .NotBe(empty);
        (empty != new RenderLight(new PointRenderLight(Position: default, Color: default, Intensity: 0, Range: 0)))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ALightIsNotEqualToAnObjectThatIsNotALight()
    {
        new RenderLight(Lamp).Equals((object)Lamp).Should().BeFalse();
        new RenderLight(Lamp).Equals(null).Should().BeFalse();
    }

    [Fact]
    public void ALightThatHoldsANaNEqualsItselfAndAnyCopyAndHashesTheSame()
    {
        // Equals must be reflexive, even for a field that is not a number: a value that is not equal to itself breaks
        // every set and every dictionary that holds it. So a NaN equals a NaN here, as it does in Vector3.Equals.
        RenderLight[] lights =
        [
            Lamp with
            {
                Intensity = float.NaN,
            },
            Beam with
            {
                Position = new(float.NaN, 0, 0),
            },
            Beam with
            {
                OuterConeAngle = float.NaN,
            },
        ];

        foreach (RenderLight light in lights)
        {
            RenderLight copy = light.Value switch
            {
                PointRenderLight point => point,
                SpotRenderLight spot => spot,
                _ => default,
            };

            light.Equals(light).Should().BeTrue($"{light} equals itself");
            (light == copy).Should().BeTrue($"{light} equals a copy of itself");
            (light != copy).Should().BeFalse($"{light} does not differ from a copy of itself");
            light
                .GetHashCode()
                .Should()
                .Be(copy.GetHashCode(), $"{light} and its copy are equal, so they hash the same");
        }
    }

    // ---- ToString ------------------------------------------------------------------------------------------------

    [Fact]
    public void ToStringNamesTheCaseAndItsFieldsAndSaysNoneForTheDefault()
    {
        default(RenderLight).ToString().Should().Be("RenderLight(none)");
        new RenderLight(Lamp)
            .ToString()
            .Should()
            .StartWith("RenderLight(")
            .And.Contain(nameof(PointRenderLight))
            .And.Contain("Range = 10");
        new RenderLight(Beam)
            .ToString()
            .Should()
            .StartWith("RenderLight(")
            .And.Contain(nameof(SpotRenderLight))
            .And.Contain("OuterConeAngle");
    }

    // ---- Shape ---------------------------------------------------------------------------------------------------

    [Fact]
    public void NoPublicMemberOfTheUnionExposesTheFieldsOfOneCase()
    {
        // The fields are reachable only through TryGetValue and a pattern match, which name the case. A public property
        // or field of the union could be read whichever case the light holds.
        const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;
        Type union = typeof(RenderLight);

        union.GetFields(publicInstance).Should().BeEmpty();
        union
            .GetProperties(publicInstance)
            .Select(property => property.PropertyType)
            .Should()
            .OnlyContain(type => type == typeof(bool) || type == typeof(object));
        union
            .GetConstructors()
            .Select(constructor => constructor.GetParameters().Single().ParameterType)
            .Should()
            .BeEquivalentTo(
                [typeof(PointRenderLight), typeof(SpotRenderLight)],
                "a light is made from one case and nothing else"
            );
    }

    [Fact]
    public void ALightTakesFiftySixBytesTheFieldsOfOneSpotLightAndItsKindByte()
    {
        // The cases share the storage of position, color, intensity and range, so the union holds one spot light's 52 bytes
        // and a kind byte padded to 4, as the struct that it replaced did. Storing each case whole would take 88 bytes.
        Unsafe.SizeOf<SpotRenderLight>().Should().Be(52);
        Unsafe.SizeOf<RenderLight>().Should().Be(56);
    }
}
