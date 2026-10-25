using JoltPhysicsSharp;

namespace Tertius.Physics.Tests;

/// <summary>
/// The object layer of every Jolt body holds 16 group bits and 16 mask bits, which Jolt's mask filter tests both ways:
/// two bodies collide when the groups of each one meet the mask of the other. The low 12 bits are content's collision
/// groups. The high 4 bits are the engine's roles: static, moving, frame hull and ghost. These tests check the encoding
/// against the halves that Jolt itself reads from a layer.
/// </summary>
public sealed class CollisionLayerTests
{
    private static readonly CollisionGroups Solid = CollisionGroups.Of(CollisionGroup.Solid);
    private static readonly CollisionMask InAnySolid = CollisionMask.Empty.InAny(CollisionGroup.Solid);

    private static readonly CollisionGroup[] ThreeGroups = MakeThreeGroups();

    /// <summary>The 8 subsets of <see cref="ThreeGroups"/>, by the bits 0 to 7. Bit i stands for group i.</summary>
    private static readonly int[] Subsets = [0, 1, 2, 3, 4, 5, 6, 7];

    public CollisionLayerTests()
    {
        JoltRuntime.EnsureInitialized();
    }

    /// <summary>The pair test of Jolt's <c>ObjectLayerPairFilterMask::ShouldCollide</c>, on the halves that Jolt reads.</summary>
    private static bool Collide(ObjectLayer a, ObjectLayer b) =>
        (ObjectLayerPairFilterMask.GetGroup(a) & ObjectLayerPairFilterMask.GetMask(b)) != 0
        && (ObjectLayerPairFilterMask.GetGroup(b) & ObjectLayerPairFilterMask.GetMask(a)) != 0;

    /// <summary>The kinds of Jolt body that the backend makes, each with the default groups and mask.</summary>
    public enum Kind
    {
        /// <summary>A static body, or the interior of a frame.</summary>
        Static,

        /// <summary>A dynamic or kinematic body, the inner body of a character, or the layer that a character moves with.</summary>
        Moving,
        FrameHull,
        Ghost,
    }

    private static ObjectLayer DefaultLayer(Kind kind) =>
        kind switch
        {
            Kind.Static => Layers.For(BodyLayer.Static, Solid, InAnySolid),
            Kind.Moving => Layers.For(BodyLayer.Moving, Solid, InAnySolid),
            Kind.FrameHull => Layers.FrameHull,
            Kind.Ghost => Layers.For(BodyLayer.Ghost, Solid, InAnySolid),
        };

    private static CollisionGroup[] MakeThreeGroups()
    {
        var registry = new CollisionGroupRegistry();
        return [CollisionGroup.Solid, registry.Register("B"), registry.Register("C")];
    }

    private static CollisionGroups GroupsOf(int bits)
    {
        CollisionGroups set = CollisionGroups.None;
        for (int i = 0; i < 3; i++)
            if ((bits & (1 << i)) != 0)
                set = set.And(ThreeGroups[i]);
        return set;
    }

    /// <summary>The mask whose any-of set is the subset <paramref name="bits"/>. The empty set is no condition.</summary>
    private static CollisionMask AnyOf(int bits)
    {
        CollisionMask mask = CollisionMask.Empty;
        for (int i = 0; i < 3; i++)
            if ((bits & (1 << i)) != 0)
                mask = mask.InAny(ThreeGroups[i]);
        return mask;
    }

    [Fact]
    public void JoltSplitsAnObjectLayerIntoSixteenGroupBitsAndSixteenMaskBits()
    {
        ObjectLayer layer = ObjectLayerPairFilterMask.GetObjectLayer(0x8001, 0x4002);

        ((uint)layer).Should().Be(0x4002_8001u, "joltc builds with 32-bit object layers, so each half has 16 bits");
        ObjectLayerPairFilterMask.GetGroup(layer).Should().Be(0x8001u);
        ObjectLayerPairFilterMask.GetMask(layer).Should().Be(0x4002u);
    }

    [Fact]
    public void ABodysContentGroupsAndAnyOfSetAreTheLowTwelveBitsOfItsLayer()
    {
        var registry = new CollisionGroupRegistry();
        CollisionGroup last = CollisionGroup.Solid;
        for (int i = 1; i < CollisionGroup.Capacity; i++)
            last = registry.Register($"Group{i}");
        CollisionGroups groups = CollisionGroups.Of(CollisionGroup.Solid).And(last);

        ObjectLayer layer = Layers.For(BodyLayer.Moving, groups, CollisionMask.Empty.InAny(last));

        (ObjectLayerPairFilterMask.GetGroup(layer) & 0xFFF).Should().Be(0x801u);
        (ObjectLayerPairFilterMask.GetMask(layer) & 0xFFF).Should().Be(0x800u);
    }

