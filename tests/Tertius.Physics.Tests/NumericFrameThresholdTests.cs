using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The thresholds of numeric frames under the default membership rules (<see cref="NumericFrameOptions"/>), on the World
/// and the real Jolt backend. A group of free bodies gets a falling frame from 1,000 m/s, and a group with a body that
/// thrusts gets one from 50 m/s. Every character thrusts, always. Any other body thrusts after 0.5 s of ticks whose force
/// and impulse ask for at least 1 mm/s per minute, and keeps thrusting for 20 s after its last such tick. A slow group gets
/// a resting frame from 2^23 m (8,389 km) from its semantic frame's origin.
/// </summary>
/// <remarks>
/// A force or an impulse that a component writes during a tick acts in the physics step of the next tick, and the numeric
/// update at the sync point at the end of the tick reads it: so the thrust of the first tick counts at the first sync
/// point. At 60 Hz, 30 ticks of thrust give 30 × (1/60 s as a float) = 0.50000003 s, and 29 ticks give 0.483 s.
/// </remarks>
public class NumericFrameThresholdTests
{
    private const float CrateMass = 10;

    /// <summary>How many ticks of thrust at 60 Hz a body needs before it thrusts: the first count of ticks of 1/60 s that reaches 0.5 s.</summary>
    private const int OnsetTicks = 30;

