using System.Collections.Immutable;
using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>One joint's part of a <see cref="RigSway"/>.</summary>
/// <param name="Joint">The joint's index in the rig.</param>
/// <param name="Axis">The unit axis that the joint turns about, in its parent joint's axes, or the model's for a root joint.</param>
/// <param name="Amplitude">The largest turn, in radians.</param>
public readonly record struct JointSway(int Joint, Vector3 Axis, float Amplitude);

/// <summary>One morph's part of a <see cref="RigSway"/>.</summary>
/// <param name="Morph">The morph's index in the rig.</param>
/// <param name="Center">The weight about which the morph sways.</param>
/// <param name="Amplitude">The largest change of the weight from <paramref name="Center"/>.</param>
public readonly record struct MorphSway(int Morph, float Center, float Amplitude);

/// <summary>
/// A demo component that sways its own entity's rig pose by the time, the same on every run. With the sway s =
/// sin(2π t / <see cref="Period"/> + <see cref="Phase"/>), each joint of <see cref="Joints"/> turns by its amplitude times
/// s about its axis after its rest rotation, and each morph of <see cref="Morphs"/> takes its center plus its amplitude
/// times s. Every other joint and morph keeps <see cref="Rest"/>. In each tick it writes the pose of the tick's end, the
/// time of the snapshot that the tick's sync point publishes.
/// </summary>
public sealed class RigSway : IComponent
{
    /// <summary>The rig pose that the sways start from, which fits the entity's rig.</summary>
    public required RigPose Rest { get; init; }

    /// <summary>The time of one sway to each side and back, in seconds.</summary>
    public required double Period { get; init; }

    /// <summary>The sway's phase at time 0, in radians.</summary>
    public required double Phase { get; init; }

    public required ImmutableArray<JointSway> Joints { get; init; }

    public required ImmutableArray<MorphSway> Morphs { get; init; }

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    /// <summary>The rig pose at <paramref name="time"/>, in seconds: the pose to spawn the entity with.</summary>
    public RigPose PoseAt(double time)
    {
        Pose[] joints = [.. Rest.Joints];
        float[] weights = [.. Rest.Weights];
        Write(time: time, joints: joints, weights: weights);
        return new([.. joints], [.. weights]);
    }

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        RigPoseWriter pose = self.RigPose;
        Write(time: self.TimeAt(self.Tick + 1), joints: pose.Joints, weights: pose.Weights);
    }

    /// <summary>Writes the swayed joints and weights at <paramref name="time"/> over <paramref name="joints"/> and <paramref name="weights"/>.</summary>
    private void Write(double time, Span<Pose> joints, Span<float> weights)
    {
        float sway = (float)Math.Sin(2 * Math.PI * time / Period + Phase);
        foreach (JointSway joint in Joints)
        {
            Pose rest = Rest.Joints[joint.Joint];
            var turn = Quaternion.CreateFromAxisAngle(joint.Axis, joint.Amplitude * sway);
            joints[joint.Joint] = rest with { Rotation = turn * rest.Rotation };
        }

        foreach (MorphSway morph in Morphs)
            weights[morph.Morph] = morph.Center + morph.Amplitude * sway;
    }
}
