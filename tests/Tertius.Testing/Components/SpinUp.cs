using Tertius.Frames.Rails;
using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Turns its entity in place about its parent's +Y with a constant angular acceleration, from rest at
/// <see cref="StartTime"/>: a frame that spins up (docs/plans/spin-gravity.md, "Acceptance tests"). The engine needs no rails
/// type for a spin-up, so a test component writes its full samples. At a time t after the start the entity has turned by
/// half the rate times t², and turns at the rate times t. In each tick it writes the sample of the step in which the tick's
/// intents act, at the place where the entity starts that step, as <see cref="SpinMotion"/> does. Before the start it holds
/// still.
/// </summary>
public sealed class SpinUp(double angularAcceleration, double startTime) : IComponent
{
    /// <summary>The angular acceleration about the parent's +Y, in rad/s².</summary>
    public double AngularAcceleration => angularAcceleration;

    /// <summary>The time at which the entity starts to turn from rest, in s.</summary>
    public double StartTime => startTime;

    public int Priority => -1000;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        self.MoveOnRails(SampleAt(self.PoseAtNextStepStartIn(self.Frame).Position, self.TimeAt(self.Tick + 2),
            self.DeltaTime));

    /// <summary>The entity's rotation in its parent at <paramref name="time"/>.</summary>
    public DQuaternion RotationAt(double time) =>
        DQuaternion.FromAxisAngle(Double3.UnitY, 0.5 * angularAcceleration * Since(time) * Since(time));

    /// <summary>The entity's angular velocity in its parent's axes at <paramref name="time"/>, in rad/s.</summary>
    public Double3 AngularVelocityAt(double time) => new(0, angularAcceleration * Since(time), 0);

    /// <summary>
    /// The full sample of the step of <paramref name="step"/> seconds that ends at <paramref name="stepEnd"/>, at
    /// <paramref name="place"/>: the rotation and the angular velocity there, and the mean angular acceleration over the step.
    /// </summary>
    public MotionSample SampleAt(Double3 place, double stepEnd, double step) =>
        new(Pose: new(place, RotationAt(stepEnd)), ChordVelocity: Double3.Zero, Velocity: Double3.Zero,
            Acceleration: Double3.Zero, AngularVelocity: AngularVelocityAt(stepEnd),
            AngularAcceleration: (AngularVelocityAt(stepEnd) - AngularVelocityAt(stepEnd - step)) / step);

    private double Since(double time) => Math.Max(0, time - startTime);

    /// <summary>
    /// A copy of <paramref name="desc"/>, for a spawn outside a tick in <paramref name="world"/>, whose entity spins up at
    /// <paramref name="angularAcceleration"/> from the world's time now: a <see cref="SpinUp"/> comes before the other
    /// components, and the first motion intent is the sample of the entity's first step (see <see cref="SpawnDesc.FirstMove"/>
    /// ),
    /// so the entity turns from its first step on. The spawn takes no velocities: only the samples turn the entity.
    /// </summary>
    public static SpawnDesc Spawning(SpawnDesc desc, World world, double angularAcceleration)
    {
        var spin = new SpinUp(angularAcceleration, world.Time);
        return new()
        {
            Frame = desc.Frame,
            Pose = new(desc.Pose.Position, spin.RotationAt(world.Time)),
            FirstMove = new(spin.SampleAt(desc.Pose.Position, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
            Body = desc.Body,
            Render = desc.Render,
            Components = [spin, .. desc.Components ?? []],
            AsFrame = desc.AsFrame,
            AttachTo = desc.AttachTo,
            AttachOffset = desc.AttachOffset,
            Subscriptions = desc.Subscriptions
        };
    }
}
