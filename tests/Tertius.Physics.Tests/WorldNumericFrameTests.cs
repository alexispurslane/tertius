using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Numeric frames (docs/plans/numeric-frames.md, steps 2 and 3): frames that the engine makes around a group of bodies,
/// which fall freely in doubles or rest in their parent, with a Jolt system each. The tests make and move them through the
/// World's internal operations, which the clustering of a later step calls.
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

    private static FrameHandle Earth(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu)));

    [Fact]
    public void ACrateInAFallingNumericFrameFollowsTheOrbitThatItsStartingStateGivesInEarth()
    {
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle earth = Earth(world);
        // The crate starts 100 m above the frame's origin and 30 m ahead, and drifts away from it, so tidal gravity acts.
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = At(OrbitRadius + 100, 0, 30),
            LinearVelocity = new Double3(0.1, 0, OrbitSpeed + 0.05),
            Body = Crate,
        });
        FrameHandle numeric = world.CreateNumericFrame(earth, new Double3(OrbitRadius, 0, 0), new Double3(0, 0, OrbitSpeed), NumericMode.Falling);
        world.MoveBodies([crate], numeric);

        // The orbit of the starting state, stepped in doubles with the scheme that Jolt uses for every body.
        Double3 position = world.PoseIn(crate, earth).Position;
        Double3 velocity = world.VelocityIn(crate, earth);
        double dt = world.DeltaTime;
        const int ticks = 120 * 60;
        for (int i = 0; i < ticks; i++)
        {
            velocity += GravityField.Point(EarthMu).AtPrecise(position) * dt;
            position += velocity * dt;
        }

        Tick(world, ticks);

        world.Errors.Should().BeEmpty();
        Double3 reached = world.PoseIn(crate, earth).Position;
        (reached - position).Length.Should().BeLessThan(1e-3, $"the crate reached {reached}, and the orbit {position}");
        (reached - world.Frames.PointToParent(numeric, Double3.Zero)).Length.Should().BeGreaterThan(100, "the crate stayed away from the frame's origin");
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
        FrameHandle numeric = world.CreateNumericFrame(earth, new Double3(0, OrbitRadius, 0), new Double3(OrbitSpeed, 0, 0), NumericMode.Falling);
        world.MoveBodies([crate], numeric);

        pilot.Once = body => body.SetVelocity(new Double3(7672.123456, 0, 0));
        Tick(world, 1);
        // The velocity applies in the next step, which also adds the step's gravity: Earth's field at the crate, which
        // has moved 128 m along X, so the field has a part along X of 164 µm/s².
        Double3 field = GravityField.Point(EarthMu).AtPrecise(world.PoseIn(crate, earth).Position);
        Tick(world, 1);

        // A float of 7,672 m/s steps by 488 µm/s.
        world.Errors.Should().BeEmpty();
        world.VelocityIn(crate, earth).X.Should().BeApproximately(7672.123456 + field.X * world.DeltaTime, 1e-6);
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

        FrameHandle numeric = world.CreateNumericFrame(ground, new Double3(3, 0, -2), Double3.Zero, NumericMode.Resting);
        world.MoveBodies([floor, lower, upper], numeric);
        Tick(world, 60);

        world.Errors.Should().BeEmpty();
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
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(2), Body = Cube() });
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(-20),
            Body = Cube(100) with { InteriorShape = new SphereShapeDesc(0.05f) },
            AsFrame = new FrameSpec(FrameBounds.Sphere(5), MotionDriver.Physics),
        });
        FrameHandle ship = world.Frames.TryGetFrame(owner, out FrameHandle own) ? own : FrameHandle.Null;

        FrameHandle numeric = world.CreateNumericFrame(ground, new Double3(5, 0, 0), Double3.Zero, NumericMode.Resting);
        world.MoveBodies([crate, owner], numeric);
        Tick(world, 5);

        world.Errors.Should().BeEmpty();
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
    }

    [Fact]
    public void APersonWhoseHomeIsAFallingNumericFrameDoesNotStandOnAFloorThatFallsWithIt()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        Entity floor = Floor(world, ground);
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.92), Body = Person });
        Tick(world, 30);
        world.Snapshot.IsStanding(person).Should().BeTrue("the person stands on the floor in the frame ground");

        // As outside a ship in orbit: the person, and what it touches, fall freely in a numeric frame.
        FrameHandle numeric = world.CreateNumericFrame(ground, Double3.Zero, Double3.Zero, NumericMode.Falling);
        world.MoveBodies([floor, person], numeric);
        Tick(world, 30);

        world.Errors.Should().BeEmpty();
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
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5)), Mass: 1e4f, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(0.05f)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Physics),
        });
        world.Frames.TryGetFrame(owner, out FrameHandle ship).Should().BeTrue();
        Entity cargo = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(1, 2, 3), Body = Crate });

        FrameHandle numeric = world.CreateNumericFrame(earth, new Double3(OrbitRadius, 0, 0), new Double3(0, 0, OrbitSpeed), NumericMode.Falling);
        world.MoveBodies([owner], numeric);
        DPose start = world.PoseIn(owner, earth);
        Tick(world, 120);

        world.Errors.Should().BeEmpty();
        world.Frames.Parent(ship).Should().Be(earth);
        world.GetFrame(cargo).Should().Be(ship);
        (world.PoseIn(cargo, ship).Position - new Double3(1, 2, 3)).Length.Should().BeLessThan(1e-3, "the cargo floats where it was in the ship");
        DPose shipPose = world.PoseIn(owner, earth);
        (shipPose.Position - start.Position).Length.Should().BeGreaterThan(15_000, "the ship moved along its orbit for 2 s");
        (world.PoseIn(cargo, earth).Position - shipPose.TransformPoint(new Double3(1, 2, 3))).Length.Should().BeLessThan(1e-3,
            "the cargo moved with the ship");
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
