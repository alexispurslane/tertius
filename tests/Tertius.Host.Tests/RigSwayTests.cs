using System.Collections.Immutable;
using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo component <see cref="RigSway"/>: the rig pose that it gives at a time, and that it writes its own entity's
/// rig pose for the end of each tick.
/// </summary>
public sealed class RigSwayTests
{
    private const double Period = 4;

    /// <summary>A rest of two joints, the second turned and moved off its parent, and two morphs.</summary>
    private static readonly RigPose Rest = new(
        [Pose.Identity, new(new(0, 1, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f))],
        [0.25f, -0.5f]
    );

    private static RigSway Sway(ImmutableArray<JointSway> joints, ImmutableArray<MorphSway> morphs, double phase = 0) =>
        new()
        {
            Rest = Rest,
            Period = Period,
            Phase = phase,
            Joints = joints,
            Morphs = morphs,
        };

    [Fact]
    public void AJointSwayTurnsItsJointByItsAmplitudeAboutItsAxisAfterItsRestRotationAQuarterPeriodIn()
    {
        RigSway sway = Sway(joints: [new(Joint: 1, Axis: Vector3.UnitZ, Amplitude: 0.6f)], morphs: []);

        Pose tip = sway.PoseAt(Period / 4).Joints[1];

        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.6f) * Rest.Joints[1].Rotation;
        tip.Position.Should().Be(Rest.Joints[1].Position, "a sway turns a joint and never moves it");
        Quaternion.Dot(tip.Rotation, expected).Should().BeApproximately(1, 1e-6f);
    }

    [Fact]
    public void AJointSwayTurnsItsJointTheOtherWayThreeQuartersOfAPeriodIn()
    {
        RigSway sway = Sway(joints: [new(Joint: 1, Axis: Vector3.UnitZ, Amplitude: 0.6f)], morphs: []);

        Pose tip = sway.PoseAt(3 * Period / 4).Joints[1];

        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.6f) * Rest.Joints[1].Rotation;
        Quaternion.Dot(tip.Rotation, expected).Should().BeApproximately(1, 1e-6f);
    }

    [Theory]
    [InlineData(Period / 4, 1.2f)]
    [InlineData(3 * Period / 4, 0.2f)]
    [InlineData(Period, 0.7f)]
    public void AMorphSwayGivesItsCenterPlusItsAmplitudeTimesTheSineOfItsPhase(double time, float weight)
    {
        RigSway sway = Sway(joints: [], morphs: [new(Morph: 1, Center: 0.7f, Amplitude: 0.5f)]);

        float[] weights = [.. sway.PoseAt(time).Weights];

        weights[1].Should().BeApproximately(weight, 1e-6f);
    }

    [Fact]
    public void ThePhaseShiftsTheSwayAlongTheTime()
    {
        RigSway sway = Sway(joints: [], morphs: [new(Morph: 0, Center: 0, Amplitude: 1)], phase: Math.PI / 2);

        float weight = sway.PoseAt(0).Weights[0];

        weight.Should().BeApproximately(1, 1e-6f, "a quarter turn of phase is a quarter period of time");
    }

    [Fact]
    public void AJointAndAMorphThatNoSwayNamesKeepTheirRest()
    {
        RigSway sway = Sway(
            joints: [new(Joint: 1, Axis: Vector3.UnitZ, Amplitude: 0.6f)],
            morphs: [new(Morph: 1, Center: 0, Amplitude: 1)]
        );

        RigPose pose = sway.PoseAt(Period / 4);

        pose.Joints[0].Should().Be(Rest.Joints[0]);
        pose.Weights[0].Should().Be(Rest.Weights[0]);
    }

    [Fact]
    public void TheSwayWritesItsEntitysRigPoseForTheEndOfEachTick()
    {
        using World world = NewJoltWorld();
        FrameHandle room = world.CreateRootFrame(new(FrameBounds.Sphere(1e3f)));
        // The snapshot holds the frames that the camera sees.
        world.Camera = world.Spawn(
            new()
            {
                Frame = room,
                Pose = new(new(0, 0, 5), DQuaternion.Identity),
                Render = new RenderData(),
            }
        );
        RigSway sway = Sway(
            joints: [new(Joint: 1, Axis: Vector3.UnitX, Amplitude: 0.4f)],
            morphs: [new(Morph: 0, Center: 0.5f, Amplitude: 0.5f)]
        );
        world.Spawn(
            new()
            {
                Frame = room,
                Render = new RenderData { Mesh = new(1), Flags = RenderFlags.Visible },
                RigPose = sway.PoseAt(world.Time),
                Components = [sway],
            }
        );

        for (int i = 0; i < 7; i++)
            world.Tick();
        RenderSnapshot snapshot = world.RenderSnapshots.AcquireLatest() ?? world.RenderSnapshots.ReadBuffer;

        RigPose end = sway.PoseAt(snapshot.Time);
        RenderRigPose entry = snapshot.RigPoses.Single();
        snapshot.JointsOf(entry).ToArray().Should().Equal(end.Joints);
        snapshot.WeightsOf(entry).ToArray().Should().Equal(end.Weights);
    }
}
