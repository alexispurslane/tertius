using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Where the two forms of a character (ticket ter-1w06) meet the anchors, following frames and intent conversion of ticket
/// ter-dhkf, on the World and the real Jolt backend. A character takes its form from the effective gravity in its physics
/// home at the start of each step: the rigid form below <see cref="FrameTree.ZeroGravityThreshold"/>, the walking form
/// from twice it on. The following frame K of a Kepler hull falls with the hull, so the gravity in K is the tidal pull, while
/// earth's own system has the full pull. Gameplay writes an intent in the frame that the character was in during the tick,
/// and sync step 7b converts it into the frame that a hand-off of that tick's step gave the character, before the next
/// step gives the character the form that the new frame's gravity asks for.
/// </summary>
public class CharacterFormNumericFrameTests
{
    private const float PersonMass = 70;

    /// <summary>The radius of the bounding sphere of <see cref="Person"/>, in meters.</summary>
    private const float PersonRadius = 0.9f;

    // ---- A character that drifts out of a Kepler station ----------------------------------------------------------

    /// <summary>The station's bubble, a sphere of 16 m.</summary>
    private const float StationRadius = 16;

    /// <summary>A person: a capsule of half height 0.6 m and radius 0.3 m, so a bounding sphere of 0.9 m, and 70 kg.</summary>
    private static readonly BodyDesc Person = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character,
        Mass: PersonMass);

    /// <summary>A circular low orbit of 6,800 km round earth, at about 7.66 km/s, whose frame keeps the axes of earth. The pull there is 8.6 m/s².</summary>
    private static readonly KeplerOrbit LowOrbit = KeplerOrbit.Circular(6.8e6, phase: 0.4);

    /// <summary>A kinematic hull that its frame's Kepler orbit moves: a box of 4 m.</summary>
    private static readonly BodyDesc Core = new(Shape: new BoxShapeDesc(new(2)), Motion: BodyMotion.Kinematic, Mass: 1e4f,
        Restitution: 0);

    /// <summary>
    /// A kinematic hull that its frame's Kepler orbit moves: <see cref="Core"/>, and a plate of 16 m by 0.5 m by 16 m across the +Y
    /// axis, whose face toward the core is 19.75 m from it. The plate makes the hull's bounding sphere about 21.6 m wide, so
    /// that the merge test links the hull to a body near the bubble of 16 m, as in <see cref="NumericFrameOrbitTests"/>.
    /// </summary>
    private static readonly BodyDesc StationHull = new(Shape: new CompoundShapeDesc([
        new(new BoxShapeDesc(new(2)), new(Vector3.Zero, Quaternion.Identity)),
        new(new BoxShapeDesc(new(8, 0.25f, 8)), new(new(0, 20, 0), Quaternion.Identity))
    ]), Motion: BodyMotion.Kinematic, Mass: 1e5f, Restitution: 0);

    // ---- Intent conversion and a switch of form ------------------------------------------------------------------

    /// <summary>A turn that moves every axis: 90 degrees about Z, then 90 degrees about the new X.</summary>
    private static DQuaternion Turned => DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2) *
                                         DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2);

    private static World NewWorld(bool numericFrames, out FaultInjectingBackend faults) =>
        NewWorldWithFaults(faults: out faults,
            configure: numericFrames ? null : options => options.NumericFrames = WithoutNumericFrames);

    /// <summary>Earth with its point gravity.</summary>
    private static FrameHandle Earth(World world) =>
        world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(Scenes.EarthMu)));

    /// <summary>What a test sees of a character at the end of a tick: the kind of its physics home and its form.</summary>
    private static string State(World world, JoltPhysicsBackend backend, Entity person, FrameHandle station,
        FrameHandle earth)
    {
        FrameHandle home = world.PhysicsHomeOf(person);
        string kind = home == station ? "station" : home == earth ? "earth" : world.Frames.Numeric(home).ToString();
        return $"{kind} {(backend.HasRigidForm(person) ? "rigid" : "walking")}";
    }

    /// <summary>
    /// Space has no gravity, so a character there has the rigid form. It flies along +X at 12 m/s into the bubble of a ship
    /// with a dynamic hull, turned by <see cref="Turned"/>, that drifts at 0.5 m/s along +Y, and whose frame has a
    /// gravity of 9.81 m/s² along its own -Y. The step of tick 30 hands the character into the ship, in the rigid form,
    /// which a hand-off keeps. In tick 30 gameplay adds an impulse of 140 N·s along space's +Y, which sync step 7b converts
    /// into the ship's axes. The step of tick 31 gives the character the walking form, because the ship's gravity asks for
    /// it, and the walking form applies the converted impulse: 2 m/s along space's +Y. The ship's -Y is space's -Z, so the
    /// impulse without the conversion would push along the ship's +Y, which is space's +Z, against the ship's gravity. The
    /// ship's gravity adds at most one step of its pull along space's -Z: the read is the chord of the step, which carries
    /// half of it.
    /// </summary>
    [Fact]
    public void
        AnImpulseAddedInTheTickOfAWeightlessCharactersHandOffIntoAShipWithGravityPushesTheWalkingFormAlongTheAxesOfTheFrameThatGameplayNamed()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Entity hull = world.Spawn(new()
        {
            Frame = space,
            Pose = new(new(30, 0, 0), Turned),
            LinearVelocity = new(0, 0.5, 0),
            Body = new(Shape: new BoxShapeDesc(new(2)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(20), GravityField.Uniform(new(0, -9.81f, 0)))
        });
        FrameHandle ship = world.GetOwnedFrame(hull);
        var pilot = new Pilot();
        Scenes.Approach approach = Scenes.ApproachToBubble(world: world, into: ship, tick: 30, ballRadius: PersonRadius);
        Entity person = world.Spawn(new()
        {
            Frame = space, Pose = approach.Pose, LinearVelocity = approach.Velocity, Body = Person, Components = [pilot]
        });
        Tick(world, 30);
        world.GetFrame(person).Should().Be(space);
        faults.Jolt.HasRigidForm(person).Should().BeTrue("space has no gravity, so the character is weightless");
        Double3 before = world.VelocityIn(person, space);

        pilot.Once = body => body.AddImpulse(new(0, 2 * PersonMass, 0));
        world.Tick();
        world.GetFrame(person).Should().Be(ship, "the step of tick 30 handed the character into the ship");
        faults.Jolt.HasRigidForm(person).Should().BeTrue("a hand-off keeps the form");
        world.Tick();
        faults.Jolt.HasRigidForm(person).Should()
            .BeFalse("the ship's gravity of 9.81 m/s² gives the character the walking form");

        Double3 down = world.PoseIn(hull, space).Rotation.Rotate(new Double3(0, -1, 0));
        (down - new Double3(0, 0, -1)).Length.Should().BeLessThan(1e-9, "the ship's -Y is space's -Z");
        Double3 change = world.VelocityIn(person, space) - before;
        double fall = Double3.Dot(change, down);
        Double3 across = change - down * fall;
        (across - new Double3(0, 2, 0)).Length.Should().BeLessThan(1e-4,
            $"the impulse of 140 N·s along space's +Y gives the character of 70 kg 2 m/s along it, not {change}");
        fall.Should().BeInRange(0, 9.81 * world.DeltaTime,
            "the ship's gravity pulled the character along space's -Z for one step");
        world.Errors.Should().BeEmpty();
    }

    // ---- A character beside a Kepler hull ------------------------------------------------------------------------

    /// <summary>
    /// A frame on <see cref="LowOrbit"/>, whose hull is <see cref="Core"/> with a bubble of 1 cm, and a character at rest
    /// relative to it, 3.5 m below its core along earth's -Y, within the reach of the merge test. With numeric frames the
    /// character joins the hull's following frame K, which falls with the hull, so the gravity at the character is the tidal pull
    /// of about 1e-5 m/s², and the character has the rigid form. Without numeric frames it lives in earth's own system,
    /// where the pull is 8.6 m/s², so it keeps the walking form and falls freely, without standing. Either way it falls with
    /// the hull and stays beside it: in K to a micrometer, and in earth's own system to a few millimeters, because a float
    /// velocity of 7.66 km/s steps by 0.5 mm/s, and each step's pull of 0.14 m/s rounds to that step. The hull is on its
    /// orbit from its first step, by its first motion intent. Without one it would hold still for that step, and K with it
    /// (ticket ter-zsku), so K's system would show the whole pull of the orbit's speed lost in one step.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void
        ACharacterAtRestBesideAKeplerHullIsRigidInTheHullsFollowingFrameAndWalksInEarthsOwnSystemAndStaysBesideTheHull(
            bool numericFrames)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        FrameHandle earth = Earth(world);
        Entity hull = world.Spawn(new SpawnDesc
        {
            Frame = earth, Body = Core, AsFrame = new(FrameBounds.Sphere(0.01f))
        }.OnKeplerOrbit(world, LowOrbit, firstMove: true));
        (Double3 place, Double3 velocity) = LowOrbit.StateAt(world.Time, Scenes.EarthMu);
        var offset = new Double3(0, -3.5, 0);
        Entity person = world.Spawn(new()
        {
            Frame = earth, Pose = new(place + offset, DQuaternion.Identity), LinearVelocity = velocity, Body = Person
        });
        world.Tick();

        FrameHandle home = world.PhysicsHomeOf(person);
        if (numericFrames)
        {
            world.Frames.Numeric(home).Should().Be(NumericMode.Following);
            home.Should().Be(world.PhysicsHomeOf(hull), "the character shares the hull's following frame");
            faults.Jolt.HasRigidForm(person).Should()
                .BeTrue("the following frame falls with the hull, so only the tidal pull acts in it");
        }
        else
        {
            home.Should().Be(earth);
            faults.Jolt.HasRigidForm(person).Should().BeFalse("earth's own system has the full pull of 8.6 m/s²");
        }

        Tick(world, 59);

        faults.Jolt.HasRigidForm(person).Should().Be(numericFrames, "the character keeps its form for the second");
        world.Snapshot.IsStanding(person).Should().BeFalse("nothing stands the character, which falls freely");
        Double3 drift = world.PoseIn(person, earth).Position - world.PoseIn(hull, earth).Position - offset;
        drift.Length.Should().BeLessThan(numericFrames ? 1e-5 : 0.02,
            $"the character falls with the hull, 7.66 km in the second, and drifted {drift}");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The scene of
    /// <see cref="ACharacterAtRestBesideAKeplerHullIsRigidInTheHullsFollowingFrameAndWalksInEarthsOwnSystemAndStaysBesideTheHull"/>
    /// with numeric frames, with a first motion intent of the hull. The hull's new following frame K starts with the chord
    /// velocity that the numeric frames expect (docs/plans/rails-components.md, "The first step of a new following frame").
    /// So K's acceleration is the pull of 8.6 m/s² in each planned step, the first included, where it lands on its anchor's
    /// plan. The gravity in K is the tidal pull in every step, and the character at rest in K has the rigid form from its
    /// first step on. When K started with the hull's instant velocity, its first planned step took half the pull, and the
    /// character walked in that step. Before ticket ter-zsku the test ran without a first motion intent too, in which K's
    /// first step fell. Since ter-zsku the hull holds still in that step, and K with it, so K's system shows the orbit's speed
    /// lost in one step, about 460,000 m/s², and a character there is not weightless in that step.
    /// </summary>
    [Fact]
    public void ACharacterAtRestBesideANewKeplerHullIsRigidInEveryStepOfTheHullsFollowingFrame()
    {
        using World world = NewWorld(numericFrames: true, out FaultInjectingBackend faults);
        FrameHandle earth = Earth(world);
        Entity hull = world.Spawn(new SpawnDesc
        {
            Frame = earth, Body = Core, AsFrame = new(FrameBounds.Sphere(0.01f))
        }.OnKeplerOrbit(world, LowOrbit, firstMove: true));
        (Double3 place, Double3 velocity) = LowOrbit.StateAt(world.Time, Scenes.EarthMu);
        Entity person = world.Spawn(new()
        {
            Frame = earth, Pose = new(place + new Double3(0, -3.5, 0), DQuaternion.Identity), LinearVelocity = velocity,
            Body = Person
        });

        var forms = new List<string>();
        for (int step = 1; step <= 4; step++)
        {
            world.Tick();
            FrameHandle home = world.PhysicsHomeOf(person);
            home.Should().Be(world.PhysicsHomeOf(hull),
                $"the character shares the hull's following frame in step {step}");
            forms.Add(faults.Jolt.HasRigidForm(person) ? "rigid" : "walking");
            world.Frames.EffectiveGravity(home, world.PoseIn(person, home).Position).Length.Should().BeLessThan(1e-3,
                $"the following frame falls with the pull in step {step}, so its system shows only the tidal pull of about 1e-5 m/s² and the pull's change over a step, 1.6e-4 m/s²");
        }

        forms.Should().Equal(["rigid", "rigid", "rigid", "rigid"],
            "the gravity in the following frame is the tidal pull in every step");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A station on <see cref="LowOrbit"/>, whose hull is <see cref="StationHull"/> when <paramref name="plate"/> is true
    /// and <see cref="Core"/> otherwise, and whose bubble is a sphere of <see cref="StationRadius"/>, and a character in it,
    /// 10 m along +Y from the core, that drifts along +Y at 1 m/s relative to the station. The station's frame falls with
    /// its orbit and has no gravity of its own, so the character has the rigid form there. While it sticks out of the bubble
    /// it has a ghost in the station's tree parent, the home of the station's hull: the following frame K with numeric frames,
    /// earth's own system without. With numeric frames, no body of earth may touch that ghost, so earth's own system keeps
    /// none (ticket ter-4psc). It leaves the bubble into the tree parent. With numeric
    /// frames that is the station's following frame K: the merge test links the character to a hull with the plate, so it stays
    /// in K, and it does not link it to the core alone, so the same sync point moves it into a falling frame of its own.
    /// Both fall with the station, so the character stays rigid. Without numeric frames the tree parent is earth's own
    /// system, where the next step gives the character the walking form. Either way it drifts on at 1 m/s relative to the
    /// station, 8 m in 480 ticks: in a numeric frame to a micrometer, and in earth's own system, where the walking form
    /// moves at 7.66 km/s, to a millimeter.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void
        ARigidCharacterThatDriftsOutOfAKeplerStationHasAGhostInTheStationsTreeParentWhileItSticksOutAndTakesTheFormThatTheGravityOfItsNewHomeAsksFor(
            bool numericFrames, bool plate)
    {
        using World world = NewWorld(numericFrames, out FaultInjectingBackend faults);
        FrameHandle earth = Earth(world);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = earth, Body = plate ? StationHull : Core, AsFrame = new(FrameBounds.Sphere(StationRadius))
        }.OnKeplerOrbit(world, LowOrbit, firstMove: true));
        FrameHandle station = world.GetOwnedFrame(owner);
        Entity person = world.Spawn(new()
            { Frame = station, Pose = At(0, 10), LinearVelocity = new(0, 1, 0), Body = Person });

        var states = new List<string>();
        bool ghostWhileRigid = false;
        for (int tick = 0; tick < 480; tick++)
        {
            world.Tick();
            string state = State(world: world, backend: faults.Jolt, person: person, station: station, earth: earth);
            if (states.Count == 0 || states[^1] != state)
                states.Add(state);
            FrameHandle parent = world.Frames.TreeParent(station);
            if (world.PhysicsHomeOf(person) == station && faults.Jolt.HasRigidForm(person) &&
                faults.Jolt.HasGhostIn(person, parent))
                ghostWhileRigid = true;
            if (parent != earth)
                faults.Jolt.HasGhostIn(person, earth).Should()
                    .BeFalse($"after tick {tick} no body of earth may touch the character's ghost in K");
        }

        states.Should().Equal(!numericFrames ? ["station rigid", "earth rigid", "earth walking"]
            : plate ? ["station rigid", "Following rigid"]
            : ["station rigid", "Falling rigid"],
            "a hand-off keeps the form, and the next step takes the form of the new home's gravity");
        ghostWhileRigid.Should()
            .BeTrue("a rigid character that sticks out of the bubble is partly in the station's tree parent");
        world.GetFrame(person).Should().Be(earth);
        (world.PoseIn(person, station).Position - new Double3(0, 18, 0)).Length.Should().BeLessThan(
            numericFrames ? 1e-6 : 1e-3,
            "the character drifts on at 1 m/s relative to the station");
        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
    }
}
