using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Motion samples and rails components on the owner of a frame that has a body: who may write a sample, what the rails
/// components check, and the teleport with which a rails component puts a hull on its rails. In this ticket a hull is
/// kinematic when its frame has the Kepler or the Spin driver. Nothing follows the samples yet, so these tests read the
/// sample table and what each step received. The world runs on the real backend.
/// </summary>
public class RailsHullTests
{
    /// <summary>A sample at (1, 2, 3) with no motion.</summary>
    private static readonly MotionSample Parked =
        new(new DPose(new Double3(1, 2, 3), DQuaternion.Identity), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Vector3.Zero);

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
    [InlineData(MotionDriver.Physics)]
    [InlineData(MotionDriver.Static)]
    public void MoveOnRailsThrowsForTheOwnerOfAFrameWhoseHullIsNotKinematic(MotionDriver driver)
    {
        using World world = NewWorld(out _);
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), driver, new WritesSample(Parked));

        Tick(world, 1);

        world.Errors.Should().ContainSingle().Which.Should().Contain(owner.ToString()).And.Contain(nameof(InvalidOperationException));
        world.Frames.Samples.Applied.TryGet(world.GetOwnedFrame(owner), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(MotionDriver.Kepler)]
    [InlineData(MotionDriver.Spin)]
    public void MoveOnRailsTakesASampleFromTheOwnerOfAKinematicHull(MotionDriver driver)
    {
        using World world = NewWorld(out _);
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), driver, new WritesSample(Parked));

        Tick(world, 1);

        world.Errors.Should().BeEmpty();
        world.Frames.Samples.Applied.TryGet(world.GetOwnedFrame(owner), out MotionSample sample).Should().BeTrue();
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
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static),
            Components = [new WritesSample(Parked)],
        });

        Tick(world, 1);

        world.Errors.Should().BeEmpty();
        world.Frames.Samples.Applied.TryGet(world.GetOwnedFrame(owner), out _).Should().BeTrue();
    }

    [Fact]
    public void AHullThatStopsBeingKinematicDropsItsAppliedSample()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), MotionDriver.Kepler, new WritesSample(Parked));
        FrameHandle frame = world.GetOwnedFrame(owner);
        Tick(world, 2);
        world.Frames.Samples.Applied.TryGet(frame, out _).Should().BeTrue("tick 1 wrote a sample for step 2");

        world.SetFrameDriver(frame, MotionDriver.Physics);

        world.Frames.Samples.Applied.TryGet(frame, out _).Should().BeFalse();
        Tick(world, 1);
        faults.Samples.Should().NotContain(record => record.Frame == frame && record.Tick == 2, "step 2 moves a dynamic hull");
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
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), MotionDriver.Physics, Rails(component));

        Tick(world, 3);

        // The applied buffer holds what the tick wrote. The followed buffer holds the step record that physics measured for
        // the dynamic hull, which is no sample of the component.
        world.Frames.Samples.Applied.TryGet(world.GetOwnedFrame(owner), out _).Should().BeFalse();
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
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), component == "KeplerMotion" ? MotionDriver.Kepler : MotionDriver.Spin, Rails(component));

        Tick(world, 4);

        // Tick 0 wrote the first sample and the teleport, which step 1 applies first: at the start of step 1.
        IntentRecord[] teleports = faults.Intents.Where(r => r.Entity == owner && (r.Intent.Flags & IntentFlags.Teleport) != 0).ToArray();
        teleports.Should().ContainSingle().Which.Tick.Should().Be(1);
        teleports[0].Intent.TeleportPose.Should().Be(RailsPose(component, world.TimeAt(1)));
        world.Frames.Samples.Applied.TryGet(world.GetOwnedFrame(owner), out _).Should().BeTrue();
    }

    [Fact]
    public void ANewRailsComponentTeleportsItsHullAgain()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var first = new KeplerMotion { Orbit = Orbit };
        Entity owner = Hull(world, Scenes.AsteroidSpace(world), MotionDriver.Kepler, first);
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
    /// The owner of a frame in <paramref name="space"/> whose hull is a ball, with <paramref name="driver"/>: on
    /// <see cref="Orbit"/> for Kepler, at <see cref="SpinPlace"/> with <see cref="Spin"/> for Spin, and at
    /// <see cref="SpinPlace"/> for the others.
    /// </summary>
    private static Entity Hull(World world, FrameHandle space, MotionDriver driver, params IComponent[] components) => world.Spawn(new SpawnDesc
    {
        Frame = space,
        Pose = new DPose(SpinPlace, DQuaternion.FromAxisAngle(Double3.UnitX, 1)),
        Body = Ball(),
        AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f), driver, Orbit: driver == MotionDriver.Kepler ? Orbit : default,
            Spin: driver == MotionDriver.Spin ? Spin : default),
        Components = components,
    });

    private static IComponent Rails(string component) =>
        component == "KeplerMotion" ? new KeplerMotion { Orbit = Orbit } : new SpinMotion { Spin = Spin };

    /// <summary>The rails pose of a hull of <see cref="Hull"/> with the rails component <paramref name="component"/> at <paramref name="time"/>.</summary>
    private static DPose RailsPose(string component, double time) =>
        component == "KeplerMotion"
            ? new DPose(Orbit.StateAt(time, Scenes.AsteroidMu).Position, Orbit.RotationAt(time, Scenes.AsteroidMu))
            : new DPose(SpinPlace, Spin.RotationAt(time));
}
