using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Motion samples and rails components on the owner of a frame that has a body: who may write a sample, what the rails
/// components check, and the teleport with which a rails component puts a hull on its rails. These tests read the motion intent of the next step and what each
/// step received; RailsFollowTests checks how frames follow the samples. The world runs on the real backend.
/// </summary>
public class RailsHullTests
{
    /// <summary>A sample at (1, 2, 3) with no motion.</summary>
    private static readonly MotionSample Parked =
        new(new(new(1, 2, 3), DQuaternion.Identity), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero,
            Double3.Zero);

    private static readonly KeplerOrbit Orbit = KeplerOrbit.Circular(5_000, phase: 0.3, spinRate: 0.2);
    private static readonly FrameSpin Spin = new(DQuaternion.FromAxisAngle(Double3.UnitZ, 0.4), 0.5, PhaseAtEpoch: 0.1);
    private static readonly Double3 SpinPlace = new(4_000, 300, -200);

    /// <summary>A far orbit of 9,000 km at 47 m/s round the point gravity of an <see cref="Scenes.AsteroidSpace"/>, past 2^23 m.</summary>
    private static readonly KeplerOrbit FarOrbit = KeplerOrbit.Circular(9e6, phase: 0.3);

    /// <summary>Writes one sample in every tick.</summary>
    private sealed class WritesSample(MotionSample sample) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.MoveOnRails(sample);
    }

    // ---- Who can write a sample ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Static)]
    public void MoveOnRailsThrowsForTheOwnerOfAFrameWhoseHullIsNotKinematic(BodyMotion motion)
    {
        using World world = NewWorld(out _);
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), motion, new WritesSample(Parked));

        Tick(world, 1);

        world.Errors.Should().ContainSingle().Which.Should().Contain(owner.ToString()).And
            .Contain(nameof(InvalidOperationException));
        NextSample(world, owner, out _).Should().BeFalse();
    }

    [Fact]
    public void MoveOnRailsTakesASampleFromTheOwnerOfAKinematicHull()
    {
        using World world = NewWorld(out _);
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), BodyMotion.Kinematic, new WritesSample(Parked));

        Tick(world, 1);

        world.Errors.Should().BeEmpty();
        NextSample(world, owner, out MotionSample sample).Should().BeTrue();
        sample.Should().Be(Parked);
    }

    [Fact]
    public void TheOwnerOfARootFrameWithABodyCanWriteASample()
    {
        using World world = NewWorld(out _);
        // A root frame has no parent, so its owner's body is the frame's static interior, not a hull.
        Entity owner = world.Spawn(new()
        {
            Body = new(new BoxShapeDesc(new(5)), BodyMotion.Static),
            AsFrame = new(FrameBounds.Sphere(1e6f)),
            Components = [new WritesSample(Parked)]
        });

        Tick(world, 1);

        world.Errors.Should().BeEmpty();
        NextSample(world, owner, out _).Should().BeTrue();
    }

    /// <summary>
    /// The owner writes a sample in tick 2, and then makes its hull dynamic in the same tick. Step 2 still moves the
    /// kinematic hull by the sample of tick 1. The change acts at the sync point of tick 2, so the sample of tick 2 goes, and
    /// step 3, which moves a dynamic hull, gets no sample.
    /// </summary>
    [Fact]
    public void AHullThatStopsBeingKinematicSendsNoSampleToTheStepAfterTheChange()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var rails = new WritesSample(Parked);
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), BodyMotion.Kinematic, rails);
        FrameHandle frame = world.GetOwnedFrame(owner);
        Tick(world, 2);
        NextSample(world, owner, out _).Should().BeTrue("tick 1 wrote a sample for step 2");

        world.SetMotionNextTick(owner, BodyMotion.Dynamic, remove: rails);
        Tick(world, 2);

        faults.Moves.Should().Contain(record => record.Entity == frame.Entity && record.Tick == 2,
            "step 2 moves the kinematic hull by the sample of tick 1");
        faults.Moves.Should().NotContain(record => record.Entity == frame.Entity && record.Tick == 3,
            "step 3 moves a dynamic hull");
        world.Errors.Should().BeEmpty();
    }

    // ---- A teleport and a sample in one tick ---------------------------------------------------------------------

    /// <summary>
    /// In one tick, the owner of a kinematic hull teleports it 10 m above (1, 2, 3) and writes a sample at (1, 2, 3). The
    /// teleport applies first, and the sample's target moves the hull on from there: Jolt sweeps the hull down 10 m over the
    /// step, at 600 m/s, so it meets what lies between. After the step the hull sits on the sample's pose, and the frame
    /// stands on it at the sync point.
    /// </summary>
    [Fact]
    public void InATickWithATeleportAndASampleOfAKinematicHullTheTeleportAppliesFirst()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var pilot = new Pilot();
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), BodyMotion.Kinematic, pilot, new WritesSample(Parked));
        FrameHandle frame = world.GetOwnedFrame(owner);
        Tick(world, 3);

        pilot.Once = body => body.Teleport(At(1, 12, 3));
        Tick(world, 2);

        BodyState hull = faults.Outputs[^1].Bodies.Single(state => state.Entity == owner);
        hull.Pose.Position.Should().Be(Parked.Pose.Position);
        (hull.LinearVelocity - new Vector3(0, -600, 0)).Length().Should().BeLessThan(0.01f,
            "the target moved the hull from the teleport's pose over the step");
        world.Frames.LocalPose(frame).Should().Be(Parked.Pose);
        world.Errors.Should().BeEmpty();
    }

    // ---- A teleport without a sample ----------------------------------------------------------------------------

    /// <summary>
    /// A kinematic hull without a sample holds still in its tree parent, and its owner can still teleport it. Physics moves
    /// the hull at the start of the next step and releases the teleport's pose, with a unit rotation and at rest, as the
    /// frame's step record, as for a static hull. So the frame stands where its hull is at the sync point after the step.
    /// </summary>
    [Fact]
    public void TheFrameOfAKinematicHullThatTeleportsInAStepWithoutASampleTakesTheTeleportsPose()
    {
        using World world = NewWorld(out _);
        var pilot = new Pilot();
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), BodyMotion.Kinematic, pilot);
        FrameHandle frame = world.GetOwnedFrame(owner);
        Tick(world, 3);

        var there = new DPose(new(1, 12, 3), DQuaternion.FromAxisAngle(new(0.6, 0, 0.8), 0.5));
        pilot.Once = body => body.Teleport(there);
        Tick(world, 2);

        world.Frames.LocalPose(frame).Should().Be(new DPose(there.Position, there.Rotation.Normalized()));
        world.Frames.LinearVelocity(frame).Should().Be(Double3.Zero);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A fast hull lives at the origin of its following frame. When the hull has no sample, the frame's plan holds the hull
    /// still, so the frame holds still in space too (ticket ter-zsku), and the hull, now slow and near the origin, leaves it
    /// for the system of space. A teleport without a sample then moves the hull 100 m, and it holds still there. Jolt's hull
    /// stands where its frame is at every sync point after the teleport: it keeps no velocity of the following frame. The step
    /// before the teleport has no sample either, so no rule that stops a hull after its last sample applies to the step of
    /// the teleport. Before ter-zsku the following frame fell on without a sample, and the hull teleported in it and jumped
    /// with it.
    /// </summary>
    [Fact]
    public void AKinematicHullThatLeavesItsStillFollowingFrameAndTeleportsWithoutASampleStaysWithItsFrame()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = Scenes.AsteroidSpace(world);
        var pilot = new Pilot();
        var rails = new KeplerMotion { Orbit = Orbit };
        Entity owner = Hull(world, space, BodyMotion.Kinematic, rails, pilot);
        FrameHandle frame = world.GetOwnedFrame(owner);
        Tick(world, 4);
        world.Frames.Numeric(world.Frames.TreeParent(frame)).Should()
            .Be(NumericMode.Following, "the hull moves at 2 km/s");

        // Tick 3 wrote the last sample, for step 4. Steps 5 and 6 have none, and the teleport of tick 6 acts in step 7.
        world.RemoveComponent(owner, rails);
        Tick(world, 2);
        DPose there = world.PoseIn(owner, space);
        world.VelocityIn(owner, space).Should()
            .Be(Double3.Zero, "the following frame holds the hull still without a sample");
        world.Frames.TreeParent(frame).Should().Be(space,
            "the still hull is slow and near the origin, so it leaves its following frame");
        pilot.Once = body => body.Teleport(new(there.Position + new Double3(0, 100, 0), DQuaternion.Identity));
        Tick(world, 2);

        for (int i = 0; i < 10; i++)
        {
            (world.PoseIn(owner, space).Position - (there.Position + new Double3(0, 100, 0))).Length
                .Should().BeLessThan(1e-6, "the hull holds still where the teleport put it at sync point {0}",
                    world.TickNumber);
            Double3 apart = HullPosition(backend, world.Frames.TreeParent(frame), owner) -
                            world.Frames.TreeLocalPose(frame).Position;
            apart.Length.Should().BeLessThan(1e-6, "the hull stands where its frame is at sync point {0}",
                world.TickNumber);
            Tick(world, 1);
        }

        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A hull on a far orbit lives at the origin of a following frame of its own by its distance from the origin of space,
    /// and keeps it when its samples stop, because it stays far. Without a sample the frame's plan holds the hull still, so
    /// physics aims the hull at the frame's origin, puts it there after the update, and releases the frame's record there, at
    /// rest, in every step (ticket ter-zsku). A teleport without a sample then moves the hull 100 m, and it holds still
    /// there: the frame jumps with it, and Jolt's hull stands at the frame's origin at every sync point after the teleport.
    /// In the step of the teleport the released record stands at the teleport's pose in the frame's system as the step
    /// found it, and the sync point lands the frame on its plan there.
    /// </summary>
    [Fact]
    public void AKinematicHullWithoutASampleThatTeleportsInItsFarFollowingFrameStandsAtTheFramesOriginInJolt()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        var pilot = new Pilot();
        var rails = new KeplerMotion { Orbit = FarOrbit };
        Entity owner = Hull(world, space, BodyMotion.Kinematic, rails, pilot);
        FrameHandle frame = world.GetOwnedFrame(owner);
        Tick(world, 4);
        FrameHandle following = world.Frames.TreeParent(frame);
        world.Frames.Numeric(following).Should()
            .Be(NumericMode.Following, "the hull is 9,000 km from the origin of space");

        world.RemoveComponent(owner, rails);
        Tick(world, 2);
        DPose there = world.PoseIn(owner, space);
        world.VelocityIn(owner, space).Should()
            .Be(Double3.Zero, "the following frame holds the hull still without a sample");
        pilot.Once = body => body.Teleport(new(there.Position + new Double3(0, 100, 0), DQuaternion.Identity));
        for (int i = 0; i < 12; i++)
        {
            world.Tick();
            world.Frames.TreeParent(frame).Should().Be(following, "the far hull keeps its following frame");
            MotionSample released = faults.Outputs[^1].Frames.Single(record => record.Frame == frame).Motion;
            released.Velocity.Should().Be(Double3.Zero, "the still hull's record is at rest at sync point {0}",
                world.TickNumber);
            if (i == 1)
            {
                (released.Pose.Position - new Double3(0, 100, 0)).Length
                    .Should().BeLessThan(1e-6,
                        "the record of the teleport's step stands at the teleport's pose in the frame's system");
                continue;
            }

            released.Pose.Position.Should().Be(Double3.Zero,
                "physics releases the still hull's record at the frame's origin at sync point {0}", world.TickNumber);
            if (i < 2)
                continue;
            (world.PoseIn(owner, space).Position - (there.Position + new Double3(0, 100, 0))).Length
                .Should().BeLessThan(1e-6, "the hull holds still where the teleport put it at sync point {0}",
                    world.TickNumber);
            HullPosition(faults.Jolt, following, owner).Should().Be(Double3.Zero,
                "Jolt's hull stands at the frame's origin at sync point {0}", world.TickNumber);
        }

        world.Errors.Should().BeEmpty();
    }

    // ---- The checks of a rails component ------------------------------------------------------------------------

    /// <summary>
    /// A rails component reads the guard's reason before it writes (see <see cref="Self.MotionIntentProblem"/>). An entity
    /// that refuses a full sample gets nothing from the component, neither a sample nor the teleport of a first run, and
    /// the component logs the guard's reason once.
    /// </summary>
    [Theory]
    [InlineData("KeplerMotion", "dynamic hull")]
    [InlineData("SpinMotion", "dynamic hull")]
    [InlineData("KeplerMotion", "dynamic plain body")]
    [InlineData("SpinMotion", "dynamic plain body")]
    [InlineData("KeplerMotion", "static plain body")]
    [InlineData("SpinMotion", "static plain body")]
    public void ARailsComponentWritesNothingAndLogsTheGuardsReasonOnce(string component, string kind)
    {
        using World world = NewWorld(out _);
        var log = new List<string>();
        world.Log += log.Add;
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity owner = kind == "dynamic hull"
            ? Hull(world, space, BodyMotion.Dynamic, Rails(component))
            : world.Spawn(new()
            {
                Frame = space,
                Pose = new(SpinPlace, DQuaternion.Identity),
                Body = Ball() with { Motion = kind == "static plain body" ? BodyMotion.Static : BodyMotion.Dynamic },
                Components = [Rails(component)]
            });

        Tick(world, 3);

        string? reason = world.MotionIntentProblem(owner, new(Parked));
        reason.Should().NotBeNull();
        // The next step's intent holds what the tick wrote, which is nothing of the component.
        PhysicsIntent next = world.Entities.NextStepIntent(owner);
        next.Move.HasValue.Should().BeFalse();
        (next.Flags & IntentFlags.Teleport).Should().Be(IntentFlags.None);
        log.Where(line => line.Contains(owner.ToString())).Should().ContainSingle().Which.Should().Contain(reason!);
        world.Errors.Should().BeEmpty();
    }

    // ---- The first run of a rails component ---------------------------------------------------------------------

    [Theory]
    [InlineData("KeplerMotion")]
    [InlineData("SpinMotion")]
    public void TheFirstSampleOfARailsComponentComesWithATeleportOfItsHullToTheRailsPoseAtTheStartOfTheStep(
        string component)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), BodyMotion.Kinematic, Rails(component));

        Tick(world, 4);

        // Tick 0 wrote the first sample and the teleport, which step 1 applies first: at the start of step 1.
        IntentRecord[] teleports =
            [.. faults.Intents.Where(r => r.Entity == owner && (r.Intent.Flags & IntentFlags.Teleport) != 0)];
        teleports.Should().ContainSingle().Which.Tick.Should().Be(1);
        teleports[0].Intent.TeleportPose.Should().Be(RailsPose(component, world.TimeAt(1)));
        NextSample(world, owner, out _).Should().BeTrue();
    }

    [Fact]
    public void ANewRailsComponentTeleportsItsHullAgain()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var first = new KeplerMotion { Orbit = Orbit };
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), BodyMotion.Kinematic, first);
        Tick(world, 3);

        world.RemoveComponent(owner, first);
        world.AddComponent(owner, new KeplerMotion { Orbit = Orbit });
        Tick(world, 2);

        // The new component first ran in tick 3, so step 4 teleports the hull.
        faults.Intents.Where(r => r.Entity == owner && (r.Intent.Flags & IntentFlags.Teleport) != 0).Select(r => r.Tick)
            .Should().Equal(1, 4);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The owner of a frame at <see cref="SpinPlace"/> in <paramref name="space"/> whose hull is a ball with
    /// <paramref name="motion"/>. A rails component among <paramref name="components"/> puts the hull on its rails.
    /// </summary>
    private static Entity Hull(World world, FrameHandle space, BodyMotion motion, params IComponent[] components) =>
        world.Spawn(new()
        {
            Frame = space,
            Pose = new(SpinPlace, DQuaternion.FromAxisAngle(Double3.UnitX, 1)),
            Body = Ball() with { Motion = motion },
            AsFrame = new(FrameBounds.Sphere(0.01f)),
            Components = components
        });

    /// <summary>Where Jolt holds the hull of <paramref name="owner"/> in the system of <paramref name="frame"/>.</summary>
    private static Double3 HullPosition(JoltPhysicsBackend backend, FrameHandle frame, Entity owner) =>
        backend.BodiesIn(frame).Single(facts =>
                BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner))
            .Position;

    private static IComponent Rails(string component) =>
        component == "KeplerMotion" ? new KeplerMotion { Orbit = Orbit } : new SpinMotion { Spin = Spin };

    /// <summary>The rails pose of a hull of <see cref="Hull"/> with the rails component <paramref name="component"/> at
    /// <paramref name="time"/>.</summary>
    private static DPose RailsPose(string component, double time) =>
        component == "KeplerMotion"
            ? new(Orbit.StateAt(time, Scenes.AsteroidMu).Position, Orbit.RotationAt(time, Scenes.AsteroidMu))
            : new DPose(SpinPlace, Spin.RotationAt(time));

    /// <summary>The full sample that the next step applies to <paramref name="owner"/>, between two ticks, or false.</summary>
    private static bool NextSample(World world, Entity owner, out MotionSample sample) =>
        world.Entities.NextStepIntent(owner).Move.TryGetValue(out sample);
}
