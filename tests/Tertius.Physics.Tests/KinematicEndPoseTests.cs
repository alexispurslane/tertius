using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Physics moves every kinematic body that a motion intent moves by one branch: it aims the body at the pose of the intent,
/// the end pose or the full sample's pose, and puts it exactly there after the Jolt update, because Jolt reaches a kinematic
/// target only to the float rounding of the velocity that it gives the body (docs/plans/rails-components.md, "MoveKinematic
/// as an end pose"). The tests run the real World on the real Jolt backend.
/// </summary>
public sealed class KinematicEndPoseTests
{
    private static readonly Double3 Start = new(12_345.678, -910.11, 42.4242);

    // Not a round number in any axis, so that Jolt's float velocity cannot carry the body exactly onto its end pose.
    private static readonly Double3 Velocity = new(301.37, 17.71, -3.29);

    /// <summary>
    /// Moves its entity along a line at <see cref="Velocity"/> from <see cref="Start"/> at the time <paramref name="t0"/>: in
    /// each tick it writes the pose at the end of the next step, as an end pose, or with <paramref name="sample"/> as a full
    /// sample.
    /// </summary>
    private sealed class Line(double t0, bool sample) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            var end = new DPose(Place(self.TimeAt(self.Tick + 2), t0), DQuaternion.Identity);
            if (sample)
                self.MoveOnRails(
                    new(
                        Pose: end,
                        ChordVelocity: Velocity,
                        Velocity: Velocity,
                        Acceleration: Double3.Zero,
                        AngularVelocity: Double3.Zero,
                        AngularAcceleration: Double3.Zero
                    )
                );
            else
                self.MoveKinematic(end);
        }
    }

    private static Double3 Place(double time, double t0) => Start + Velocity * (time - t0);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EveryKinematicBodyEndsItsStepExactlyOnItsEndPose(bool hull, bool sample)
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        double t0 = world.Time;
        Entity mover = world.Spawn(
            new()
            {
                Frame = space,
                Pose = new(Start, DQuaternion.Identity),
                Body = new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Kinematic),
                AsFrame = hull ? new FrameSpec(FrameBounds.Sphere(5)) : null,
                Components = [new Line(t0, sample)],
            }
        );
        Tick(world, 2);

        for (int i = 0; i < 60; i++)
        {
            world.Tick();
            Double3 end = Place(world.Time, t0);
            BodyState state = faults.Outputs[^1].Bodies.Single(s => s.Entity == mover);
            if (state.Pose.Position != end)
                state
                    .Pose.Position.Should()
                    .Be(end, "after step {0} the body sits exactly on its end pose", world.TickNumber - 1);
        }

        world.Errors.Should().BeEmpty();
        world
            .PhysicsHomeOf(mover)
            .Should()
            .Be(space, "the body stays in the system of space, where its pose needs no conversion");
    }
}
