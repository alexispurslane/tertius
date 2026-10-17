using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Numeric frames (docs/plans/numeric-frames.md): frames that the engine makes around a group of bodies, which fall freely
/// in doubles or rest in their parent, with a Jolt system each. Only the numeric update at each sync point makes and moves
/// them, so a test that needs one builds a scene that is fast or far enough for the default membership rules
/// (<see cref="NumericFrameOptions"/>), or uses one of the rule sets of <see cref="NumericFrameTesting"/> on purpose.
/// </summary>
public class WorldNumericFrameTests
{
    /// <summary>Earth's G·M, in m³/s², as in the demo scenes.</summary>
    private const double EarthMu = 3.986004418e14;

    /// <summary>The radius of the ship's orbit in the demo scenes, 400 km above Earth's mean radius, in meters.</summary>
    private const double OrbitRadius = 6.371e6 + 400e3;

    private static readonly double OrbitSpeed = Math.Sqrt(EarthMu / OrbitRadius);

    /// <summary>
    /// A distance from a frame's origin, in meters, at which a slow group rests in a numeric frame: 10,000 km, beyond the
    /// 2^23 m (8,389 km) of <see cref="NumericFrameOptions.CreateDistance"/>.
    /// </summary>
    private const double FarOut = 1e7;

    /// <summary>
    /// How many ticks of thrust at 60 Hz a body needs before it thrusts: the first count of ticks of 1/60 s that reaches the
    /// 0.5 s of <see cref="NumericFrameOptions.ThrustOnset"/>.
    /// </summary>
    private const int OnsetTicks = 30;

    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);

    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    private static readonly BodyDesc Hull = new(new BoxShapeDesc(new Vector3(5)), Mass: 1e4f, LinearDamping: 0, AngularDamping: 0,
        InteriorShape: new SphereShapeDesc(0.05f));

    private static FrameHandle Earth(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu)));

    private static bool InFallingFrame(World world, Entity entity) =>
        world.Frames.Numeric(world.PhysicsHomeOf(entity)) == NumericMode.Falling;

    [Fact]
    public void ACrateInAFallingNumericFrameFollowsTheOrbitThatItsSpawnGivesInEarth()
    {
        // The lookahead links the crate to a heavy block 100 m away, so their frame's origin stays near the block and the
        // crate drifts away from it, where tidal gravity acts.
        using World world = NewWorld(out _, debugChecks: false, configure: o => o.NumericFrames = new NumericFrameOptions { LookaheadTime = 1e4 });
        FrameHandle earth = Earth(world);
        world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = At(OrbitRadius),
            LinearVelocity = new Double3(0, 0, OrbitSpeed),
            Body = Crate with { Mass = 1e5f },
        });
        var start = new Double3(OrbitRadius + 100, 0, 30);
        var startVelocity = new Double3(0.1, 0, OrbitSpeed + 0.05);
        Entity crate = world.Spawn(new SpawnDesc { Frame = earth, Pose = new DPose(start, DQuaternion.Identity), LinearVelocity = startVelocity, Body = Crate });

        // The orbit of the spawn's state, stepped in doubles with the scheme that Jolt uses for every body. A float of
        // 7,672 m/s steps by 488 µm/s, which would move the crate by up to 3 cm in the two minutes.
        double dt = world.DeltaTime;
        // Jolt steps a chord, so the scheme starts from the chord of the spawn's instant velocity: half a step of gravity behind it.
        Double3 position = start, velocity = startVelocity - GravityField.Point(EarthMu).AtPrecise(start) * (dt / 2);
        const int ticks = 120 * 60;
        for (int i = 0; i < ticks; i++)
        {
            velocity += GravityField.Point(EarthMu).AtPrecise(position) * dt;
            position += velocity * dt;
        }

        Tick(world, ticks);

        world.Errors.Should().BeEmpty();
        InFallingFrame(world, crate).Should().BeTrue();
        Double3 reached = world.PoseIn(crate, earth).Position;
        (reached - position).Length.Should().BeLessThan(1e-3, $"the crate reached {reached}, and the orbit {position}");
        FrameHandle home = world.PhysicsHomeOf(crate);
        (reached - world.Frames.HomeOffset(home, earth)).Length.Should().BeGreaterThan(100, "the crate stayed away from the frame's origin");
    }

    [Fact]
    public void ABodyWhoseHomeIsAFallingNumericFrameReadsBackAnOrbitalVelocityToAMicrometerPerSecond()
    {
        using World world = NewWorld(out _);
        FrameHandle earth = Earth(world);
        var pilot = new Pilot();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = At(0, OrbitRadius),
            LinearVelocity = new Double3(OrbitSpeed, 0, 0),
            Body = Crate,
            Components = [pilot],
        });

        pilot.Once = body => body.SetVelocity(new Double3(7672.123456, 0, 0));
        Tick(world, 1);
        InFallingFrame(world, crate).Should().BeTrue();
        // The velocity applies in the next step, which also adds the step's gravity: Earth's field at the crate, which
        // has moved 128 m along X, so the field has a part along X of 164 µm/s².
        Double3 field = GravityField.Point(EarthMu).AtPrecise(world.PoseIn(crate, earth).Position);
        Tick(world, 1);

        // A float of 7,672 m/s steps by 488 µm/s.
        world.Errors.Should().BeEmpty();
        // The set velocity is an instant velocity, and the velocity after the step is the chord of the step.
        world.VelocityIn(crate, earth).X.Should().BeApproximately(7672.123456 + field.X * world.DeltaTime / 2, 1e-6);
    }

    [Fact]
    public void AGroupThatStartsToFallAtOrbitalSpeedKeepsTheVelocityOfEachBodyToAMicrometerPerSecond()
    {
        // Two crates 10,000 km out rest in one numeric frame, then speed up to orbital speed, far above the 1,000 m/s from
        // which a group of free bodies falls. The frame takes over their mass-weighted mean velocity, which no float holds,
        // and each body loses it.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var lighter = new Pilot();
        var heavier = new Pilot();
        Entity a = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(FarOut), Body = Crate, Components = [lighter] });
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(FarOut, 0, 1.5), Body = Crate with { Mass = 20 }, Components = [heavier] });
        Tick(world, 2);
        world.Frames.Numeric(world.PhysicsHomeOf(a)).Should().Be(NumericMode.Resting);

        lighter.Once = body => body.SetVelocity(new Double3(7672.1, 0, 0));
        heavier.Once = body => body.SetVelocity(new Double3(7672.4, 0, 0));
        TickUntil(world, () => InFallingFrame(world, a), 5);
        InFallingFrame(world, a).Should().BeTrue();
        Double3 afterShift = world.VelocityIn(a, ground);

        // No gravity and no contact: physics keeps the velocity that the shift left in Jolt.
        Tick(world, 1);

        world.Errors.Should().BeEmpty();
        (world.VelocityIn(a, ground) - afterShift).Length.Should().BeLessThan(1e-6);
    }

    [Fact]
    public void ASleepingStackThatMovesIntoANumericFrameStaysAsleepWithNoNewContactAndNoFrameChanged()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Entity floor = Floor(world, ground);
        var lowerLog = new CollisionLog();
        var upperLog = new CollisionLog();
        var lowerFrames = new FrameChangedLog();
        var upperFrames = new FrameChangedLog();
        Entity lower = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = Cube(), Components = [lowerLog, lowerFrames] });
        var nudge = new Pilot();
        Entity upper = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 1.5), Body = Cube(), Components = [upperLog, upperFrames, nudge] });
        Tick(world, 1);
        TickUntil(world, () => !IsAwake(backend, ground, lower) && !IsAwake(backend, ground, upper), 600);
        IsAwake(backend, ground, upper).Should().BeFalse("the stack came to rest");
        int lowerContacts = lowerLog.Seen.Count, upperContacts = upperLog.Seen.Count;
        DPose upperPose = world.PoseIn(upper, ground);

        // From now on, every slow group gets a resting frame: the floor and the stack on it.
        world.Frames.NumericOptions = EverySlowGroupRests;
        Tick(world, 60);
        FrameHandle numeric = world.PhysicsHomeOf(upper);

        world.Errors.Should().BeEmpty();
        world.Frames.Numeric(numeric).Should().Be(NumericMode.Resting);
        world.PhysicsHomeOf(floor).Should().Be(numeric);
        IsAwake(backend, numeric, lower).Should().BeFalse();
        IsAwake(backend, numeric, upper).Should().BeFalse();
        world.GetFrame(upper).Should().Be(ground);
        (world.PoseIn(upper, ground).Position - upperPose.Position).Length.Should().BeLessThan(1e-9);

        // A nudge wakes the stack. Jolt finds the contacts of the woken boxes again, and they go on.
        nudge.Once = body => body.AddImpulse(new Vector3(0.05f, 0, 0));
        Tick(world, 30);
        IsAwake(backend, numeric, upper).Should().BeTrue("the nudge woke the upper box");
        lowerLog.Seen.Should().HaveCount(lowerContacts, "the contacts go on");
        upperLog.Seen.Should().HaveCount(upperContacts, "the contacts go on");
        lowerLog.Ended.Should().BeEmpty("a move between numeric frames ends no contact");
        upperLog.Ended.Should().BeEmpty("a move between numeric frames ends no contact");
        lowerFrames.Seen.Should().BeEmpty();
        upperFrames.Seen.Should().BeEmpty();
    }

    [Fact]
    public void TheFramesThatGameplaySeesAreOnlySemanticFramesWhileANumericFrameExists()
    {
        using World world = NewWorld(out _, configure: o => o.NumericFrames = EverySlowGroupRests);
        FrameHandle ground = Ground(world, 0);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(2), Body = Cube() });
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(-20, 0, 3),
            Body = Cube(100) with { InteriorShape = new SphereShapeDesc(0.05f) },
            AsFrame = new FrameSpec(FrameBounds.Sphere(5), MotionDriver.Physics),
        });
        FrameHandle ship = world.Frames.TryGetFrame(owner, out FrameHandle own) ? own : FrameHandle.Null;

        Tick(world, 5);

        world.Errors.Should().BeEmpty();
        world.Frames.IsNumeric(world.Frames.TreeParent(ship)).Should().BeTrue("the ship's frame lies in its hull's numeric frame");
        var all = new List<FrameHandle>();
        world.Frames.GetAll(all);
        all.Should().Equal(ground, ship);
        var children = new List<FrameHandle>();
        world.Frames.GetChildren(ground, children);
        children.Should().Equal(ship);
        world.Frames.Parent(ship).Should().Be(ground);
        world.Snapshot.Frames.Parent(ship).Should().Be(ground);
        world.GetFrame(crate).Should().Be(ground);
        world.Snapshot.FrameOf(crate).Should().Be(ground);
        world.Snapshot.FrameOf(owner).Should().Be(ground);
        var near = new List<Entity>();
        world.Snapshot.QueryRadius(ground, new Double3(2, 0, 0), 1, near);
        near.Should().Contain(crate);

        // The frame's pose and motion read relative to its parent, ground, not to the numeric frame between them.
        (world.Frames.LocalPose(ship).Position - new Double3(-20, 0, 3)).Length.Should().BeLessThan(1e-6);
        world.Frames.LinearVelocity(ship).Length.Should().BeLessThan(1e-6);
        (world.Frames.PointToParent(ship, Double3.Zero) - new Double3(-20, 0, 3)).Length.Should().BeLessThan(1e-6);
        (world.Frames.PointFromParent(ship, new Double3(-20, 0, 3))).Length.Should().BeLessThan(1e-6);
        world.Frames.VelocityToParent(ship, Double3.Zero, Double3.Zero).Length.Should().BeLessThan(1e-6);
        (world.Snapshot.Frames.LocalPose(ship).Position - new Double3(-20, 0, 3)).Length.Should().BeLessThan(1e-6);
    }

    [Fact]
    public void APersonWhoseHomeIsAFallingNumericFrameDoesNotStandOnAFloorThatFallsWithIt()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        // A kinematic floor, which can fall with a numeric frame. A static body never does: it stays in its own system.
        Entity floor = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Kinematic),
        });
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.92), Body = Person });
        Tick(world, 30);
        world.Snapshot.IsStanding(person).Should().BeTrue("the person stands on the floor in the frame ground");

        // As outside a ship in orbit: the person, and what it touches, fall freely in a numeric frame.
        world.Frames.NumericOptions = EveryGroupFalls;
        Tick(world, 30);

        world.Errors.Should().BeEmpty();
        InFallingFrame(world, person).Should().BeTrue();
        world.PhysicsHomeOf(floor).Should().Be(world.PhysicsHomeOf(person));
        world.Snapshot.IsStanding(person).Should().BeFalse("in free fall the effective gravity is below the zero-gravity threshold");
        (world.PoseIn(person, ground).Position.Y - world.PoseIn(floor, ground).Position.Y).Should().BeApproximately(1.42, 0.05,
            "the person floats where it stood, above the floor that falls with it");
    }

    [Fact]
    public void AShipFrameUnderAFallingNumericFrameCarriesItsContentsWithIt()
    {
        using World world = NewWorld(out _);
        FrameHandle earth = Earth(world);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = At(OrbitRadius),
            LinearVelocity = new Double3(0, 0, OrbitSpeed),
            Body = Hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Physics),
        });
        world.Frames.TryGetFrame(owner, out FrameHandle ship).Should().BeTrue();
        Entity cargo = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(1, 2, 3), Body = Crate });

        DPose start = world.PoseIn(owner, earth);
        Tick(world, 120);

        world.Errors.Should().BeEmpty();
        InFallingFrame(world, owner).Should().BeTrue();
        world.Frames.Parent(ship).Should().Be(earth);
        world.GetFrame(cargo).Should().Be(ship);
        (world.PoseIn(cargo, ship).Position - new Double3(1, 2, 3)).Length.Should().BeLessThan(1e-3, "the cargo floats where it was in the ship");
        DPose shipPose = world.PoseIn(owner, earth);
        (shipPose.Position - start.Position).Length.Should().BeGreaterThan(15_000, "the ship moved along its orbit for 2 s");
        (world.PoseIn(cargo, earth).Position - shipPose.TransformPoint(new Double3(1, 2, 3))).Length.Should().BeLessThan(1e-3,
            "the cargo moved with the ship");
    }

    [Fact]
    public void AShipInAFallingNumericFrameThatTheGameParksStopsWhereItIs()
    {
        using World world = NewWorld(out _);
        FrameHandle earth = Earth(world);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = At(OrbitRadius),
            LinearVelocity = new Double3(0, 0, OrbitSpeed),
            Body = Hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Physics),
        });
        world.Frames.TryGetFrame(owner, out FrameHandle ship).Should().BeTrue();
        Tick(world, 2);
        InFallingFrame(world, owner).Should().BeTrue();

        world.SetFrameDriver(ship, MotionDriver.Static);
        DPose parked = world.PoseIn(owner, earth);
        Tick(world, 60);

        world.Errors.Should().BeEmpty();
        world.Frames.TreeParent(ship).Should().Be(earth, "a frame that physics does not drive moves relative to its semantic parent");
        world.PhysicsHomeOf(owner).Should().Be(earth);
        (world.PoseIn(owner, earth).Position - parked.Position).Length.Should().BeLessThan(1e-6, "a parked ship does not fall");
        world.VelocityIn(owner, earth).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>
    /// With no gravity, a static wall 1 m thick and 10 m square across the X axis at the origin of <paramref name="ground"/>,
    /// with <paramref name="wallComponents"/>, and a crate of 10 kg 200 m before it that flies at it along +X at 60 m/s, with
    /// a gentle thrust along +X from its first tick (<see cref="Thruster"/>) and <paramref name="crateComponents"/>. A free
    /// crate at 60 m/s needs no numeric frame, but after 0.5 s of thrust the crate thrusts, and a group that thrusts falls
    /// from 50 m/s (<see cref="NumericFrameOptions.ThrustCreateSpeed"/>). The merge test reaches 128 m from the wall's
    /// center: the bounding spheres of the wall (7.09 m) and the crate (0.87 m), plus their relative speed times the
    /// lookahead time of 2 s. So at the sync point of its thirtieth tick of thrust the crate is 170 m from the wall's center
    /// and falls, and about 43 ticks later the wall comes within the reach, about 2 s before the crate hits it.
    /// </summary>
    private static (Entity Wall, Entity Crate) CrateThatThrustsAtAWall(World world, FrameHandle ground, IComponent[] wallComponents,
        IComponent[] crateComponents)
    {
        Entity wall = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 5, 5)), BodyMotion.Static),
            Components = wallComponents,
        });
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = ground, Pose = At(-200), LinearVelocity = new Double3(60, 0, 0), Body = Crate,
            Components = [new Thruster(Crate.Mass), .. crateComponents],
        });
        return (wall, crate);
    }

    /// <summary>
    /// A crate flies at 60 m/s with a gentle thrust at a static wall (<see cref="CrateThatThrustsAtAWall"/>). Far from the
    /// wall, it falls in a numeric frame of its own. When the wall comes within the reach of the merge test, the test links
    /// the crate to the wall, an anchor, and a group with an anchor never falls: the group takes the wall's home, the system
    /// of ground, and the wall is a real body there (ter-dhkf). Before, the crate kept falling in its own frame and hit the
    /// wall's ghost.
    /// </summary>
    [Fact]
    public void AFastCrateNearAStaticWallJoinsTheWallsSystemAndStopsAtTheRealWall()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        (Entity wall, Entity crate) = CrateThatThrustsAtAWall(world, ground, [], []);
        Tick(world, OnsetTicks);
        InFallingFrame(world, crate).Should().BeTrue("the crate thrusts at 60 m/s, and the wall is beyond the reach of the merge test");

        TickUntil(world, () => world.PhysicsHomeOf(crate) == ground, 60);

        world.PhysicsHomeOf(crate).Should().Be(ground, "the group of the crate and the wall takes the wall's home");
        world.PoseIn(crate, ground).Position.X.Should().BeLessThan(-100, "the crate joins the wall's system about 2 s before it hits the wall");
        world.PhysicsHomeOf(wall).Should().Be(ground, "a static body stays in its own system");
        world.Frames.AnchorGhosts.IsEmpty.Should().BeTrue("the wall is a real body of the crate's system, so no frame keeps a ghost of it");

        Tick(world, 180);
        world.Errors.Should().BeEmpty();
        world.PhysicsHomeOf(crate).Should().Be(ground);
        world.PoseIn(crate, ground).Position.X.Should().BeLessThan(0, "the crate hit the wall and did not pass through");
    }

    /// <summary>
    /// The scene of <see cref="AFastCrateNearAStaticWallJoinsTheWallsSystemAndStopsAtTheRealWall"/>: a crate flies at
    /// 60 m/s with a gentle thrust at a static wall, falls in a numeric frame of its own while the wall is far, and joins the
    /// wall's system when the wall comes within the reach of the merge test. So the crate and the wall each hear one
    /// Collision when the crate hits the wall, and no end while the crate rests against it. The impulse is the crate's mass
    /// times its approach speed. Before ter-dhkf, the crate kept falling in its numeric frame and hit the wall's ghost
    /// there, with the same events: a contact with a ghost is a contact of the two entities.
    /// </summary>
    [Fact]
    public void AFastCrateAndTheStaticWallThatItHitsEachHearOneCollision()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var wallLog = new CollisionLog();
        var crateLog = new CollisionLog();
        (Entity wall, Entity crate) = CrateThatThrustsAtAWall(world, ground, [wallLog], [crateLog]);
        Tick(world, OnsetTicks);
        InFallingFrame(world, crate).Should().BeTrue("the crate thrusts at 60 m/s, and the wall is beyond the reach of the merge test");
        TickUntil(world, () => world.PhysicsHomeOf(crate) == ground, 60);
        world.PhysicsHomeOf(crate).Should().Be(ground, "the merge test links the crate to the wall");
        crateLog.Seen.Should().BeEmpty("the crate has not reached the wall yet");

        // The crate hits the wall about 2 s later, and then rests against it for about 3 s.
        Tick(world, 300);

        world.Errors.Should().BeEmpty();
        world.PoseIn(crate, ground).Position.X.Should().BeLessThan(0, "the crate hit the wall and did not pass through");
        crateLog.NewsOf(wall).Should().Equal([Started], "the crate rests against the wall");
        wallLog.NewsOf(crate).Should().Equal([Started]);
        crateLog.Seen.Single().Impulse.Should().BeApproximately(600f, 30f, "the crate of 10 kg approached the static wall at 60 m/s");
    }

    /// <summary>
    /// With no gravity, a crate rests against a static wall, and then every group falls
    /// (<see cref="NumericFrameTesting.EveryGroupFalls"/>). The merge test links the crate to the wall, and a group with a
    /// static body never falls, so the crate stays in the wall's system, and the contact goes on there (ter-dhkf).
    /// </summary>
    [Fact]
    public void ACrateThatRestsAgainstAStaticWallStaysInTheWallsSystemWhenEveryGroupFalls()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var crateLog = new CollisionLog();
        Entity wall = world.Spawn(new SpawnDesc { Frame = ground, Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 5, 5)), BodyMotion.Static) });
        Entity crate = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(-1.1), LinearVelocity = new Double3(0.2, 0, 0), Body = Crate, Components = [crateLog] });
        Tick(world, 60);
        crateLog.NewsOf(wall).Should().Equal([Started]);

        world.Frames.NumericOptions = EveryGroupFalls;
        Tick(world, 120);

        world.Errors.Should().BeEmpty();
        world.PhysicsHomeOf(crate).Should().Be(ground);
        world.Frames.NumericCount.Should().Be(0);
        crateLog.NewsOf(wall).Should().Equal([Started], "the crate still rests against the wall");
    }

    /// <summary>
    /// With no gravity, two crates touch, and then one drifts away at 1 cm/s. Jolt holds their pair while they are within
    /// its speculative contact distance of 2 cm, so their contact goes on for about 2 s. Meanwhile every slow group gets a
    /// resting frame (<see cref="NumericFrameTesting.EverySlowGroupRests"/>), and both crates move into one. In the new
    /// system, Jolt finds their pair a few millimeters apart, without a touch, which would wait for a touch as a new pair.
    /// The move hands it the contact instead, so the contact ends only when the crates are 2 cm apart, and not at the move.
    /// </summary>
    [Fact]
    public void TwoCratesThatDriftApartMoveIntoARestingFrameAndTheirContactEndsOnlyWhenTheySeparate()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var leftLog = new CollisionLog();
        var pilot = new Pilot();
        Entity left = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(-0.5), Body = Crate, Components = [leftLog, pilot] });
        Entity right = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0.5), Body = Crate });
        Tick(world, 10);
        leftLog.NewsOf(right).Should().Equal([Started]);

        // Half a second of drift puts the crates 5 mm apart, beyond the 1 mm of a touch.
        pilot.Once = body => body.SetVelocity(new Double3(-0.01, 0, 0));
        long drift = world.TickNumber;
        Tick(world, 30);
        world.Frames.NumericOptions = EverySlowGroupRests;
        TickUntil(world, () => world.PhysicsHomeOf(left) != ground, 300);
        FrameHandle home = world.PhysicsHomeOf(left);
        world.Frames.Numeric(home).Should().Be(NumericMode.Resting);
        world.PhysicsHomeOf(right).Should().Be(home, "the two crates move together");
        long moved = world.TickNumber;
        leftLog.NewsOf(right).Should().Equal([Started]);
        Tick(world, 180);

        world.Errors.Should().BeEmpty();
        leftLog.NewsOf(right).Should().Equal(Started, Ended);
        long ended = leftLog.Ended.Single().Tick;
        ended.Should().BeGreaterThan(moved + 5, "the move did not end the contact");
        (ended - drift).Should().BeInRange(110, 160, "the contact ends when the crates are about 2 cm apart, after about 2 s");
    }

    /// <summary>
    /// With no gravity and every group falling (<see cref="NumericFrameTesting.EveryGroupFalls"/>), a crate of 10 kg flies
    /// at 3 m/s at a static cube of triangles. The merge test links the crate to the cube, an anchor, and a group with a
    /// static body never falls, so the crate stays in the cube's system and meets the real cube (ter-dhkf). Each hears one
    /// Collision, whose impulse is the crate's mass times 3 m/s. Before, the crate fell in a frame of its own, and the ghost
    /// of the cube came at it at 3 m/s, with the same events.
    /// </summary>
    [Fact]
    public void ACrateThatFliesAtAStaticMeshJoinsItsSystemAndHearsTheImpulseOfItsApproach()
    {
        using World world = NewWorld(out _, configure: o => o.NumericFrames = EveryGroupFalls);
        FrameHandle ground = Ground(world, 0);
        var wallLog = new CollisionLog();
        Entity wall = world.Spawn(new SpawnDesc { Frame = ground, Body = new BodyDesc(MeshCube(0.5f), BodyMotion.Static), Components = [wallLog] });
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(-3), LinearVelocity = new Double3(3, 0, 0), Body = Crate, Components = [crateLog] });
        Tick(world, 2);
        world.PhysicsHomeOf(crate).Should().Be(ground, "the group with the static cube takes the cube's home");
        world.Frames.AnchorGhosts.IsEmpty.Should().BeTrue();

        Tick(world, 60);

        world.Errors.Should().BeEmpty();
        crateLog.NewsOf(wall).Should().Equal([Started]);
        wallLog.NewsOf(crate).Should().Equal([Started]);
        crateLog.Seen.Single().Impulse.Should().BeApproximately(30f, 3f, "the cube came at the crate of 10 kg at 3 m/s");
    }

    // The scenes of the shifts need ghosts in a falling frame. Since ter-dhkf, every anchor joins the group of the crates
    // near it, or that group lives in the anchor's home, so no anchor has a ghost in a falling frame. The ghost of a body
    // that sticks out of a bubble does: the numeric update names the falling frame of the crates near the bubble to keep
    // it (see StickingWall). Every wall lies about 1 km along +X in space.

    [Fact]
    public void AShiftOfAFallingFrameMovesTheGhostsInItWithoutWakingTheBodiesNearThem()
    {
        // Every group falls, and a frame moves its origin when its members' center is 1 m from it. A sleeping crate floats
        // 4 cm from the ghost of a still wall, inside the margin in which a removed ghost wakes bodies, and beyond the
        // distance at which Jolt makes a contact. A second crate in its frame puts the members' center 4 m away.
        using World world = NewWorld(out JoltPhysicsBackend backend,
            configure: o => o.NumericFrames = EveryGroupFalls with { RecenterDistance = 1 });
        FrameHandle ground = Space(world);
        Entity wall = StickingWall(world, ground, 1000, cratesSide: 1);
        Entity sleeper = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1001.04), Body = Crate });
        Entity other = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1009), Body = Crate });
        Tick(world, 1);
        FrameHandle home = world.PhysicsHomeOf(sleeper);
        world.PhysicsHomeOf(other).Should().Be(home);
        backend.HasGhostIn(wall, home).Should().BeTrue();
        TickUntil(world, () => !IsAwake(backend, home, sleeper), 600);
        IsAwake(backend, home, sleeper).Should().BeFalse("the crate came to rest next to the wall's ghost");

        world.Destroy(other);
        Tick(world, 1);

        world.Errors.Should().BeEmpty();
        world.PhysicsHomeOf(sleeper).Should().Be(home);
        world.Frames.HomeOffset(home, ground).X.Should().BeApproximately(1001.04, 1e-6, "the frame's origin moved to the crate");
        IsAwake(backend, home, sleeper).Should().BeFalse("the ghost moved without waking the crate next to it");
        backend.HasGhostIn(wall, home).Should().BeTrue();
    }

    [Fact]
    public void AShiftOfAFallingFrameMovesEveryGhostInItAndLeavesTheGhostsOfOtherFramesWhereTheyAre()
    {
        // Every group falls, and a frame moves its origin when its members' center is 1 m from it. In one falling frame, two
        // sleeping crates float 4 cm from the ghosts of two still walls, and a third crate above them puts the members'
        // center 2.7 m above the center of the two. Far away, a second falling frame holds a crate that sleeps 4 cm from the
        // ghost of a third wall. The ghost pass puts back a ghost that is not where its wall is relative to the frame, and so
        // wakes the crate next to it.
        using World world = NewWorld(out JoltPhysicsBackend backend,
            configure: o => o.NumericFrames = EveryGroupFalls with { RecenterDistance = 1 });
        FrameHandle ground = Space(world);
        StickingWall(world, ground, 1000, cratesSide: 1);
        StickingWall(world, ground, 1010, cratesSide: -1);
        Entity farWall = StickingWall(world, ground, 1200, cratesSide: 1);
        Entity left = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1001.04), Body = Crate });
        Entity right = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1008.96), Body = Crate });
        Entity above = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1005, 8), Body = Crate });
        Entity far = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1201.04), Body = Crate });
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1209), Body = Crate });
        Tick(world, 1);
        FrameHandle home = world.PhysicsHomeOf(left);
        FrameHandle farHome = world.PhysicsHomeOf(far);
        world.PhysicsHomeOf(right).Should().Be(home);
        farHome.Should().NotBe(home);
        GhostsIn(backend, home).Should().HaveCount(2, "the frame keeps a ghost of each wall next to its crates");
        TickUntil(world, () => !IsAwake(backend, home, left) && !IsAwake(backend, home, right) && !IsAwake(backend, farHome, far), 600);
        IsAwake(backend, farHome, far).Should().BeFalse("the far crate came to rest next to its wall's ghost");
        Double3 farOrigin = world.Frames.HomeOffset(farHome, ground);
        Double3 farGhost = GhostPosition(backend, farHome, farWall);

        world.Destroy(above);
        Tick(world, 1);

        world.Errors.Should().BeEmpty();
        Double3 origin = world.Frames.HomeOffset(home, ground);
        origin.X.Should().BeApproximately(1005, 1e-6);
        origin.Y.Should().BeApproximately(0, 1e-6, "the frame's origin moved down to its two crates");
        IsAwake(backend, home, left).Should().BeFalse("the ghost of the left wall moved with the frame");
        IsAwake(backend, home, right).Should().BeFalse("the ghost of the right wall moved with the frame");
        world.Frames.HomeOffset(farHome, ground).Should().Be(farOrigin, "only the near frame shifted");
        IsAwake(backend, farHome, far).Should().BeFalse("the ghost in the far frame did not move");
        (GhostPosition(backend, farHome, farWall) - farGhost).Length.Should().BeLessThan(1e-6, "the far wall holds still, and its ghost with it");
        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
    }

    [Fact]
    public void AShiftOfAFallingFrameDoesNotTouchAGhostThatWentAwayBeforeIt()
    {
        // As above, without the far frame. In the tick that shifts the frame, the right wall goes away, and its ghost with it.
        using World world = NewWorld(out JoltPhysicsBackend backend,
            configure: o => o.NumericFrames = EveryGroupFalls with { RecenterDistance = 1 });
        FrameHandle ground = Space(world);
        Entity leftWall = StickingWall(world, ground, 1000, cratesSide: 1);
        Entity rightWall = StickingWall(world, ground, 1010, cratesSide: -1);
        Entity left = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1001.04), Body = Crate });
        Entity right = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1008.96), Body = Crate });
        Entity above = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1005, 8), Body = Crate });
        Tick(world, 1);
        FrameHandle home = world.PhysicsHomeOf(left);
        backend.HasGhostIn(rightWall, home).Should().BeTrue();
        TickUntil(world, () => !IsAwake(backend, home, left) && !IsAwake(backend, home, right), 600);
        IsAwake(backend, home, left).Should().BeFalse("the left crate came to rest next to its wall's ghost");

        world.Destroy(rightWall);
        world.Destroy(above);
        Tick(world, 1);

        world.Errors.Should().BeEmpty();
        world.Frames.HomeOffset(home, ground).Y.Should().BeApproximately(0, 1e-6, "the frame shifted");
        List<JoltPhysicsBackend.BodyFacts> ghosts = GhostsIn(backend, home);
        ghosts.Should().ContainSingle("the ghost of the right wall went with the wall");
        BodyUserData.Names(ghosts[0].UserData, leftWall).Should().BeTrue("the ghost of the left wall stays");
        IsAwake(backend, home, left).Should().BeFalse("the ghost of the left wall moved with the frame");
        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>Space without gravity.</summary>
    private static FrameHandle Space(World world) => world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));

    /// <summary>
    /// A still wall 1 m thick and 10 m square, across the X axis at <paramref name="x"/> in <paramref name="space"/>: a
    /// kinematic body at rest in a station, a frame with the Static driver, no hull and a bubble of radius 2 m, on the edge
    /// of the bubble, so that it sticks out. The station lies on the side of the wall away from the crates, whose side is
    /// <paramref name="cratesSide"/>: +1 for +X, -1 for -X. A body that sticks out of a bubble has a ghost in the bubble's
    /// tree parent, space, and in each falling frame of space whose members may touch it, which the numeric update names.
    /// </summary>
    private static Entity StickingWall(World world, FrameHandle space, double x, int cratesSide)
    {
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(x - 2 * cratesSide), AsFrame = new FrameSpec(FrameBounds.Sphere(2), MotionDriver.Static),
        }));
        return world.Spawn(new SpawnDesc
        {
            Frame = station, Pose = At(2 * cratesSide), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 5, 5)), BodyMotion.Kinematic),
        });
    }

    /// <summary>Every ghost in the system of <paramref name="frame"/>, as Jolt holds it.</summary>
    private static List<JoltPhysicsBackend.BodyFacts> GhostsIn(JoltPhysicsBackend backend, FrameHandle frame) =>
        backend.BodiesIn(frame).Where(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost).ToList();

    /// <summary>Where Jolt has the ghost of <paramref name="entity"/> in the system of <paramref name="frame"/>.</summary>
    private static Double3 GhostPosition(JoltPhysicsBackend backend, FrameHandle frame, Entity entity) =>
        GhostsIn(backend, frame).Single(facts => BodyUserData.Names(facts.UserData, entity)).Position;
}

/// <summary>Records every FrameChanged that its entity hears.</summary>
internal sealed class FrameChangedLog : IComponent
{
    public List<FrameChanged> Seen { get; } = new();

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<FrameChanged>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].Is<FrameChanged>())
                Seen.Add(events[i].As<FrameChanged>());
    }
}
