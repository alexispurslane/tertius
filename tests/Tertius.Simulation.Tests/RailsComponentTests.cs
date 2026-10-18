using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;
using static Tertius.Simulation.Tests.BlueprintSamples;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The rails components <see cref="KeplerMotion"/> and <see cref="SpinMotion"/> on frames without a body: what they check,
/// and the sample that each writes in each tick. The tests of rails components on hulls are in Physics.Tests
/// (RailsHullTests). These tests read the sample table; KeplerFrameTests, SpinFrameTests and RailsFollowTests (Physics.Tests)
/// check how frames follow the samples.
/// </summary>
public class RailsComponentTests
{
    private const double Mu = 4e14;

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

    /// <summary>An orbit that a <see cref="KeplerMotion"/> cannot follow. The default orbit would leave the frame at its parent's origin.</summary>
    public static TheoryData<string, KeplerOrbit> OrbitsThatAreNotValid => new()
    {
        { "the default orbit", default },
        { "no semi-major axis", KeplerOrbit.Circular(0) },
        { "an open orbit", KeplerOrbit.Circular(1000) with { Eccentricity = 1 } },
        { "a negative eccentricity", KeplerOrbit.Circular(1000) with { Eccentricity = -0.1 } },
        { "a semi-major axis that is not a number", KeplerOrbit.Circular(double.NaN) },
        { "an infinite spin", KeplerOrbit.Circular(1000, spinRate: double.PositiveInfinity) },
    };

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
        var spin = new FrameSpin(DQuaternion.Identity, double.NaN);

        FluentActions.Invoking(() => new SpinMotion { Spin = spin }).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ABlueprintMakesAKeplerMotionFromItsProperties()
    {
        var orbit = KeplerOrbit.Circular(7e6, phase: 0.4);
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("moon", [new ComponentSpec(typeof(KeplerMotion), Fields(("Orbit", orbit)))]));

        var motion = (KeplerMotion)blueprints.Instantiate("moon").Single();

        motion.Orbit.Should().Be(orbit);
    }

    [Fact]
    public void ABlueprintMakesASpinMotionFromItsProperties()
    {
        var spin = new FrameSpin(DQuaternion.FromAxisAngle(Double3.UnitZ, -0.4), 0.7, PhaseAtEpoch: 0.3, Epoch: 2);
        var properAcceleration = new Vector3(0, 9.81f, 0);
        var blueprints = new Blueprints();
        blueprints.Register(new Blueprint("ground",
            [new ComponentSpec(typeof(SpinMotion), Fields(("Spin", spin), ("ProperAcceleration", properAcceleration)))]));

        var motion = (SpinMotion)blueprints.Instantiate("ground").Single();

        motion.Spin.Should().Be(spin);
        motion.ProperAcceleration.Should().Be(properAcceleration);
    }

    // ---- The samples --------------------------------------------------------------------------------------------

    [Fact]
    public void KeplerMotionWritesTheLibrarysSampleForTheEndOfTheStepInWhichItsIntentActsWithZeroProperAcceleration()
    {
        using var test = new TestWorld();
        test.GiveRootPointGravity(Mu);
        var orbit = KeplerOrbit.Circular(7e6, phase: 0.4, spinRate: 0.01);
        FrameHandle moon = SpawnFrame(test, components: new KeplerMotion { Orbit = orbit });

        test.Run(3);

        // Tick 2 wrote the sample, for step 3, which ends at the time of tick 4.
        MotionSample expected = orbit.Sample(Mu, test.World.TimeAt(4), test.World.DeltaTime);
        Applied(test, moon).Should().Be(expected);
        expected.ProperAcceleration.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void SpinMotionWritesTheSpinsSampleAtItsFramesPlaceWithItsProperAcceleration()
    {
        using var test = new TestWorld();
        var spin = new FrameSpin(DQuaternion.FromAxisAngle(Double3.UnitZ, -0.4), 0.7, PhaseAtEpoch: 0.3, Epoch: 2);
        var place = new Double3(5, -6, 7);
        var properAcceleration = new Vector3(0, 9.81f, 0);
        FrameHandle ground = SpawnFrame(test, place, new SpinMotion { Spin = spin, ProperAcceleration = properAcceleration });

        test.Run(3);

        Applied(test, ground).Should().Be(spin.Sample(place, properAcceleration, test.World.TimeAt(4), test.World.DeltaTime));
    }

    [Fact]
    public void ALaterMoveOnRailsFromAnotherComponentOfTheEntityWinsOverTheRailsComponentsSample()
    {
        using var test = new TestWorld();
        test.GiveRootPointGravity(Mu);
        var parked = new MotionSample(new DPose(new Double3(1, 2, 3), DQuaternion.Identity), Double3.Zero, Double3.Zero, Double3.Zero,
            Double3.Zero, Double3.Zero, Vector3.Zero);
        // The other component comes first in the list, so only the priority of KeplerMotion makes it run first.
        FrameHandle moon = SpawnFrame(test, components:
        [
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.MoveOnRails(parked)),
            new KeplerMotion { Orbit = KeplerOrbit.Circular(7e6) },
        ]);

        test.Run(1);

        Applied(test, moon).Should().Be(parked);
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
        Followed(test, moon).Should().BeNull();
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
        Followed(test, moon).Should().BeNull();
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

        Applied(test, ground).Should().Be(default(FrameSpin).Sample(place, Vector3.Zero, test.World.TimeAt(4), test.World.DeltaTime));
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

        log.Should().Contain(line => line.Contains(moon.Entity.ToString()), "the component logged that it holds its frame still");
        test.World.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("KeplerMotion")]
    [InlineData("SpinMotion")]
    public void ARailsComponentOnAFrameWithoutABodyWritesNoTeleport(string component)
    {
        using var test = new TestWorld();
        test.GiveRootPointGravity(Mu);
        IComponent rails = component == "KeplerMotion"
            ? new KeplerMotion { Orbit = KeplerOrbit.Circular(7e6) }
            : new SpinMotion { Spin = new FrameSpin(DQuaternion.Identity, 0.5) };
        FrameHandle frame = SpawnFrame(test, components: rails);

        test.Run(2);

        // A teleport of an entity without a body throws, and the World reports it as an error.
        test.World.Errors.Should().BeEmpty();
        Applied(test, frame).Should().NotBeNull();
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    /// <summary>Spawns a frame without a body and with the Static driver at <paramref name="place"/> in the root, and returns it.</summary>
    private static FrameHandle SpawnFrame(TestWorld test, Double3 place = default, params IComponent[] components) =>
        test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = new DPose(place, DQuaternion.Identity),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            Components = components,
        }));

    private static MotionSample? Applied(TestWorld test, FrameHandle frame) =>
        test.World.Frames.Samples.Applied.TryGet(frame, out MotionSample sample) ? sample : null;

    private static MotionSample? Followed(TestWorld test, FrameHandle frame) =>
        test.World.Frames.Samples.Followed.TryGet(frame, out MotionSample sample) ? sample : null;
}
