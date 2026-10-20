using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A free body inside a frame whose dynamic hull physics spins up, on the World and the real Jolt backend
/// (docs/plans/spin-gravity.md, "V1, the turn of the step in flight", fork F3, and "Acceptance tests"). Physics turns the
/// hull in the step in flight, so the frame tree does not know that turn when the step starts: it predicts it from the
/// hull's last record, the rate of that record plus its angular acceleration times the step. So the contents of a hull that
/// spins up feel the Euler force one step late, the same lag as the hull's proper acceleration.
/// </summary>
public class SpinningShipBodyTests
{
    /// <summary>Pushes its entity's body with a steady torque on every tick from <paramref name="fromTick"/> on.</summary>
    private sealed class SteadyTorque(Vector3 torque, long fromTick) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick >= fromTick)
                self.Body.AddTorque(torque);
        }
    }

    [Fact]
    public void TheContentsOfAShipThatPhysicsSpinsUpByASteadyTorqueFeelTheEulerForceOneStepLate()
    {
        // The ship's hull is a ball of 5 m and 1,000 kg, whose moment of inertia is 2/5 m r² = 10,000 kg m² about every
        // axis, so a torque of 10,000 N m spins it up about its +Y at α = 1 rad/s² from rest. Nothing damps it. A crate
        // rests in inertial space 20 m from the ship's center, inside its bubble, and nothing damps it either.
        //
        // In the first step of the torque, the hull's last record turned at no rate and with no angular acceleration, so
        // the tree predicts no turn, and the crate keeps its coordinates in the ship's axes while the hull turns by
        // α dt². From the next step on, the record holds α, the prediction is the hull's real turn, and V2 is the turn
        // that the crate's chord came from (the prediction of the step before). So the crate's path in the ship's axes is
        // the path in which each step turns by the predicted rate, and in inertial space it is the crate's place turned
        // once by α dt² about the ship's axis, in the sense of the spin: 5.6 mm at 20 m. The crate's expected path, its
        // place at rest in inertial space, misses that by the Euler force of one step, α r dt².
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        const long torqueFrom = 10;
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(new SphereShapeDesc(5), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(2)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30)),
            Components = [new SteadyTorque(new Vector3(0, 10_000, 0), torqueFrom)],
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        var place = new Double3(20, 0, 0);
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = ship,
            Pose = new DPose(place, DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0),
        });

        double dt = world.DeltaTime;
        const double alpha = 1;
        Double3 predicted = DQuaternion.FromAxisAngle(Double3.UnitY, alpha * dt * dt).Rotate(place);
        int firstTurn = -1;
        double worstBefore = 0, worstPredicted = 0;
        var missed = new List<double>();
        for (int tick = 0; tick < 120; tick++)
        {
            world.Tick();
            Double3 inSpace = world.PoseIn(crate, space).Position;
            if (firstTurn < 0 && world.Frames.LocalPose(ship).Rotation != DQuaternion.Identity)
                firstTurn = tick;
            if (firstTurn < 0)
                worstBefore = Math.Max(worstBefore, Double3.Distance(inSpace, place));
            else
            {
                worstPredicted = Math.Max(worstPredicted, Double3.Distance(inSpace, predicted));
                missed.Add(Double3.Distance(inSpace, place));
            }
        }

        firstTurn.Should().BeGreaterThan(0, "the hull starts to turn when the torque starts");
        world.Frames.AngularVelocity(ship).Y.Should().BeApproximately((float)(alpha * (120 - firstTurn) * dt), 1e-4f, "the torque spun the hull up at α");
        worstBefore.Should().BeLessThan(1e-9, "the crate rests in inertial space while the hull rests");
        // Within 2e-5 m: the prediction holds Jolt's float rate, whose rounding grows with the rate, up to 1.8 rad/s here
        // (measured: 1e-8 m after 0.15 s, 7e-6 m after 1.5 s).
        worstPredicted.Should().BeLessThan(2e-5, "each step turns the crate's coordinates by the rate predicted from the hull's last record");
        missed.Should().AllSatisfy(m => m.Should().BeApproximately(alpha * place.Length * dt * dt, 2e-5,
            "the crate misses its place at rest in inertial space by the Euler force of one step"));
        world.GetFrame(crate).Should().Be(ship);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ship whose dynamic hull turns slower and slower, by its angular damping, falls asleep in empty space. When Jolt puts
    /// a body to sleep it sets its velocities to zero, which is no motion of the step: the hull's last angular velocity
    /// does not reverse. So the frame tree predicts no turn for the step after the sleep, and from the step after that the
    /// ship is no turning frame, so its contents keep the results of a hull that does not turn, to the bit
    /// (docs/plans/spin-gravity.md, "V1, the turn of the step in flight"). A record that read the reset as an angular
    /// acceleration of −ω/dt would turn the frame back by its last rate for one step.
    /// </summary>
    [Fact]
    public void AShipThatFallsAsleepStopsTurningWithoutTurningBack()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            AngularVelocity = new Vector3(0, 0.05f, 0),
            Body = new BodyDesc(new SphereShapeDesc(5), Mass: 1000, LinearDamping: 0, AngularDamping: 5, InteriorShape: new SphereShapeDesc(2)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30)),
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        Tick(world, 2);
        IsAwake(backend, space, owner).Should().BeTrue();

        int slept = TickUntil(world, () => !IsAwake(backend, space, owner), 1200);
        slept.Should().BeLessThan(1200, "the damping slows the hull until it falls asleep");
        world.Tick();
        FrameTurns afterSleep = world.Frames.Turns(ship);
        world.Tick();

        afterSleep.Step.AngularVelocity.Should().Be(Double3.Zero, "the hull asleep does not turn, and its last rate does not reverse");
        world.Frames.Turns(ship).Turns.Should().BeFalse("two steps after the sleep the ship turns in neither step");
        world.Errors.Should().BeEmpty();
    }
}
