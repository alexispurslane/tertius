using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The loose-body step of a frame on the World and the real backend (docs/plans/particles.md, "The rule of motion"). After
/// step 2 of a sync point, <see cref="FrameTree.LastLooseBodyStep"/> gives the linear gravity that the step that ended
/// applied: physics reads the frame's field less its stored proper acceleration during the step, and step 2 then writes a
/// new proper acceleration. The loose-body step keeps the value that the step found, so a puff that steps after step 2
/// feels what a free body felt in the same step. The test records what physics reads, during the step, through the fault
/// wrapper's edit of the output, which runs on the physics thread after Jolt's step, before the sync point.
/// </summary>
public class LooseBodyStepPhysicsTests
{
    private const float G = 9.81f;
    private static readonly Double3[] Places = [new(0, 0, 0), new(1, 2, 3), new(-4, 0.5, 2)];

    /// <summary>
    /// Ticks <paramref name="world"/> <paramref name="ticks"/> times and checks after each tick that the loose-body step of
    /// <paramref name="frame"/> gives, at each place, the linear gravity that physics read in that tick's step. Returns how
    /// many ticks ended with a proper acceleration that differs from the one that the step found.
    /// </summary>
    private static int CheckTicks(World world, FaultInjectingBackend faults, FrameHandle frame, int ticks,
        Action<int>? beforeTick = null)
    {
        Double3[] read = [];
        faults.EditOutput = _ => read = [.. Places.Select(place => world.Frames.LinearGravity(frame, place))];
        int changes = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            beforeTick?.Invoke(tick);
            world.Tick();

            LooseBodyStep step = world.Frames.LastLooseBodyStep(frame);
            for (int i = 0; i < Places.Length; i++)
                step.LinearGravity(Places[i]).Should().Be(read[i], "tick {0}, place {1}", tick, Places[i]);
            if (world.Frames.ProperAcceleration(frame) != step.ProperAcceleration)
                changes++;
        }

        faults.EditOutput = null;
        world.Errors.Should().BeEmpty();
        return changes;
    }

    [Fact]
    public void TheLinearGravityOfTheLastStepIsTheGravityThatPhysicsReadInThatStepInAFrameThatHoldsStillInAField()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, G);
        // The cabin's own field holds only inside its box, so the field's part shows at some places and not at others.
        GravityField cabinField = GravityField.Uniform(new(0.5f, 0, -0.25f), new BoundingBox(new(-2), new(2)));
        FrameHandle cabin = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = ground,
            Pose = At(0, 100),
            Body = new(new BoxShapeDesc(new(2, 0.5f, 2)), BodyMotion.Static),
            AsFrame = new(FrameBounds.Sphere(10), cabinField)
        }));

        int changes = CheckTicks(world, faults, cabin, ticks: 5);

        changes.Should().Be(1,
            "the first step 2 gives the new cabin the ground's gravity reversed, which its first step did not find");
    }

    [Fact]
    public void TheLinearGravityOfTheLastStepIsTheGravityThatPhysicsReadInThatStepInAHullsFrameUnderThrust()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        const float mass = 500;
        var pilot = new Pilot();
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = space,
            Body = new(new BoxShapeDesc(new(2, 0.5f, 2)), BodyMotion.Dynamic, Mass: mass, LinearDamping: 0,
                AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(10), GravityField.Uniform(new(0, -1, 0))),
            Components = [pilot]
        }));
        Tick(world, 3);

        // The engine burns from tick 2 of the check to tick 5, and its force acts in the steps of ticks 3 to 6.
        int changes = CheckTicks(world, faults, ship, ticks: 9, beforeTick: tick =>
            pilot.EveryTick = tick is >= 2 and <= 5 ? hull => hull.AddForce(new(0, mass * 1.5f, 0)) : null);

        changes.Should().BeGreaterThanOrEqualTo(2,
            "step 2 changes the ship's proper acceleration when the burn starts and when it ends");
    }
}
