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
        new(new DPose(new Double3(1, 2, 3), DQuaternion.Identity), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero);

    private static readonly KeplerOrbit Orbit = KeplerOrbit.Circular(5_000, phase: 0.3, spinRate: 0.2);
    private static readonly FrameSpin Spin = new(DQuaternion.FromAxisAngle(Double3.UnitZ, 0.4), 0.5, PhaseAtEpoch: 0.1);
    private static readonly Double3 SpinPlace = new(4_000, 300, -200);

    /// <summary>Writes one sample in every tick.</summary>
    private sealed class WritesSample(MotionSample sample) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => self.MoveOnRails(sample);
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

        world.Errors.Should().ContainSingle().Which.Should().Contain(owner.ToString()).And.Contain(nameof(InvalidOperationException));
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
        Entity owner = world.Spawn(new SpawnDesc
        {
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f)),
            Components = [new WritesSample(Parked)],
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

        faults.Moves.Should().Contain(record => record.Entity == frame.Entity && record.Tick == 2, "step 2 moves the kinematic hull by the sample of tick 1");
        faults.Moves.Should().NotContain(record => record.Entity == frame.Entity && record.Tick == 3, "step 3 moves a dynamic hull");
        world.Errors.Should().BeEmpty();
    }

    // ---- A kinematic hull takes only MoveOnRails and Teleport -----------------------------------------------------

    [Fact]
    public void MoveKinematicOnAKinematicHullThrows() =>
        AssertTheKinematicHullRefuses(body => body.MoveKinematic(At(1, 2, 3)));

    [Fact]
    public void SetVelocityOnAKinematicHullThrows() =>
        AssertTheKinematicHullRefuses(body => body.SetVelocity(new Double3(1, 0, 0)));

    [Fact]
    public void SetAngularVelocityOnAKinematicHullThrows() =>
        AssertTheKinematicHullRefuses(body => body.SetAngularVelocity(new Vector3(0, 1, 0)));

    /// <summary>
    /// The owner of a kinematic hull calls <paramref name="call"/> in a tick. Only a sample tells the frame of a kinematic
    /// hull how to follow, so the call throws an <see cref="InvalidOperationException"/>, which the World records, and the
    /// step gets no such intent.
    /// </summary>
    private static void AssertTheKinematicHullRefuses(BodyCommand call)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var pilot = new Pilot { Once = call };
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), BodyMotion.Kinematic, pilot);

        Tick(world, 2);

        world.Errors.Should().ContainSingle().Which.Should().Contain(owner.ToString()).And.Contain(nameof(InvalidOperationException)).And.Contain("kinematic hull");
        faults.Intents.Should().NotContain(record => record.Entity == owner &&
            (record.Intent.Move.HasValue || (record.Intent.Flags & (IntentFlags.SetVelocity | IntentFlags.SetAngularVelocity)) != 0));
    }

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
        (hull.LinearVelocity - new Vector3(0, -600, 0)).Length().Should().BeLessThan(0.01f, "the target moved the hull from the teleport's pose over the step");
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

        var there = new DPose(new Double3(1, 12, 3), DQuaternion.FromAxisAngle(new Double3(0.6, 0, 0.8), 0.5));
        pilot.Once = body => body.Teleport(there);
        Tick(world, 2);

        world.Frames.LocalPose(frame).Should().Be(new DPose(there.Position, there.Rotation.Normalized()));
        world.Frames.LinearVelocity(frame).Should().Be(Double3.Zero);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A fast hull lives at the origin of its orbit frame, which goes on falling when the hull has no sample. The hull holds
    /// still in the orbit frame then, also after a teleport: it does not keep the velocity of a body at rest in the semantic
    /// frame, which in the orbit frame is about minus the orbit frame's 2 km/s. The orbit frame jumps with the hull, as for a
    /// teleport with a sample, so the hull and its frame stand at the orbit frame's origin at every sync point after the
    /// teleport. The step before the teleport has no sample either, so no rule that stops a hull after its last sample
    /// applies to the step of the teleport.
    /// </summary>
    [Fact]
    public void AKinematicHullThatTeleportsWithoutASampleInItsOrbitFrameStaysWithItsFrame()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = Scenes.AsteroidSpace(world);
        var pilot = new Pilot();
        var rails = new KeplerMotion { Orbit = Orbit };
        Entity owner = Hull(world, space, BodyMotion.Kinematic, rails, pilot);
        FrameHandle frame = world.GetOwnedFrame(owner);
        Tick(world, 4);
        world.Frames.Numeric(world.Frames.TreeParent(frame)).Should().Be(NumericMode.Orbiting, "the hull moves at 2 km/s");

        // Tick 3 wrote the last sample, for step 4. Step 5 has none, and the teleport of tick 5 acts in step 6.
        world.RemoveComponent(owner, rails);
        Tick(world, 1);
        FrameHandle orbitFrame = world.Frames.TreeParent(frame);
        world.Frames.Numeric(orbitFrame).Should().Be(NumericMode.Orbiting, "the orbit frame falls on without a sample");
        DPose there = world.PoseIn(owner, space);
        pilot.Once = body => body.Teleport(new DPose(there.Position + new Double3(0, 100, 0), DQuaternion.Identity));
        Tick(world, 2);

        world.Frames.TreeParent(frame).Should().Be(orbitFrame, "the orbit frame jumped with its anchor");
        for (int i = 0; i < 10; i++)
        {
            Double3 apart = HullPosition(backend, orbitFrame, owner) - world.Frames.TreeLocalPose(frame).Position;
            apart.Length.Should().BeLessThan(1e-6, "the hull stands where its frame is at sync point {0}", world.TickNumber);
            Tick(world, 1);
        }
        world.Errors.Should().BeEmpty();
    }

    // ---- The checks of a rails component ------------------------------------------------------------------------

    [Theory]
    [InlineData("KeplerMotion")]
    [InlineData("SpinMotion")]
    public void ARailsComponentOnAHullThatIsNotKinematicWritesNoSampleAndLogsOnce(string component)
    {
        using World world = NewWorld(out _);
        var log = new List<string>();
        world.Log += log.Add;
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), BodyMotion.Dynamic, Rails(component));

        Tick(world, 3);

        // The next step's intent holds what the tick wrote, which is no sample of the component.
        NextSample(world, owner, out _).Should().BeFalse();
        log.Where(line => line.Contains(owner.ToString())).Should().ContainSingle();
        world.Errors.Should().BeEmpty();
    }

    // ---- The first run of a rails component ---------------------------------------------------------------------

    [Theory]
    [InlineData("KeplerMotion")]
    [InlineData("SpinMotion")]
    public void TheFirstSampleOfARailsComponentComesWithATeleportOfItsHullToTheRailsPoseAtTheStartOfTheStep(string component)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), BodyMotion.Kinematic, Rails(component));

        Tick(world, 4);

        // Tick 0 wrote the first sample and the teleport, which step 1 applies first: at the start of step 1.
        IntentRecord[] teleports = faults.Intents.Where(r => r.Entity == owner && (r.Intent.Flags & IntentFlags.Teleport) != 0).ToArray();
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
    private static Entity Hull(World world, FrameHandle space, BodyMotion motion, params IComponent[] components) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Pose = new DPose(SpinPlace, DQuaternion.FromAxisAngle(Double3.UnitX, 1)),
        Body = Ball() with { Motion = motion },
        AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f)),
        Components = components,
    });

    /// <summary>Where Jolt holds the hull of <paramref name="owner"/> in the system of <paramref name="frame"/>.</summary>
    private static Double3 HullPosition(JoltPhysicsBackend backend, FrameHandle frame, Entity owner) =>
        backend.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner)).Position;

    private static IComponent Rails(string component) =>
        component == "KeplerMotion" ? new KeplerMotion { Orbit = Orbit } : new SpinMotion { Spin = Spin };

    /// <summary>The rails pose of a hull of <see cref="Hull"/> with the rails component <paramref name="component"/> at <paramref name="time"/>.</summary>
    private static DPose RailsPose(string component, double time) =>
        component == "KeplerMotion"
            ? new DPose(Orbit.StateAt(time, Scenes.AsteroidMu).Position, Orbit.RotationAt(time, Scenes.AsteroidMu))
            : new DPose(SpinPlace, Spin.RotationAt(time));

    /// <summary>The full sample that the next step applies to <paramref name="owner"/>, between two ticks, or false.</summary>
    private static bool NextSample(World world, Entity owner, out MotionSample sample) =>
        world.Entities.NextStepIntent(owner).Move.TryGetValue(out sample);
}
