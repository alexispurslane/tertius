using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Numeric frames (docs/plans/numeric-frames.md): frames that the engine makes around a group of bodies, which fall freely
/// in doubles or rest in their parent, with a Jolt system each. Only the numeric update at each sync point makes and moves
/// them, so a test that needs one tunes the membership rules (<see cref="NumericFrameOptions"/>) to make it.
/// </summary>
public class WorldNumericFrameTests
{
    /// <summary>Earth's G·M, in m³/s², as in the demo scenes.</summary>
    private const double EarthMu = 3.986004418e14;

    /// <summary>The radius of the ship's orbit in the demo scenes, 400 km above Earth's mean radius, in meters.</summary>
    private const double OrbitRadius = 6.371e6 + 400e3;

    private static readonly double OrbitSpeed = Math.Sqrt(EarthMu / OrbitRadius);

    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);

    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    private static readonly BodyDesc Hull = new(new BoxShapeDesc(new Vector3(5)), Mass: 1e4f, LinearDamping: 0, AngularDamping: 0,
        InteriorShape: new SphereShapeDesc(0.05f));

    /// <summary>Membership rules that give every slow group a resting frame and never take a frame away for being near.</summary>
    private static readonly NumericFrameOptions RestEverywhere = new() { CreateDistance = 0, LeaveDistance = 0 };

    /// <summary>Membership rules that give every group a falling frame and never take a frame away for being slow.</summary>
    private static readonly NumericFrameOptions FallEverywhere = new() { CreateSpeed = 0, LeaveSpeed = 0 };

    private static FrameHandle Earth(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu)));

    private static bool InFallingFrame(World world, Entity entity) =>
        world.Frames.Numeric(world.PhysicsHomeOf(entity)) == NumericMode.Falling;

    [Fact]
    public void ACrateInAFallingNumericFrameFollowsTheOrbitThatItsSpawnGivesInEarth()
    {
        // The lookahead links the crate to a heavy block 100 m away, so their frame's origin stays near the block and the
        // crate drifts away from it, where tidal gravity acts.
        using World world = NewWorld(out _, debugChecks: false, numericFrames: new NumericFrameOptions { LookaheadTime = 1e4 });
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
        Double3 position = start, velocity = startVelocity;
        double dt = world.DeltaTime;
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
        world.VelocityIn(crate, earth).X.Should().BeApproximately(7672.123456 + field.X * world.DeltaTime, 1e-6);
    }

    [Fact]
    public void AGroupThatStartsToFallAtOrbitalSpeedKeepsTheVelocityOfEachBodyToAMicrometerPerSecond()
    {
        // Two crates 20 km out rest in one numeric frame, then speed up to orbital speed. The frame takes over their
        // mass-weighted mean velocity, which no float holds, and each body loses it.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var lighter = new Pilot();
        var heavier = new Pilot();
        Entity a = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(20_000), Body = Crate, Components = [lighter] });
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(20_000, 0, 1.5), Body = Crate with { Mass = 20 }, Components = [heavier] });
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
        world.Frames.NumericOptions = RestEverywhere;
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
        lowerFrames.Seen.Should().BeEmpty();
        upperFrames.Seen.Should().BeEmpty();
    }

    [Fact]
    public void TheFramesThatGameplaySeesAreOnlySemanticFramesWhileANumericFrameExists()
    {
        using World world = NewWorld(out _, numericFrames: RestEverywhere);
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
        world.Frames.NumericOptions = FallEverywhere;
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

    [Fact]
    public void AFastCrateNearAStaticWallFallsInItsOwnFrameAndHitsTheWallsGhost()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 0);
        Entity wall = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 5, 5)), BodyMotion.Static),
        });
        Entity crate = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(-10), LinearVelocity = new Double3(60, 0, 0), Body = Crate });

        Tick(world, 1);

        InFallingFrame(world, crate).Should().BeTrue("a static body never stops a group that falls");
        world.PhysicsHomeOf(wall).Should().Be(ground, "a static body stays in its own system");
        backend.HasGhostIn(wall, world.PhysicsHomeOf(crate)).Should().BeTrue("the falling frame keeps a ghost of the wall");

        Tick(world, 60);
        world.Errors.Should().BeEmpty();
        world.PoseIn(crate, ground).Position.X.Should().BeLessThan(0, "the crate hit the wall's ghost and did not pass through");
    }

    [Fact]
    public void AShiftOfAFallingFrameMovesTheGhostsInItWithoutWakingTheBodiesNearThem()
    {
        // Every group falls, and a frame moves its origin when its members' center is 1 m from it. A sleeping crate floats
        // 4 cm from the ghost of a static wall, inside the margin in which a removed ghost wakes bodies, and beyond the
        // distance at which Jolt makes a contact. A second crate in its frame puts the members' center 4 m away.
        using World world = NewWorld(out JoltPhysicsBackend backend,
            numericFrames: new NumericFrameOptions { CreateSpeed = 0, LeaveSpeed = 0, RecenterDistance = 1 });
        FrameHandle ground = Ground(world, 0);
        Entity wall = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 5, 5)), BodyMotion.Static),
        });
        Entity sleeper = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1.04), Body = Crate });
        Entity other = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(9), Body = Crate });
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
        world.Frames.HomeOffset(home, ground).X.Should().BeApproximately(1.04, 1e-6, "the frame's origin moved to the crate");
        IsAwake(backend, home, sleeper).Should().BeFalse("the ghost moved without waking the crate next to it");
        backend.HasGhostIn(wall, home).Should().BeTrue();
    }
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
