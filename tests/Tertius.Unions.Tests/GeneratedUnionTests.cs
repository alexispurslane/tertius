using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Tertius.Unions.Tests;

/// <summary>
/// The members that the generator writes for a <see cref="GeneratedUnionAttribute"/> struct: each case goes in and comes
/// back out unchanged, the empty value holds no case, and equality and <c>ToString</c> look at the held case only.
/// </summary>
public sealed class GeneratedUnionTests
{
    private static readonly Vector3 Sideways = new(1, 0, 0);

    [Fact]
    public void EachCaseComesBackOutOfTryGetValue()
    {
        var point = new Point(Position: new(1, 2, 3), Color: new(0.5f, 0.25f, 1), Intensity: 4, Range: 9);
        var spot = new Spot(
            Position: new(-1, 0, 2),
            Direction: Sideways,
            Color: new(1, 1, 0),
            Intensity: 2,
            Range: 30,
            Inner: 0.1f,
            Outer: 0.4f
        );

        new Light(point).TryGetValue(out Point pointBack).Should().BeTrue();
        new Light(spot).TryGetValue(out Spot spotBack).Should().BeTrue();

        pointBack.Should().Be(point);
        spotBack.Should().Be(spot);
    }

    [Fact]
    public void APatternMatchReadsTheHeldCase()
    {
        var up = new Up(new AwayFrom(Sideways));

        string read = up switch
        {
            Along { Direction: var direction } => $"along {direction}",
            AwayFrom { Center: var center } => $"away from {center}",
            null => "none",
        };

        read.Should().Be($"away from {Sideways}");
    }

    [Fact]
    public void TryGetValueOfAnotherCaseIsFalseAndGivesTheDefault()
    {
        var up = new Up(new Along(Sideways));

        up.TryGetValue(out AwayFrom away).Should().BeFalse();
        away.Should().Be(default(AwayFrom), "the slot that the two cases share holds the direction, not a center");
    }

    [Fact]
    public void TheDefaultValueHoldsNoCase()
    {
        Up up = default;

        up.HasValue.Should().BeFalse();
        up.Value.Should().BeNull();
        up.TryGetValue(out Along _).Should().BeFalse();
        up.TryGetValue(out AwayFrom _).Should().BeFalse();
    }

    [Fact]
    public void AValueWithACaseHasAValue()
    {
        new Up(new Along(Sideways)).HasValue.Should().BeTrue();
        new Odd(new Empty()).HasValue.Should().BeTrue("a case with no values is still a case");
    }

    [Fact]
    public void ACaseConvertsToTheUnionImplicitly()
    {
        Light light = new Point(Position: Vector3.One, Color: Vector3.One, Intensity: 1, Range: 1);

        light.TryGetValue(out Point _).Should().BeTrue();
    }

    [Fact]
    public void ValueBoxesTheHeldCase()
    {
        var tagged = new Tagged("crate", 3);

        new Label(tagged).Value.Should().Be(tagged);
    }