    /// <summary>
    /// The architecture's table, for every pair of kinds in which one body can be dynamic or is a character's layer. Jolt
    /// never makes a contact between two bodies that are not dynamic, so the pairs of two kinds that never are (static
    /// with static, static with ghost, ghost with ghost) do not matter: a ghost is kinematic, or static for a mesh that
    /// holds still.
    /// </summary>
    [Theory]
    [InlineData(Kind.Static, Kind.Moving, true)]
    [InlineData(Kind.Static, Kind.FrameHull, true)]
    [InlineData(Kind.Moving, Kind.Moving, true)]
    [InlineData(Kind.Moving, Kind.FrameHull, false)]
    [InlineData(Kind.Moving, Kind.Ghost, true)]
    [InlineData(Kind.FrameHull, Kind.FrameHull, true)]
    [InlineData(Kind.FrameHull, Kind.Ghost, false)]
    public void WithTheDefaultGroupsTheKindsCollideAsTheArchitectureSays(Kind a, Kind b, bool collide)
    {
        Collide(DefaultLayer(a), DefaultLayer(b)).Should().Be(collide);
        Collide(DefaultLayer(b), DefaultLayer(a)).Should().Be(collide);
    }

    [Theory]
    [InlineData(BodyLayer.Static, BodyLayer.Moving)]
    [InlineData(BodyLayer.Moving, BodyLayer.Moving)]
    [InlineData(BodyLayer.Moving, BodyLayer.Ghost)]
    public void TwoBodiesCollideExactlyWhenEachMaskMatchesTheGroupsOfTheOther(BodyLayer roleA, BodyLayer roleB)
    {
        foreach (int a in Subsets)
        foreach (int anyA in Subsets)
        foreach (int b in Subsets)
        foreach (int anyB in Subsets)
        {
            CollisionGroups groupsA = GroupsOf(a),
                groupsB = GroupsOf(b);
            CollisionMask maskA = AnyOf(anyA),
                maskB = AnyOf(anyB);
            bool expected = maskA.Matches(groupsB) && maskB.Matches(groupsA);

            bool actual = Collide(Layers.For(roleA, groupsA, maskA), Layers.For(roleB, groupsB, maskB));

            actual
                .Should()
                .Be(expected, "A is in {0} with {1}, and B is in {2} with {3}", groupsA, maskA, groupsB, maskB);
        }
    }

    [Fact]
    public void AFrameHullMeetsNoMovingBodyAndNoGhostWhateverTheirGroups()
    {
        foreach (int groups in Subsets)
        foreach (int any in Subsets)
        {
            Collide(Layers.FrameHull, Layers.For(BodyLayer.Moving, GroupsOf(groups), AnyOf(any)))
                .Should()
                .BeFalse("a moving body in {0} with {1}", GroupsOf(groups), AnyOf(any));
            Collide(Layers.FrameHull, Layers.For(BodyLayer.Ghost, GroupsOf(groups), AnyOf(any)))
                .Should()
                .BeFalse("a ghost in {0} with {1}", GroupsOf(groups), AnyOf(any));
        }
    }

    [Fact]
    public void AFrameHullMeetsEveryStaticBodyAndEveryOtherHullWhateverTheirGroups()
    {
        foreach (int groups in Subsets)
        foreach (int any in Subsets)
            Collide(Layers.FrameHull, Layers.For(BodyLayer.Static, GroupsOf(groups), AnyOf(any)))
                .Should()
                .BeTrue("a static body in {0} with {1}", GroupsOf(groups), AnyOf(any));
        Collide(Layers.FrameHull, Layers.FrameHull).Should().BeTrue();
    }

    [Fact]
    public void AFrameHullIgnoresTheGroupsAndTheMaskOfItsOwner()
    {
        var registry = new CollisionGroupRegistry();
        CollisionGroup ship = registry.Register("Ship");

        Layers
            .For(BodyLayer.FrameHull, CollisionGroups.Of(ship), CollisionMask.Empty.InAny(ship))
            .Should()
            .Be(Layers.FrameHull);
        Layers.For(BodyLayer.FrameHull, Solid, InAnySolid).Should().Be(Layers.FrameHull);
    }
}
