using AwesomeAssertions;
using Tertius.Frames.Rails;
using static Tertius.Simulation.Tests.BlueprintSamples;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The rails components <see cref="KeplerMotion"/> and <see cref="SpinMotion"/> on frames without a body: what they check,
/// and the sample that each writes in each tick. The tests of rails components on hulls are in Physics.Tests
/// (RailsHullTests). These tests read the motion intent of the next step; KeplerFrameTests, SpinFrameTests and RailsFollowTests (Physics.Tests)
/// check how frames follow the samples.
/// </summary>
public sealed class RailsComponentTests
{
    private const double Mu = 4e14;

    /// <summary>An orbit that a <see cref="KeplerMotion"/> cannot follow. The default orbit would leave the frame at its parent's origin.</summary>
    public static TheoryData<string, KeplerOrbit> OrbitsThatAreNotValid =>
        new()
        {
            { "the default orbit", default },
            { "no semi-major axis", KeplerOrbit.Circular(0) },
            { "an open orbit", KeplerOrbit.Circular(1000) with { Eccentricity = 1 } },
            { "a negative eccentricity", KeplerOrbit.Circular(1000) with { Eccentricity = -0.1 } },
            { "a semi-major axis that is not a number", KeplerOrbit.Circular(double.NaN) },
            { "an infinite spin", KeplerOrbit.Circular(1000, spinRate: double.PositiveInfinity) },
        };

    // ---- The checks of the properties ---------------------------------------------------------------------------

