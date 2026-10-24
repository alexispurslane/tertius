using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// Which way is up for the hemisphere ambient light of one instance: along one direction (<see cref="UpAlong"/>), away
/// from one center (<see cref="UpAwayFrom"/>), or neither, which mixes the sky and the ground color evenly. The value
/// holds only the vector of its own case, so a direction and a center can never be confused.
/// </summary>
public sealed class HemisphereUpTests
{
    private static readonly Vector3 Sideways = new(1, 0, 0);
    private static readonly Vector3 Below = new(0, -50, 0);

    [Fact]
    public void AnUpAlongADirectionReadsBackAsItsDirection()
    {
        var up = HemisphereUp.Along(Sideways);

        up.HasValue.Should().BeTrue();
        up.TryGetValue(out UpAlong along).Should().BeTrue();
        along.Direction.Should().Be(Sideways);
        (up is UpAlong { Direction: var direction } ? direction : default)
            .Should()
            .Be(Sideways, "a pattern match reads the same case");
    }

    [Fact]
    public void AnUpAwayFromACenterReadsBackAsItsCenter()
    {
        var up = HemisphereUp.AwayFrom(Below);

        up.HasValue.Should().BeTrue();
        up.TryGetValue(out UpAwayFrom away).Should().BeTrue();
        away.Center.Should().Be(Below);
        (up is UpAwayFrom { Center: var center } ? center : default)
            .Should()
            .Be(Below, "a pattern match reads the same case");
    }

    [Fact]
    public void AnUpBuiltFromACaseIsTheUpThatTheFactoryBuilds()
    {
        HemisphereUp viaConstructor = new(new UpAlong(Sideways));
        HemisphereUp viaConversion = new UpAwayFrom(Below);

        (viaConstructor == HemisphereUp.Along(Sideways)).Should().BeTrue();
        (viaConversion == HemisphereUp.AwayFrom(Below)).Should().BeTrue();
    }

    [Fact]
    public void AnUpAlongADirectionHasNoCenter()
    {
        var up = HemisphereUp.Along(Sideways);

        up.TryGetValue(out UpAwayFrom away).Should().BeFalse();
        away.Should()
            .Be(
                default(UpAwayFrom),
                "the view of the other case is empty, so the direction does not leak out as a center"
            );
        (up is UpAwayFrom).Should().BeFalse();
    }

    [Fact]
    public void AnUpAwayFromACenterHasNoDirection()
    {
        var up = HemisphereUp.AwayFrom(Below);

        up.TryGetValue(out UpAlong along).Should().BeFalse();
        along
            .Should()
            .Be(
                default(UpAlong),
                "the view of the other case is empty, so the center does not leak out as a direction"
            );
        (up is UpAlong).Should().BeFalse();
    }

    [Fact]
    public void TheEvenUpHoldsNoCase()
    {
        HemisphereUp even = HemisphereUp.Even;

        even.HasValue.Should().BeFalse();
        even.TryGetValue(out UpAlong along).Should().BeFalse();
        even.TryGetValue(out UpAwayFrom away).Should().BeFalse();
        along.Should().Be(default(UpAlong));
        away.Should().Be(default(UpAwayFrom));
        even.Value.Should().BeNull();
        (even == default)
            .Should()
            .BeTrue("the even up is the all-zero default, so an instance made without an up has none");
    }

    [Fact]
    public void AnUpOfTheZeroVectorIsStillAnUpAndNotTheEvenUp()
    {
        HemisphereUp.Along(Vector3.Zero).HasValue.Should().BeTrue();
        HemisphereUp
            .AwayFrom(Vector3.Zero)
            .HasValue.Should()
            .BeTrue("a planet's own center is exactly zero from the planet");
        (HemisphereUp.Along(Vector3.Zero) == HemisphereUp.Even).Should().BeFalse();
        (HemisphereUp.AwayFrom(Vector3.Zero) == HemisphereUp.Even).Should().BeFalse();
    }

    [Fact]
    public void TheSameVectorAsADirectionAndAsACenterIsTwoDifferentUps()
    {
        var along = HemisphereUp.Along(Sideways);
        var away = HemisphereUp.AwayFrom(Sideways);

        (along == away).Should().BeFalse();
        (along != away).Should().BeTrue();
        along.Equals(away).Should().BeFalse();
        away.Equals(along).Should().BeFalse();
        along.Equals((object)away).Should().BeFalse();
    }

