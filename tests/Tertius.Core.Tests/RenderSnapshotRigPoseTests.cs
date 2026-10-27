using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The rig poses of a render snapshot (docs/plans/skinning-and-morphs.md, "The rig pose in the snapshot"): the one writer,
/// <see cref="RenderSnapshot.AddRigPose"/>, what it refuses, the four reads of an entry, and <see cref="RenderSnapshot.Clear"/>.
/// </summary>
public sealed class RenderSnapshotRigPoseTests
{
    private static readonly Pose Raised = new(new(0, 0.5f, 0), Quaternion.Identity);
    private static readonly Pose Turned = new(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f));
    private static readonly Pose Shifted = new(new(0.25f, 0, 0), Quaternion.Identity);

    /// <summary>A snapshot with <paramref name="count"/> instances and no rig poses.</summary>
    private static RenderSnapshot WithInstances(int count)
    {
        var snapshot = new RenderSnapshot();
        for (int i = 0; i < count; i++)
            snapshot.Instances.Add(new RenderInstance { EntityIndex = (uint)i });
        return snapshot;
    }

    [Fact]
    public void AddRigPoseKeepsTheCurrentAndThePreviousApart()
    {
        // The entry before the one under test puts the second entry's spans away from the starts of the lists.
        RenderSnapshot snapshot = WithInstances(3);
        snapshot.AddRigPose(
            instance: 0,
            joints: [Pose.Identity],
            previousJoints: [Pose.Identity],
            weights: [0.5f],
            previousWeights: [0.5f]
        );

        snapshot.AddRigPose(
            instance: 2,
            joints: [Raised, Turned],
            previousJoints: [Shifted, Pose.Identity],
            weights: [0.75f, -0.5f],
            previousWeights: [0.25f, 0]
        );

        RenderRigPose entry = snapshot.RigPoses[1];
        (
            instance: entry.Instance,
            joints: snapshot.JointsOf(entry).ToArray(),
            previousJoints: snapshot.PreviousJointsOf(entry).ToArray(),
            weights: snapshot.WeightsOf(entry).ToArray(),
            previousWeights: snapshot.PreviousWeightsOf(entry).ToArray()
        )
            .Should()
            .BeEquivalentTo(
                (
                    instance: 2,
                    joints: new[] { Raised, Turned },
                    previousJoints: new[] { Shifted, Pose.Identity },
                    weights: new[] { 0.75f, -0.5f },
                    previousWeights: new[] { 0.25f, 0f }
                ),
                options => options.WithStrictOrdering()
            );
    }

    [Fact]
    public void AddRigPoseRefusesAnInstanceThatIsNotAfterTheLastEntry()
    {
        RenderSnapshot snapshot = WithInstances(3);
        snapshot.AddRigPose(instance: 1, joints: [], previousJoints: [], weights: [0.5f], previousWeights: [0.5f]);

        Action addAgain = () =>
            snapshot.AddRigPose(instance: 1, joints: [], previousJoints: [], weights: [0.5f], previousWeights: [0.5f]);

        addAgain.Should().Throw<ArgumentException>().WithParameterName("instance");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void AddRigPoseRefusesAnInstanceOutsideTheInstances(int instance)
    {
        RenderSnapshot snapshot = WithInstances(2);

        Action add = () =>
            snapshot.AddRigPose(
                instance: instance,
                joints: [],
                previousJoints: [],
                weights: [0.5f],
                previousWeights: [0.5f]
            );

        add.Should().Throw<ArgumentException>().WithParameterName("instance");
    }

    [Theory]
    [InlineData(1, 2, "previousJoints")]
    [InlineData(2, 1, "previousWeights")]
    public void AddRigPoseRefusesSpansOfDifferentLengths(int previousJoints, int previousWeights, string refused)
    {
        RenderSnapshot snapshot = WithInstances(1);
        Pose[] joints = [Raised, Turned];
        float[] weights = [0.75f, -0.5f];

        Action add = () =>
            snapshot.AddRigPose(
                instance: 0,
                joints: joints,
                previousJoints: joints.AsSpan(0, previousJoints),
                weights: weights,
                previousWeights: weights.AsSpan(0, previousWeights)
            );

        add.Should().Throw<ArgumentException>().WithParameterName(refused);
    }

    [Fact]
    public void ClearEmptiesTheRigPoses()
    {
        RenderSnapshot snapshot = WithInstances(1);
        snapshot.AddRigPose(
            instance: 0,
            joints: [Raised],
            previousJoints: [Shifted],
            weights: [0.5f],
            previousWeights: [0.25f]
        );

        snapshot.Clear();

        (snapshot.RigPoses.Count, snapshot.JointPoses.Count, snapshot.MorphWeights.Count).Should().Be((0, 0, 0));
    }
}