    [Theory]
    [InlineData("the default orbit")]
    [InlineData("an open orbit")]
    [InlineData("a semi-major axis that is not finite")]
    public void TheKeplerMotionSetterRefusesAnOrbitThatIsNotValid(string which)
    {
        KeplerOrbit orbit = which switch
        {
            "the default orbit" => default,
            "an open orbit" => KeplerOrbit.Circular(1000) with { Eccentricity = 1 },
            _ => KeplerOrbit.Circular(double.PositiveInfinity),
        };

        FluentActions.Invoking(() => new KeplerMotion { Orbit = orbit }).Should().Throw<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(OrbitsThatAreNotValid))]
    public void AKeplerMotionRefusesAnOrbitThatItCannotFollow(string why, KeplerOrbit orbit)
    {
        // The component checks its orbit, not the World: the setter refuses it before any frame gets it.
        Action make = () => new KeplerMotion { Orbit = orbit };

        make.Should().Throw<ArgumentException>(why).WithMessage("*orbit*not valid*");
    }

    [Fact]
    public void TheSpinMotionSetterRefusesASpinThatIsNotValid()
    {
        var spin = new FrameSpin(Tilt: DQuaternion.Identity, Rate: double.NaN);

        FluentActions.Invoking(() => new SpinMotion { Spin = spin }).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ABlueprintMakesAKeplerMotionFromItsProperties()
    {
        var orbit = KeplerOrbit.Circular(7e6, phase: 0.4);
        var blueprints = new Blueprints();
        blueprints.Register(new(Name: "moon", Components: [new(typeof(KeplerMotion), Fields(("Orbit", orbit)))]));

        var motion = (KeplerMotion)blueprints.Instantiate("moon").Single();

        motion.Orbit.Should().Be(orbit);
    }

    [Fact]
    public void ABlueprintMakesASpinMotionFromItsProperties()
    {
        var spin = new FrameSpin(
            Tilt: DQuaternion.FromAxisAngle(Double3.UnitZ, -0.4),
            Rate: 0.7,
            PhaseAtEpoch: 0.3,
            Epoch: 2
        );
        var blueprints = new Blueprints();
        blueprints.Register(new(Name: "ground", Components: [new(typeof(SpinMotion), Fields(("Spin", spin)))]));

        var motion = (SpinMotion)blueprints.Instantiate("ground").Single();

        motion.Spin.Should().Be(spin);
    }

    // ---- The samples --------------------------------------------------------------------------------------------

    [Fact]
    public void KeplerMotionWritesTheLibrarysSampleForTheEndOfTheStepInWhichItsIntentActs()
    {
        using var test = new TestWorld();
        test.GiveRootPointGravity(Mu);
        var orbit = KeplerOrbit.Circular(7e6, phase: 0.4, spinRate: 0.01);
        FrameHandle moon = SpawnFrame(test, components: new KeplerMotion { Orbit = orbit });

        test.Run(3);

        // Tick 2 wrote the sample, for step 3, which ends at the time of tick 4.
        MotionSample expected = orbit.Sample(Mu, test.World.TimeAt(4), test.World.DeltaTime);
        Applied(test, moon).Should().Be(expected);
    }

    [Fact]
    public void SpinMotionWritesTheSpinsSampleAtItsFramesPlace()
    {
        using var test = new TestWorld();
        var spin = new FrameSpin(
            Tilt: DQuaternion.FromAxisAngle(Double3.UnitZ, -0.4),
            Rate: 0.7,
            PhaseAtEpoch: 0.3,
            Epoch: 2
        );
        var place = new Double3(5, -6, 7);
        FrameHandle ground = SpawnFrame(test, place, new SpinMotion { Spin = spin });

        test.Run(3);

        Applied(test, ground).Should().Be(spin.Sample(place, test.World.TimeAt(4)));
    }

    [Fact]
    public void ALaterMoveOnRailsFromAnotherComponentOfTheEntityWinsOverTheRailsComponentsSample()
    {
        using var test = new TestWorld();
        test.GiveRootPointGravity(Mu);
        var parked = new MotionSample(
            Pose: new(new(1, 2, 3), DQuaternion.Identity),
            ChordVelocity: Double3.Zero,
            Velocity: Double3.Zero,
            Acceleration: Double3.Zero,
            AngularVelocity: Double3.Zero,
            AngularAcceleration: Double3.Zero
        );
        // The other component comes first in the list, so only the priority of KeplerMotion makes it run first.
        FrameHandle moon = SpawnFrame(
            test,
            components:
            [
                Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(parked)),
                new KeplerMotion { Orbit = KeplerOrbit.Circular(7e6) },
            ]
        );

        test.Run(1);

        Applied(test, moon).Should().Be(parked);
    }

    /// <summary>
    /// A swap of rails components leaves no step without a motion intent, and the new component starts where the old one's
    /// last sample leaves the frame, which it reads through <see cref="Self.PoseAtNextStepStartIn"/>: so a SpinMotion that
    /// replaces a flight turns the frame where the flight's last sample put it, and holds it there.
    /// </summary>
    [Fact]
    public void ASpinMotionStartsWhereTheLastSampleOfTheComponentBeforeItLeftTheFrame()
    {
        using var test = new TestWorld();
        var velocity = new Double3(10, -2, 0.5);
        var flight = Lambda.OnTick(
            (self, scoped ref events, scoped ref outbox) =>
            {
                Double3 place = velocity * self.TimeAt(self.Tick + 2);
                self.MoveOnRails(
                    new(
                        Pose: new(place, DQuaternion.Identity),
                        ChordVelocity: velocity,
                        Velocity: velocity,
                        Acceleration: Double3.Zero,
                        AngularVelocity: Double3.Zero,
                        AngularAcceleration: Double3.Zero
                    )
                );
            }
        );
        var spin = new FrameSpin(Tilt: DQuaternion.FromAxisAngle(Double3.UnitZ, 0.1), Rate: 0.7);
        FrameHandle frame = SpawnFrame(test, components: flight);
        test.Run(10);

        test.World.RemoveComponent(frame.Entity, flight);
        test.World.AddComponent(frame.Entity, new SpinMotion { Spin = spin });
        test.Run(1);
        Double3 stop = velocity * test.World.Time;
        test.World.Frames.LocalPose(frame)
            .Position.Should()
            .Be(stop, "the flight's last sample moves the frame in the step of the swap");

        test.Run(3);

        test.World.Frames.LocalPose(frame)
            .Position.Should()
            .Be(stop, "the spin turns the frame where the flight left it");
        Applied(test, frame)!.Value.Should().Be(spin.Sample(stop, test.World.TimeAt(test.World.TickNumber + 1)));
    }

    // ---- The checks of a run ------------------------------------------------------------------------------------

    [Fact]
    public void AKeplerMotionWhoseParentHasNoGMWritesNoSampleAndLogsOnce()
    {
        using var test = new TestWorld();
        var log = new List<string>();
        test.World.Log += log.Add;
        FrameHandle moon = SpawnFrame(test, components: new KeplerMotion { Orbit = KeplerOrbit.Circular(7e6) });

        test.Run(3);

        Applied(test, moon).Should().BeNull();
        test.World.Frames.Followed(moon).Should().Be(FollowKind.Static);
        log.Where(line => line.Contains(moon.Entity.ToString())).Should().ContainSingle();
    }

    [Fact]
    public void AKeplerMotionWithoutAnOrbitWritesNoSampleAndLogsOnce()
    {
        // A blueprint that names no orbit makes the component without running the property's setter, so it keeps the
        // default orbit, which is not valid.
        using var test = new TestWorld();
        test.GiveRootPointGravity(Mu);
        var log = new List<string>();
        test.World.Log += log.Add;
        FrameHandle moon = SpawnFrame(test, components: new KeplerMotion());

        test.Run(3);

        Applied(test, moon).Should().BeNull();
        test.World.Frames.Followed(moon).Should().Be(FollowKind.Static);
        log.Where(line => line.Contains(moon.Entity.ToString())).Should().ContainSingle();
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ASpinMotionWithoutASpinHoldsItsFrameAtNoRotation()
    {
        // A blueprint that names no spin makes the component without running the property's setter, so it keeps the
        // default spin: no tilt and a rate of 0, which is a valid spin. So the component has a sample to write, unlike a
        // KeplerMotion without an orbit.
        using var test = new TestWorld();
        var place = new Double3(5, -6, 7);
        FrameHandle ground = SpawnFrame(test, place, new SpinMotion());

        test.Run(3);

        Applied(test, ground).Should().Be(default(FrameSpin).Sample(place, test.World.TimeAt(4)));
        Applied(test, ground)!.Value.Pose.Rotation.Should().Be(DQuaternion.Identity);
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheLogsOfARailsComponentNeverReachWorldErrors()
    {
        using var test = new TestWorld();
        var log = new List<string>();
        test.World.Log += log.Add;
        FrameHandle moon = SpawnFrame(test, components: new KeplerMotion { Orbit = KeplerOrbit.Circular(7e6) });

        test.Run(3);

        log.Should()
            .Contain(
                line => line.Contains(moon.Entity.ToString()),
                "the component logged that it holds its frame still"
            );
        test.World.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("KeplerMotion")]
    [InlineData("SpinMotion")]
    public void ARailsComponentOnAFrameWithoutABodyWritesNoTeleport(string component)
    {
        using var test = new TestWorld();
        test.GiveRootPointGravity(Mu);
        IComponent rails =
            component == "KeplerMotion"
                ? new KeplerMotion { Orbit = KeplerOrbit.Circular(7e6) }
                : new SpinMotion { Spin = new(Tilt: DQuaternion.Identity, Rate: 0.5) };
        FrameHandle frame = SpawnFrame(test, components: rails);

        test.Run(2);

        // A teleport of an entity without a body throws, and the World reports it as an error.
        test.World.Errors.Should().BeEmpty();
        Applied(test, frame).Should().NotBeNull();
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    /// <summary>Spawns a frame without a body at <paramref name="place"/> in the root, and returns it.</summary>
    private static FrameHandle SpawnFrame(TestWorld test, Double3 place = default, params IComponent[] components) =>
        test.World.GetOwnedFrame(
            test.World.Spawn(
                new()
                {
                    Frame = test.Root,
                    Pose = new(place, DQuaternion.Identity),
                    AsFrame = new(FrameBounds.Sphere(10)),
                    Components = components,
                }
            )
        );

    /// <summary>The full sample that the next step applies to the owner of <paramref name="frame"/>, between two ticks, or null.</summary>
    private static MotionSample? Applied(TestWorld test, FrameHandle frame) =>
        test.World.Entities.NextStepIntent(frame.Entity).Move.TryGetValue(out MotionSample sample) ? sample : null;
}
