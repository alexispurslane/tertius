using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// A body is in a set of collision groups, and it has a collision mask: a filter over collision groups with an all-of
/// part, a none-of part and one any-of set. Content names the groups in a <see cref="CollisionGroupRegistry"/>, which
/// turns each name into one of <see cref="CollisionGroup.Capacity"/> groups. Every body starts in the group
/// <see cref="CollisionGroup.Solid"/> with the mask <c>InAny(Solid)</c>.
/// </summary>
public class CollisionGroupTests
{
    private static (CollisionGroup A, CollisionGroup B, CollisionGroup C) ThreeGroups()
    {
        var registry = new CollisionGroupRegistry();
        return (registry.Register("A"), registry.Register("B"), registry.Register("C"));
    }

    // ---- The registry ----

    [Fact]
    public void ANewRegistryHoldsOnlySolidAsTheFirstGroup()
    {
        var registry = new CollisionGroupRegistry();

        registry.Count.Should().Be(1);
        registry["Solid"].Should().Be(CollisionGroup.Solid);
        CollisionGroup.Solid.Index.Should().Be(0);
    }

    [Fact]
    public void EachNewNameGetsAGroupOfItsOwn()
    {
        var registry = new CollisionGroupRegistry();

        CollisionGroup debris = registry.Register("Debris");
        CollisionGroup people = registry.Register("People");

        new[] { CollisionGroup.Solid, debris, people }.Should().OnlyHaveUniqueItems();
        registry["Debris"].Should().Be(debris);
        registry["People"].Should().Be(people);
        registry.Count.Should().Be(3);
    }

    [Fact]
    public void ANameThatIsRegisteredAgainKeepsItsGroup()
    {
        var registry = new CollisionGroupRegistry();
        CollisionGroup debris = registry.Register("Debris");

        registry.Register("Debris").Should().Be(debris);
        registry.Register("Solid").Should().Be(CollisionGroup.Solid);
        registry.Count.Should().Be(2);
    }

    [Fact]
    public void ARegistryHoldsTwelveGroupsAndRefusesTheThirteenth()
    {
        CollisionGroup.Capacity.Should().Be(12, "the engine keeps 4 of Jolt's 16 group bits for its own roles");
        var registry = new CollisionGroupRegistry();
        for (int i = 1; i < CollisionGroup.Capacity; i++)
            registry.Register($"Group{i}");

        Action thirteenth = () => registry.Register("OneTooMany");

        thirteenth.Should().Throw<InvalidOperationException>().WithMessage("*OneTooMany*12*");
        registry.Count.Should().Be(CollisionGroup.Capacity);
        registry.TryGet("OneTooMany", out _).Should().BeFalse();
    }

    [Fact]
    public void ANameThatNoOneRegisteredHasNoGroup()
    {
        var registry = new CollisionGroupRegistry();

        registry.TryGet("Debris", out _).Should().BeFalse();
        Action lookUp = () => _ = registry["Debris"];
        lookUp.Should().Throw<KeyNotFoundException>().WithMessage("*Debris*");
    }