    /// <summary>How many quiet ticks at 60 Hz end the thrust of a body: the first count of ticks of 1/60 s that reaches 20 s.</summary>
    private const int WindowTicks = 1200;

    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: CrateMass, LinearDamping: 0, AngularDamping: 0);

    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    /// <summary>A root frame without gravity, with a bubble of 1,000 km.</summary>
    private static FrameHandle Space(World world) => world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));

    /// <summary>A crate at the origin of <paramref name="space"/> that drifts along +X at 60 m/s, with <paramref name="components"/>.</summary>
    private static Entity CrateAt60MetersPerSecond(World world, FrameHandle space, params IComponent[] components) =>
        world.Spawn(new SpawnDesc { Frame = space, LinearVelocity = new Double3(60, 0, 0), Body = Crate, Components = components });

    /// <summary>How the home of the body of <paramref name="entity"/> moves: <see cref="NumericMode.None"/> for its semantic frame's own system.</summary>
    private static NumericMode HomeMode(World world, Entity entity) => world.Frames.Numeric(world.PhysicsHomeOf(entity));

    /// <summary>Ticks <paramref name="ticks"/> times and returns the mode of the home of <paramref name="entity"/> after each tick.</summary>
    private static List<NumericMode> HomeModes(World world, Entity entity, int ticks)
    {
        var modes = new List<NumericMode>();
        for (int i = 0; i < ticks; i++)
        {
            world.Tick();
            modes.Add(HomeMode(world, entity));
        }
        return modes;
    }

    // ---- Speeds ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A free crate at 60 m/s is far below the 1,000 m/s from which a group of free bodies falls, so it stays in the system
    /// of space for 2 s. Under the old single threshold of 50 m/s, it fell from its first tick.
    /// </summary>
    [Fact]
    public void ACrateThrownAt60MetersPerSecondThroughEmptySpaceStaysInItsSemanticFramesOwnSystem()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity crate = CrateAt60MetersPerSecond(world, space);

        List<NumericMode> modes = HomeModes(world, crate, 120);

        modes.Should().AllBeEquivalentTo(NumericMode.None, "a free body at 60 m/s needs no numeric frame");
        world.PhysicsHomeOf(crate).Should().Be(space);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A free crate that coasts at 1,000 m/s falls in a numeric frame from its first tick, and one at 999 m/s stays in the
    /// system of space. Neither changes its mind in 1 s.
    /// </summary>
    [Theory]
    [InlineData(1000, true)]
    [InlineData(999, false)]
    public void ACrateThatCoastsThroughEmptySpaceFallsInANumericFrameFromTheCreateSpeedOfAGroupOfFreeBodies(double speed, bool falls)
    {
        NumericMode expected = falls ? NumericMode.Falling : NumericMode.None;
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity crate = world.Spawn(new SpawnDesc { Frame = space, LinearVelocity = new Double3(speed, 0, 0), Body = Crate });

        List<NumericMode> modes = HomeModes(world, crate, 60);

        modes.Should().AllBeEquivalentTo(expected, $"a free body at {speed} m/s against a create speed of 1,000 m/s");
        world.Errors.Should().BeEmpty();
    }

    // ---- Thrust ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A crate drifts at 60 m/s with a gentle thrust (<see cref="Thruster"/>) from its first tick. Its thrust time reaches
    /// 0.5 s at the sync point of its thirtieth tick, and from then on its group of one thrusts at 60 m/s, above the 50 m/s
    /// from which a group that thrusts falls. So it gets a falling frame at that sync point, not before.
    /// </summary>
    [Fact]
    public void ACrateThatDriftsAt60MetersPerSecondWithAGentleThrustGetsAFallingFrameAtTheTickWhereItsThrustTimeReachesHalfASecond()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity crate = CrateAt60MetersPerSecond(world, space, new Thruster(CrateMass));

        List<NumericMode> modes = HomeModes(world, crate, OnsetTicks + 30);

        modes.IndexOf(NumericMode.Falling).Should().Be(OnsetTicks - 1, "the crate thrusts from the sync point of its thirtieth tick of thrust");
        modes.Skip(OnsetTicks - 1).Should().AllBeEquivalentTo(NumericMode.Falling, "the crate keeps thrusting");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// As <see cref="ACrateThatDriftsAt60MetersPerSecondWithAGentleThrustGetsAFallingFrameAtTheTickWhereItsThrustTimeReachesHalfASecond"/>,
    /// but the crate pushes itself with an impulse on every tick, which asks for the same acceleration as the force of the
    /// thruster: the World counts an impulse over the step of 1/60 s.
    /// </summary>
    [Fact]
    public void ACrateThatAGentleImpulseOnEveryTickPushesThrustsLikeOneThatAForcePushes()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var pilot = new Pilot();
        Entity crate = CrateAt60MetersPerSecond(world, space, pilot);
        float impulse = CrateMass * Thruster.GentleAcceleration * world.DeltaTime;
        pilot.EveryTick = body => body.AddImpulse(new Vector3(impulse, 0, 0));

        List<NumericMode> modes = HomeModes(world, crate, OnsetTicks + 30);

        modes.IndexOf(NumericMode.Falling).Should().Be(OnsetTicks - 1, "the crate thrusts from the sync point of its thirtieth tick of thrust");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The crate of <see cref="ACrateThatDriftsAt60MetersPerSecondWithAGentleThrustGetsAFallingFrameAtTheTickWhereItsThrustTimeReachesHalfASecond"/>
    /// falls in its numeric frame for 1 s, and then its thrust stops. It keeps thrusting for the 20 s of the thrust window
    /// after its last tick of thrust, so it keeps its frame. At the sync point of the 1,200th quiet tick, its clock starts
    /// again, and the crate is a free body at 60 m/s, far below the 400 m/s down to which a falling frame of free bodies
    /// keeps falling: it goes back to the system of space.
    /// </summary>
    [Fact]
    public void ACrateWhoseGentleThrustStopsLeavesItsFallingFrameTwentySecondsAfterItsLastTickOfThrust()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var thruster = new Thruster(CrateMass);
        Entity crate = CrateAt60MetersPerSecond(world, space, thruster);
        Tick(world, 60);
        HomeMode(world, crate).Should().Be(NumericMode.Falling);

        thruster.On = false;
        List<NumericMode> modes = HomeModes(world, crate, WindowTicks + 30);

        modes.IndexOf(NumericMode.None).Should().Be(WindowTicks - 1, "the crate stops thrusting at the sync point of its 1,200th quiet tick");
        modes.Skip(WindowTicks - 1).Should().AllBeEquivalentTo(NumericMode.None, "a free body at 60 m/s needs no numeric frame");
        world.PhysicsHomeOf(crate).Should().Be(space);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Physics gives a force only to a dynamic body or a character, so the force that a kinematic crate asks for is no
    /// thrust: a kinematic crate at 60 m/s with a thruster stays in the system of space for 2 s.
    /// </summary>
    [Fact]
    public void AKinematicCrateWithAThrusterNeverThrusts()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = space, LinearVelocity = new Double3(60, 0, 0), Body = Crate with { Motion = BodyMotion.Kinematic },
            Components = [new Thruster(CrateMass)],
        });

        List<NumericMode> modes = HomeModes(world, crate, 120);

        modes.Should().AllBeEquivalentTo(NumericMode.None, "a kinematic body at 60 m/s does not thrust");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Every character thrusts, always and at once, so a weightless character that drifts at 60 m/s falls in a numeric
    /// frame from its first tick on, although it asks for no force.
    /// </summary>
    [Fact]
    public void AWeightlessCharacterThatDriftsAt60MetersPerSecondFallsInANumericFrameFromItsFirstTickOn()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity person = world.Spawn(new SpawnDesc { Frame = space, LinearVelocity = new Double3(60, 0, 0), Body = Person });

        List<NumericMode> modes = HomeModes(world, person, 120);

        modes.Should().AllBeEquivalentTo(NumericMode.Falling, "a character thrusts, and 60 m/s passes the 50 m/s of a group that thrusts");
        world.Errors.Should().BeEmpty();
    }

    // ---- Distances ---------------------------------------------------------------------------------------------------

    /// <summary>A root frame with a uniform gravity of 9.81 m/s² along -Y and a bubble of 20,000 km.</summary>
    private static FrameHandle Planet(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(2e7f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))));

    /// <summary>
    /// A static floor of 100 m by 100 m in <paramref name="planet"/>, with its top at height 0, <paramref name="distance"/> m
    /// along +X from the origin, and a crate at rest on it.
    /// </summary>
    private static (Entity Floor, Entity Crate) CrateOnFloor(World world, FrameHandle planet, double distance)
    {
        Entity floor = world.Spawn(new SpawnDesc
        {
            Frame = planet, Pose = At(distance, -0.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
        });
        Entity crate = world.Spawn(new SpawnDesc { Frame = planet, Pose = At(distance, 0.5), Body = Crate });
        return (floor, crate);
    }

    /// <summary>
    /// A crate at rest on a static floor 6,371 km from its frame's origin, the radius of the Earth, is nearer than the
    /// 8,389 km from which a slow group rests in a numeric frame, so it stays in the frame's own system for 2 s. Under the
    /// old threshold of 10 km, it rested in a numeric frame.
    /// </summary>
    [Fact]
    public void ACrateAtRestOnAFloor6371KilometersOutStaysInItsSemanticFramesOwnSystem()
    {
        using World world = NewWorld(out _);
        FrameHandle planet = Planet(world);
        (_, Entity crate) = CrateOnFloor(world, planet, 6_371e3);

        List<NumericMode> modes = HomeModes(world, crate, 120);

        modes.Should().AllBeEquivalentTo(NumericMode.None, "a slow body 6,371 km out needs no numeric frame");
        world.PhysicsHomeOf(crate).Should().Be(planet);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A crate at rest on a static floor 10,000 km from its frame's origin is beyond the 8,389 km from which a slow group
    /// rests in a numeric frame. The crate and the floor share a resting frame from the first tick, and the crate stays at
    /// rest on the floor for 2 s.
    /// </summary>
    [Fact]
    public void ACrateAtRestOnAFloor10000KilometersOutSharesARestingFrameWithTheFloorAndStaysAtRestOnIt()
    {
        using World world = NewWorld(out _);
        FrameHandle planet = Planet(world);
        (Entity floor, Entity crate) = CrateOnFloor(world, planet, 10_000e3);

        List<NumericMode> modes = HomeModes(world, crate, 120);

        modes.Should().AllBeEquivalentTo(NumericMode.Resting, "a slow group 10,000 km out rests in a numeric frame");
        world.PhysicsHomeOf(floor).Should().Be(world.PhysicsHomeOf(crate), "the crate on the floor and the floor form one group");
        Double3 position = world.PoseIn(crate, planet).Position;
        position.X.Should().BeApproximately(10_000e3, 1e-3, "the crate stays where it rests");
        position.Y.Should().BeApproximately(0.5, 0.02, "the crate rests on the floor's top at height 0");
        world.VelocityIn(crate, planet).Length.Should().BeLessThan(1e-3, "the crate is at rest");
        world.Errors.Should().BeEmpty();
    }
}