    [Fact]
    public void UpsOfTheSameCaseAndVectorAreEqualAndHashTheSame()
    {
        HemisphereUp.Along(Sideways).Equals(HemisphereUp.Along(Sideways)).Should().BeTrue();
        HemisphereUp.Along(Sideways).Equals((object)HemisphereUp.Along(Sideways)).Should().BeTrue();
        (HemisphereUp.Along(Sideways) != HemisphereUp.Along(Sideways)).Should().BeFalse();
        HemisphereUp.Along(Sideways).GetHashCode().Should().Be(HemisphereUp.Along(Sideways).GetHashCode());
        HemisphereUp.AwayFrom(Below).GetHashCode().Should().Be(HemisphereUp.AwayFrom(Below).GetHashCode());
        HemisphereUp.Even.GetHashCode().Should().Be(default(HemisphereUp).GetHashCode());
        (HemisphereUp.Along(Sideways) == HemisphereUp.Along(-Sideways)).Should().BeFalse();
        (HemisphereUp.AwayFrom(Below) == HemisphereUp.AwayFrom(-Below)).Should().BeFalse();
    }

    [Fact]
    public void AnUpThatHoldsANaNEqualsItselfAndAnyCopyAndHashesTheSame()
    {
        // Equals must be reflexive, even for a vector that is not a number: a value that is not equal to itself breaks
        // every set and every dictionary that holds it. So a NaN equals a NaN here, as it does in Vector3.Equals.
        var notANumber = new Vector3(float.NaN, 0, 1);
        HemisphereUp[] ups = [HemisphereUp.Along(notANumber), HemisphereUp.AwayFrom(notANumber)];

        foreach (HemisphereUp up in ups)
        {
            HemisphereUp copy = up.TryGetValue(out UpAlong along)
                ? HemisphereUp.Along(along.Direction)
                : HemisphereUp.AwayFrom(((UpAwayFrom)up.Value!).Center);

            up.Equals(up).Should().BeTrue($"{up} equals itself");
            up.Equals((object)up).Should().BeTrue($"{up} equals itself as an object");
            (up == copy).Should().BeTrue($"{up} equals a copy of itself");
            (up != copy).Should().BeFalse($"{up} does not differ from a copy of itself");
            up.GetHashCode().Should().Be(copy.GetHashCode(), $"{up} and its copy are equal, so they hash the same");
        }

        (ups[0] == ups[1]).Should().BeFalse("a direction and a center that hold the same vector are still two ups");
    }

    [Fact]
    public void ValueBoxesTheCaseForDebugging()
    {
        HemisphereUp.Along(Sideways).Value.Should().Be(new UpAlong(Sideways));
        HemisphereUp.AwayFrom(Below).Value.Should().Be(new UpAwayFrom(Below));
    }

    [Fact]
    public void ASwitchOverTheCasesTellsThemApart()
    {
        static string Describe(HemisphereUp up) =>
            up switch
            {
                UpAlong { Direction: var direction } => $"along {direction.X}",
                UpAwayFrom { Center: var center } => $"away from {center.Y}",
                null => "even",
            };

        Describe(HemisphereUp.Along(Sideways)).Should().Be("along 1");
        Describe(HemisphereUp.AwayFrom(Below)).Should().Be("away from -50");
        Describe(HemisphereUp.Even).Should().Be("even");
    }

    [Fact]
    public void AnUpTakesSixteenBytesBecauseEveryRenderInstanceCarriesOne() =>
        // One vector and a tag. Two vectors, one for each case, would make it 28 bytes for each of 20,000 instances.
        Unsafe.SizeOf<HemisphereUp>().Should().Be(16);

    [Fact]
    public void NoPublicMemberOfTheUnionExposesTheVectorOfOneCase()
    {
        // The vectors are reachable only through TryGetValue and a pattern match, which name the case. A public
        // property or field of the union could be read whichever case the up holds.
        const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;
        Type union = typeof(HemisphereUp);

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
            .BeEquivalentTo([typeof(UpAlong), typeof(UpAwayFrom)], "an up is made from one case and nothing else");
    }
}