    [Fact]
    public void TwoValuesOfOneCaseWithEqualFieldsAreEqual()
    {
        var a = new Label(new Tagged("crate", 3));
        var b = new Label(new Tagged("crate", 3));

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void TwoValuesOfOneCaseWithDifferentFieldsAreNotEqual()
    {
        var a = new Label(new Tagged("crate", 3));
        var b = new Label(new Tagged("crate", 4));

        a.Equals(b).Should().BeFalse();
        (a == b).Should().BeFalse();
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void TwoCasesWhoseSharedSlotHoldsTheSameVectorAreNotEqual()
    {
        var along = new Up(new Along(Sideways));
        var away = new Up(new AwayFrom(Sideways));

        along.Equals(away).Should().BeFalse("a direction is not a center, though both sit in one slot");
    }

    [Fact]
    public void ACaseWithANaNEqualsItself()
    {
        var up = new Up(new Along(new(float.NaN, 0, 0)));

        up.Equals(up).Should().BeTrue("each value compares with its own Equals, as the record does");
    }

    [Fact]
    public void TheEmptyValueEqualsItselfAndNoCase()
    {
        Up none = default;

        none.Equals(default).Should().BeTrue();
        none.Equals(new(new Along(Vector3.Zero))).Should().BeFalse("a zero direction is still a case");
    }

    [Fact]
    public void EqualsOfAnObjectComparesABoxedUnionOnly()
    {
        var up = new Up(new Along(Sideways));

        up.Equals((object)new Up(new Along(Sideways))).Should().BeTrue();
        up.Equals((object)new Along(Sideways)).Should().BeFalse("a boxed case is not its union");
        up.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void ToStringNamesTheUnionAndTheHeldCase()
    {
        new Up(new Along(Sideways)).ToString().Should().Be($"Up({new Along(Sideways)})");
        default(Up).ToString().Should().Be("Up(none)");
    }

    [Fact]
    public void SlotsAreSharedByType()
    {
        Unsafe.SizeOf<Up>().Should().Be(16, "one Vector3 slot and the kind");
        Unsafe.SizeOf<Light>().Should().Be(56, "three Vector3 slots, four float slots and the kind");
        Unsafe.SizeOf<Mixed>().Should().Be(16, "one double slot, one int slot and the kind");
        Unsafe.SizeOf<Label>().Should().Be(16, "one string slot, one int slot and the kind");
    }

    [Fact]
    public void TheMostAlignedSlotsComeFirst() =>
        Unsafe
            .SizeOf<NarrowFirst>()
            .Should()
            .Be(
                16,
                "the double slot comes before the int slot, though the first case names the int first, so no padding sits between them"
            );

    [Fact]
    public void TheHashDependsOnTheValuesOfTheHeldCase()
    {
        int[] hashes = [.. Enumerable.Range(0, 8).Select(count => new Label(new Tagged("crate", count)).GetHashCode())];

        hashes.Should().OnlyHaveUniqueItems("eight values of one case almost never share a hash");
    }

    [Fact]
    public void ACaseWithStateBesideItsParametersComesBackWhole()
    {
        var extra = new Extra(2) { Hidden = 5 };

        new Odd(extra).TryGetValue(out Extra back).Should().BeTrue();

        back.Hidden.Should().Be(5);
    }

    [Fact]
    public void ACaseWhosePropertyChangesItsParameterComesBackWithTheSameBits()
    {
        var bumped = new Bumped(1);

        new Odd(bumped).TryGetValue(out Bumped back).Should().BeTrue();

        back.Count.Should().Be(2, "the case is stored whole, not built again from its property");
    }

    [Fact]
    public void APlainStructCaseComesBackWhole()
    {
        var plain = new Plain { A = 3, B = -7 };

        new Odd(plain).TryGetValue(out Plain back).Should().BeTrue();

        back.A.Should().Be(3);
        back.B.Should().Be(-7);
    }

    [Fact]
    public void TwoValuesOfACaseWithNoValuesAreEqual() => new Odd(new Empty()).Should().Be(new Odd(new Empty()));

    [Fact]
    public void TheAllZeroValueOfAUnionWithAZeroCaseHoldsThatCase()
    {
        Machine machine = default;

        machine.HasValue.Should().BeTrue();
        machine.TryGetValue(out Idle _).Should().BeTrue();
        machine.TryGetValue(out Busy _).Should().BeFalse();
        machine.Value.Should().Be(new Idle());
        machine.ToString().Should().Be($"Machine({new Idle()})");
    }

    /// <summary>A union with a zero case has no empty value, so its <c>Value</c> is never null, and its type says so.</summary>
    [Fact]
    public void TheValueOfAUnionWithAZeroCaseIsNeverNull()
    {
        var nullability = new NullabilityInfoContext();

        nullability
            .Create(typeof(Machine).GetProperty(nameof(Machine.Value))!)
            .ReadState.Should()
            .Be(NullabilityState.NotNull);
        nullability
            .Create(typeof(Label).GetProperty(nameof(Label.Value))!)
            .ReadState.Should()
            .Be(NullabilityState.Nullable, "a union without a zero case has the empty value");
    }

    [Fact]
    public void AUnionWithAZeroCaseHoldsItsOtherCases()
    {
        var machine = new Machine(new Busy(12));

        machine.TryGetValue(out Busy busy).Should().BeTrue();
        busy.Ticks.Should().Be(12);
        machine.TryGetValue(out Idle _).Should().BeFalse();
        machine.Should().NotBe(default(Machine));
    }

    [Fact]
    public void ANestedUnionHoldsItsCases()
    {
        var inner = new Holder.Inner(new Busy(4));

        inner.TryGetValue(out Busy busy).Should().BeTrue();
        busy.Ticks.Should().Be(4);
    }
}