    [Fact]
    public void NamesAreCaseSensitive()
    {
        var registry = new CollisionGroupRegistry();
        CollisionGroup debris = registry.Register("Debris");

        registry.Register("debris").Should().NotBe(debris);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankNameIsRefused(string name)
    {
        var registry = new CollisionGroupRegistry();

        Action register = () => registry.Register(name);

        register.Should().Throw<ArgumentException>();
        registry.Count.Should().Be(1);
    }

    // ---- Sets of groups ----

    [Fact]
    public void AGroupSetContainsTheGroupsAddedToItAndNoOther()
    {
        var (a, b, c) = ThreeGroups();

        CollisionGroups groups = CollisionGroups.Of(a).And(b);

        groups.Contains(a).Should().BeTrue();
        groups.Contains(b).Should().BeTrue();
        groups.Contains(c).Should().BeFalse();
        groups.IsEmpty.Should().BeFalse();
        CollisionGroups.None.IsEmpty.Should().BeTrue();
        CollisionGroups.None.Contains(CollisionGroup.Solid).Should().BeFalse();
    }

    // ---- Masks ----

    [Fact]
    public void AnEmptyMaskIsNoConditionSoItMatchesEveryGroupSet()
    {
        var (a, b, _) = ThreeGroups();

        CollisionMask.Empty.Matches(CollisionGroups.None).Should().BeTrue();
        CollisionMask.Empty.Matches(CollisionGroups.Of(a)).Should().BeTrue();
        CollisionMask.Empty.Matches(CollisionGroups.Of(a).And(b)).Should().BeTrue();
    }

    [Fact]
    public void EachInAnyWidensTheOneAnyOfSet()
    {
        var (a, b, c) = ThreeGroups();

        CollisionMask mask = CollisionMask.Empty.InAny(a).InAny(b);

        mask.Matches(CollisionGroups.Of(a)).Should().BeTrue();
        mask.Matches(CollisionGroups.Of(b)).Should().BeTrue();
        mask.Matches(CollisionGroups.Of(b).And(c)).Should().BeTrue();
        mask.Matches(CollisionGroups.Of(c)).Should().BeFalse();
        mask.Matches(CollisionGroups.None).Should().BeFalse("a body in no group of a non-empty any-of set fails it");
        mask.AnyOf.Should().Be(CollisionGroups.Of(a).And(b));
    }

    [Fact]
    public void InAllNeedsEveryGroupThatItNames()
    {
        var (a, b, c) = ThreeGroups();

        CollisionMask mask = CollisionMask.Empty.InAll(a).InAll(b);

        mask.Matches(CollisionGroups.Of(a).And(b)).Should().BeTrue();
        mask.Matches(CollisionGroups.Of(a).And(b).And(c)).Should().BeTrue();
        mask.Matches(CollisionGroups.Of(a)).Should().BeFalse();
        mask.Matches(CollisionGroups.Of(b)).Should().BeFalse();
    }

    [Fact]
    public void InNoneRefusesEveryGroupThatItNames()
    {
        var (a, b, c) = ThreeGroups();

        CollisionMask mask = CollisionMask.Empty.InNone(a).InNone(b);

        mask.Matches(CollisionGroups.Of(c)).Should().BeTrue();
        mask.Matches(CollisionGroups.None).Should().BeTrue();
        mask.Matches(CollisionGroups.Of(a)).Should().BeFalse();
        mask.Matches(CollisionGroups.Of(b).And(c)).Should().BeFalse();
    }

    [Fact]
    public void AMaskMatchesOnlyWhenEveryPartMatches()
    {
        var (a, b, c) = ThreeGroups();

        CollisionMask mask = CollisionMask.Empty.InAll(a).InNone(b).InAny(c);

        mask.Matches(CollisionGroups.Of(a).And(c)).Should().BeTrue();
        mask.Matches(CollisionGroups.Of(c)).Should().BeFalse("the all-of part fails");
        mask.Matches(CollisionGroups.Of(a).And(b).And(c)).Should().BeFalse("the none-of part fails");
        mask.Matches(CollisionGroups.Of(a)).Should().BeFalse("the any-of part fails");
    }

    // ---- A body's groups and mask ----

    [Fact]
    public void ABodyStartsInSolidWithTheMaskInAnySolid()
    {
        var desc = new BodyDesc(new SphereShapeDesc(1));

        desc.CollisionGroups.Should().Be(CollisionGroups.Of(CollisionGroup.Solid));
        desc.CollisionMask.Should().Be(CollisionMask.Empty.InAny(CollisionGroup.Solid));
    }

    [Fact]
    public void ABodyCanBeInSeveralGroupsAndCollideWithSeveral()
    {
        var (a, b, c) = ThreeGroups();

        var desc = new BodyDesc(new SphereShapeDesc(1))
        {
            CollisionGroups = CollisionGroups.Of(a).And(b),
            CollisionMask = CollisionMask.Empty.InAny(b).InAny(c),
        };

        desc.Problem(BodyRole.OwnsNoFrame).Should().BeNull();
    }

    [Theory]
    [InlineData(BodyRole.OwnsNoFrame)]
    [InlineData(BodyRole.Hull)]
    [InlineData(BodyRole.OwnsRootFrame)]
    public void ABodyWhoseMaskHasAnAllOfPartIsRefused(BodyRole role)
    {
        var desc = new BodyDesc(new BoxShapeDesc(new Vector3(1))) { CollisionMask = CollisionMask.Empty.InAny(CollisionGroup.Solid).InAll(CollisionGroup.Solid) };

        desc.Problem(role).Should().Contain("all-of");
    }

    [Theory]
    [InlineData(BodyRole.OwnsNoFrame)]
    [InlineData(BodyRole.Hull)]
    [InlineData(BodyRole.OwnsRootFrame)]
    public void ABodyWhoseMaskHasANoneOfPartIsRefused(BodyRole role)
    {
        var desc = new BodyDesc(new BoxShapeDesc(new Vector3(1))) { CollisionMask = CollisionMask.Empty.InNone(CollisionGroup.Solid) };

        desc.Problem(role).Should().Contain("none-of");
    }
}
