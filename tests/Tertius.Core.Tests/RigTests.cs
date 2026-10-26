using System.Collections.Immutable;
using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// A rig (docs/plans/skinning-and-morphs.md, "The mesh data"): what its constructor refuses, its rest pose, and the
/// lookup of a joint or a morph by name.
/// </summary>
public sealed class RigTests
{
    private static readonly Pose Raised = new(new(0, 0.5f, 0), Quaternion.Identity);

    [Fact]
    public void RigRefusesAJointWhoseParentIsNotBeforeIt()
    {
        // A joint that is its own parent is the nearest parent that does not come before it.
        ImmutableArray<RigJoint> joints =
        [
            new(Name: "Base", Parent: null, Rest: Pose.Identity),
            new(Name: "Tip", Parent: 1, Rest: Raised),
        ];

        Action make = () => _ = new Rig(joints, []);

        make.Should().Throw<ArgumentException>().WithMessage("*Tip*parent*");
    }

    [Fact]
    public void RigRefusesTwoJointsWithOneName()
    {
        ImmutableArray<RigJoint> joints =
        [
            new(Name: "Base", Parent: null, Rest: Pose.Identity),
            new(Name: "Base", Parent: 0, Rest: Raised),
        ];

        Action make = () => _ = new Rig(joints, []);

        make.Should().Throw<ArgumentException>().WithMessage("*joints*Base*");
    }

    [Fact]
    public void RigRefusesTwoMorphsWithOneName()
    {
        ImmutableArray<RigMorph> morphs = [new(Name: "Smile", Rest: 0), new(Name: "Smile", Rest: 0)];

        Action make = () => _ = new Rig([], morphs);

        make.Should().Throw<ArgumentException>().WithMessage("*morphs*Smile*");
    }

    [Fact]
    public void RigRefusesARestPoseThatIsNotFinite()
    {
        ImmutableArray<RigJoint> joints =
        [
            new(Name: "Base", Parent: null, Rest: new(new(0, float.NaN, 0), Quaternion.Identity)),
        ];

        Action make = () => _ = new Rig(joints, []);

        make.Should().Throw<ArgumentException>().WithMessage("*Base*not finite*");
    }

    [Fact]
    public void RigRefusesARestRotationThatIsNotARotation()
    {
        // Its squared length, 1e-14, is finite and above 0, but at most 1e-12, the least that the renderer normalizes.
        ImmutableArray<RigJoint> joints =
        [
            new(Name: "Base", Parent: null, Rest: new(Vector3.Zero, new(x: 0, y: 0, z: 0, w: 1e-7f))),
        ];

        Action make = () => _ = new Rig(joints, []);

        make.Should().Throw<ArgumentException>().WithMessage("*Base*not a rotation*");
    }

    [Fact]
    public void RigRefusesARestWeightThatIsNotFinite()
    {
        ImmutableArray<RigMorph> morphs = [new(Name: "Smile", Rest: float.PositiveInfinity)];

        Action make = () => _ = new Rig([], morphs);

        make.Should().Throw<ArgumentException>().WithMessage("*Smile*not finite*");
    }

    [Fact]
    public void RigRefusesMoreJointsThanMaxJoints()
    {
        ImmutableArray<RigJoint> joints =
        [
            .. Enumerable
                .Range(0, Rig.MaxJoints + 1)
                .Select(index => new RigJoint(Name: $"Joint{index}", Parent: null, Rest: Pose.Identity)),
        ];

        Action make = () => _ = new Rig(joints, []);

        make.Should().Throw<ArgumentException>().WithMessage("*at most 65535 joints*");
    }

    [Fact]
    public void RestHoldsEachJointsRestPoseAndEachMorphsRestWeight()
    {
        // A rotation of length 2 shows that the rest keeps each pose as given, and does not normalize it.
        var bent = new Pose(new(1, 2, 3), new(x: 0, y: 0, z: 1.2f, w: 1.6f));
        var rig = new Rig(
            [new(Name: "Base", Parent: null, Rest: Raised), new(Name: "Tip", Parent: 0, Rest: bent)],
            [new(Name: "Smile", Rest: 0.25f), new(Name: "Frown", Rest: -1)]
        );

        (joints: rig.Rest.Joints.ToArray(), weights: rig.Rest.Weights.ToArray())
            .Should()
            .BeEquivalentTo(
                (joints: new[] { Raised, bent }, weights: new[] { 0.25f, -1f }),
                options => options.WithStrictOrdering()
            );
    }

    [Fact]
    public void JointNamedFindsTheJointOfAName()
    {
        var rig = new Rig(
            [new(Name: "Base", Parent: null, Rest: Pose.Identity), new(Name: "Tip", Parent: 0, Rest: Raised)],
            []
        );

        rig.JointNamed("Tip").Should().Be(1);
    }

    [Fact]
    public void JointNamedIsNullForANameOfNoJoint()
    {
        // A morph's name is not a joint's name.
        var rig = new Rig([new(Name: "Base", Parent: null, Rest: Pose.Identity)], [new(Name: "Smile", Rest: 0)]);

        rig.JointNamed("Smile").Should().BeNull();
    }

    [Fact]
    public void MorphNamedFindsTheMorphOfAName()
    {
        var rig = new Rig(
            [new(Name: "Smile", Parent: null, Rest: Pose.Identity)],
            [new(Name: "Frown", Rest: 0), new(Name: "Smile", Rest: 0)]
        );

        rig.MorphNamed("Smile").Should().Be(1);
    }
}
