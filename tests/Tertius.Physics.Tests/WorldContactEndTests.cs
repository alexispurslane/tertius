using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A contact starts at a real touch, the first step with a solver impulse or zero separation, and not when Jolt finds the
/// pair at its speculative contact distance. It ends when the two bodies stop touching, or when one of them is destroyed
/// or leaves for a system where the other entity has no body. Each entity of a contact hears one Collision at the start
/// and one CollisionEnded at the end. Sleep, wake, a growth of the system and moves between numeric frames end nothing.
/// </summary>
public class WorldContactEndTests
{
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    /// <summary>A heavy box, which a character cannot push: friction holds 0.5 · 500 kg · g, and a character pushes with 100 N.</summary>
    private static readonly BodyDesc HeavyBox = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 500);

    /// <summary>The floor of <see cref="Floor"/>, with <paramref name="log"/> on it.</summary>
    private static Entity LoggedFloor(World world, FrameHandle frame, CollisionLog log) => world.Spawn(new SpawnDesc
    {
        Frame = frame,
        Pose = At(0, -0.5),
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
        Components = [log],
    });

    /// <summary>A kinematic block high above the far end of the floor, which touches nothing and keeps the system stepping.</summary>
    private static void KeepStepping(World world, FrameHandle frame) => world.Spawn(KinematicDrift.Drifting(new SpawnDesc
    {
        Frame = frame,
        Pose = At(-40, 20),
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Kinematic),
    }, world, new Double3(0, 0, 0.1)));

    /// <summary>A frame with no body and a bubble of radius 5 m at <paramref name="x"/> on the X axis of <paramref name="parent"/>.</summary>
    private static FrameHandle Bubble(World world, FrameHandle parent, double x) => world.GetOwnedFrame(world.Spawn(new SpawnDesc
    {
        Frame = parent,
        Pose = At(x),
        AsFrame = new FrameSpec(FrameBounds.Sphere(5)),
    }));

    [Fact]
    public void ABoxThatJumpsOffTheFloorAndTheFloorEachHearTheEndAndTheNextLanding()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        var floorLog = new CollisionLog();
        Entity floor = LoggedFloor(world, ground, floorLog);
        var boxLog = new CollisionLog();
        var pilot = new Pilot();
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = Cube(), Components = [boxLog, pilot] });
        Tick(world, 30);
        boxLog.NewsOf(floor).Should().Equal([Started], "the box rests on the floor");

        pilot.Once = body => body.SetVelocity(new Double3(0, 3, 0));
        Tick(world, 10);

        boxLog.NewsOf(floor).Should().Equal(Started, Ended);
        floorLog.NewsOf(box).Should().Equal(Started, Ended);

        Tick(world, 60);

        boxLog.NewsOf(floor).Should().Equal(Started, Ended, Started);
        floorLog.NewsOf(box).Should().Equal(Started, Ended, Started);
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABoxThatSleepsOnTheFloorAndWakesThereHearsNoEnd(bool systemKeepsStepping)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        var floorLog = new CollisionLog();
        Entity floor = LoggedFloor(world, ground, floorLog);
        if (systemKeepsStepping)
            KeepStepping(world, ground);
        var boxLog = new CollisionLog();
        var pilot = new Pilot();
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = Cube(), Components = [boxLog, pilot] });
        Tick(world, 1);
        TickUntil(world, () => !IsAwake(backend, ground, box), 600);
        IsAwake(backend, ground, box).Should().BeFalse("the box rests on the floor, so it falls asleep");
        Tick(world, 30);

        pilot.Once = body => body.AddImpulse(new Vector3(0.5f, 0, 0));
        Tick(world, 5);
        IsAwake(backend, ground, box).Should().BeTrue("the push woke the box");
        Tick(world, 30);

        boxLog.NewsOf(floor).Should().Equal([Started], "the box never left the floor");
        floorLog.NewsOf(box).Should().Equal([Started]);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A box at rest 1 cm above a floor, with no gravity. Jolt finds the pair, because 1 cm is inside its speculative
    /// contact distance of 2 cm, but the two never touch.
    /// </summary>
    [Fact]
    public void ABoxThatRestsOneCentimeterFromTheFloorHearsNoCollision()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        var floorLog = new CollisionLog();
        LoggedFloor(world, space, floorLog);
        var boxLog = new CollisionLog();
        Entity box = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, 0.51), Body = Cube(), Components = [boxLog] });

        Tick(world, 60);

        boxLog.Seen.Should().BeEmpty("a gap of 1 cm is no touch");
        floorLog.Seen.Should().BeEmpty();
        boxLog.Ended.Should().BeEmpty("a contact that never started does not end");
        world.PoseInOwnFrame(box).Position.Y.Should().BeApproximately(0.51, 1e-6, "nothing pushed the box");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A box 1 cm above a floor, with no gravity, flies off. The two never touched, so neither hears an end.</summary>
    [Fact]
    public void ABoxThatLeavesAFloorThatItNeverTouchedHearsNoEnd()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        var floorLog = new CollisionLog();
        LoggedFloor(world, space, floorLog);
        var boxLog = new CollisionLog();
        var pilot = new Pilot();
        Entity box = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, 0.51), Body = Cube(), Components = [boxLog, pilot] });
        Tick(world, 10);

        pilot.Once = body => body.SetVelocity(new Double3(0, 1, 0));
        Tick(world, 10);

        world.PoseInOwnFrame(box).Position.Y.Should().BeGreaterThan(0.6, "the box flew off");
        boxLog.Ended.Should().BeEmpty();
        floorLog.Ended.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>Two boxes 1 cm apart, with no gravity. One is destroyed. They never touched, so the other hears nothing.</summary>
    [Fact]
    public void DestroyingABoxEndsNothingWithANeighborThatItNeverTouched()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        Entity doomed = world.Spawn(new SpawnDesc { Frame = space, Body = Cube() });
        var log = new CollisionLog();
        world.Spawn(new SpawnDesc { Frame = space, Pose = At(1.01), Body = Cube(), Components = [log] });
        Tick(world, 10);

        world.Destroy(doomed);
        Tick(world, 3);

        log.Seen.Should().BeEmpty();
        log.Ended.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ball rests 1 cm from a wall, with no gravity, while its system grows. Then it is pushed into the wall. The two
    /// touch only then, so the ball hears the wall once, after the growth.
    /// </summary>
    [Fact]
    public void ABallNearAWallThroughAGrowthHearsTheWallWhenItTouches()
    {
        var warnings = new List<string>();
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { MaxBodiesPerFrame = 8, WorkerThreads = 2, Log = warnings.Add });
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, backend);
        FrameHandle space = Ground(world, 0);
        Entity wall = world.Spawn(new SpawnDesc { Frame = space, Pose = At(1.01), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        var log = new CollisionLog();
        var pilot = new Pilot();
        world.Spawn(new SpawnDesc { Frame = space, Body = Ball(), Components = [log, pilot] });
        Tick(world, 10);
        for (int i = 0; i < 15; i++)
            world.Spawn(new SpawnDesc { Frame = space, Pose = At(3 * i, -100), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        Tick(world, 10);
        warnings.Count(warning => warning.Contains("so it grew")).Should().Be(2);
        log.Seen.Should().BeEmpty("the ball has not touched the wall yet");

        pilot.Once = body => body.SetVelocity(new Double3(0.5, 0, 0));
        Tick(world, 10);

        log.Seen.Should().ContainSingle().Which.Other.Should().Be(wall);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, a ball of 1 kg moves at 1 m/s, 1.67 cm in each step, toward a wall 5.5 cm away. Jolt finds the
    /// pair when the gap is 5 mm, and the solver stops the ball in that step. So the contact starts in that step, with the
    /// impulse that stops the ball, and not one step later, when the ball is at rest.
    /// </summary>
    [Fact]
    public void ABallThatHitsAWallAtOneMeterPerSecondHearsTheImpulseThatStopsIt()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        world.Spawn(new SpawnDesc { Frame = space, Pose = At(1.055), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        var log = new CollisionLog();
        world.Spawn(new SpawnDesc { Frame = space, LinearVelocity = new Vector3(1, 0, 0), Body = Ball(), Components = [log] });

        Tick(world, 10);

        log.Seen.Should().ContainSingle().Which.Impulse.Should().BeApproximately(1, 0.1f);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ball of the test above slides 1 cm over a plate toward the wall, and the plate and the wall are two parts of
    /// one static body. Jolt finds the pair of the ball and the plate at once, without a touch. The touch of the wall,
    /// which Jolt adds as a second part of the same pair, starts the contact.
    /// </summary>
    [Fact]
    public void ABallThatPassesOnePartOfABodyAndHitsAnotherHearsTheImpulseThatStopsIt()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        var plateAndWall = new CompoundShapeDesc([
            new CompoundChild(new BoxShapeDesc(new Vector3(3, 0.1f, 3)), new Pose(new Vector3(0, -0.61f, 0), Quaternion.Identity)),
            new CompoundChild(new BoxShapeDesc(new Vector3(0.5f, 1, 1)), new Pose(new Vector3(1.055f, 0, 0), Quaternion.Identity)),
        ]);
        Entity body = world.Spawn(new SpawnDesc { Frame = space, Body = new BodyDesc(plateAndWall, BodyMotion.Static) });
        var log = new CollisionLog();
        world.Spawn(new SpawnDesc { Frame = space, LinearVelocity = new Vector3(1, 0, 0), Body = Ball(), Components = [log] });

        Tick(world, 10);

        (_, Entity other, float impulse) = log.Seen.Should().ContainSingle().Subject;
        other.Should().Be(body);
        impulse.Should().BeApproximately(1, 0.1f);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A box lands on a floor, and while the box is still awake, both move into a numeric frame. Jolt finds their contact
    /// again in the first step there, and it goes on.
    /// </summary>
    [Fact]
    public void AnAwakeBoxAndItsFloorThatMoveIntoANumericFrameHearNoNewContactAndNoEnd()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        var floorLog = new CollisionLog();
        Entity floor = LoggedFloor(world, ground, floorLog);
        var boxLog = new CollisionLog();
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = Cube(), Components = [boxLog] });
        Tick(world, 3);
        boxLog.NewsOf(floor).Should().Equal(Started);

        world.Frames.NumericOptions = EverySlowGroupRests;
        TickUntil(world, () => world.Frames.IsNumeric(world.PhysicsHomeOf(box)), 30);
        FrameHandle numeric = world.PhysicsHomeOf(box);
        world.Frames.IsNumeric(numeric).Should().BeTrue();
        world.PhysicsHomeOf(floor).Should().Be(numeric);
        IsAwake(backend, numeric, box).Should().BeTrue("the box moved while awake");
        Tick(world, 10);

        boxLog.NewsOf(floor).Should().Equal(Started);
        floorLog.NewsOf(box).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The case of the deep-yard scene. A ball drifts at 0.1 m/s toward a wall 50.8 mm away, so it touches the wall in the
    /// step of tick 30, the 31st step, and hears it in tick 31. Jolt finds the pair at 2 cm, about 12 ticks earlier.
    /// </summary>
    [Fact]
    public void ABallThatDriftsSlowlyIntoAWallHearsItWhenItTouches()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        Entity wall = world.Spawn(new SpawnDesc { Frame = space, Pose = At(1.0508), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        var log = new CollisionLog();
        world.Spawn(new SpawnDesc { Frame = space, LinearVelocity = new Vector3(0.1f, 0, 0), Body = Ball(), Components = [log] });

        Tick(world, 60);

        (long tick, Entity other, _) = log.Seen.Should().ContainSingle().Subject;
        other.Should().Be(wall);
        tick.Should().BeInRange(30, 32);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A heavy box on the floor, a light box on it, and a character that walked against it. The heavy box is destroyed. Its
    /// three contacts end, and each of the others hears it. The destroyed box hears nothing, because it no longer exists.
    /// </summary>
    [Fact]
    public void DestroyingABoxEndsItsContactsWithTheFloorTheBoxOnItAndTheCharacterAgainstIt()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        var floorLog = new CollisionLog();
        LoggedFloor(world, ground, floorLog);
        Entity lower = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1, 0.5), Body = HeavyBox });
        var upperLog = new CollisionLog();
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1, 1.5), Body = Cube(), Components = [upperLog] });
        var pilot = new Pilot();
        var personLog = new CollisionLog();
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.95), Body = Person, Components = [pilot, personLog] });
        pilot.EveryTick = body => body.MoveCharacter(new Vector3(0.5f, 0, 0));
        Tick(world, 120);
        pilot.EveryTick = body => body.MoveCharacter(Vector3.Zero);
        Tick(world, 5);
        floorLog.NewsOf(lower).Should().Equal(Started);
        upperLog.NewsOf(lower).Should().Equal(Started);
        personLog.NewsOf(lower).Should().Equal([Started], "the character walked against the box");

        world.Destroy(lower);
        Tick(world, 3);

        floorLog.NewsOf(lower).Should().Equal(Started, Ended);
        upperLog.NewsOf(lower).Should().Equal(Started, Ended);
        personLog.NewsOf(lower).Should().Equal(Started, Ended);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void DestroyingACharacterEndsItsContactsWithTheFloorAndTheBoxItLeanedOn()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        var floorLog = new CollisionLog();
        LoggedFloor(world, ground, floorLog);
        var boxLog = new CollisionLog();
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1, 0.5), Body = HeavyBox, Components = [boxLog] });
        var pilot = new Pilot();
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.95), Body = Person, Components = [pilot] });
        pilot.EveryTick = body => body.MoveCharacter(new Vector3(0.5f, 0, 0));
        Tick(world, 120);
        floorLog.NewsOf(person).Should().Equal([Started], "the character stands on the floor");
        boxLog.NewsOf(person).Should().Equal([Started], "the character walked against the box");

        world.Destroy(person);
        Tick(world, 3);

        floorLog.NewsOf(person).Should().Equal(Started, Ended);
        boxLog.NewsOf(person).Should().Equal(Started, Ended);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A character walks against a heavy box and stands there until the box falls asleep, then walks 3 m away. Its own
    /// contact list no longer has the box, so the contact ends. The character's contact with the floor goes on the whole
    /// time, although Jolt never finds a contact of the character's inner body with the static floor.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACharacterThatWalksAwayFromASleepingBoxAndTheBoxEachHearTheEnd(bool systemKeepsStepping)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        var floorLog = new CollisionLog();
        Entity floor = LoggedFloor(world, ground, floorLog);
        if (systemKeepsStepping)
            KeepStepping(world, ground);
        var boxLog = new CollisionLog();
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1, 0.5), Body = HeavyBox, Components = [boxLog] });
        var pilot = new Pilot();
        var personLog = new CollisionLog();
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.95), Body = Person, Components = [pilot, personLog] });

        pilot.EveryTick = body => body.MoveCharacter(new Vector3(0.5f, 0, 0));
        Tick(world, 120);
        pilot.EveryTick = body => body.MoveCharacter(Vector3.Zero);
        Tick(world, 60);
        IsAwake(backend, ground, box).Should().BeFalse("the box fell asleep while the character stood against it");
        personLog.NewsOf(box).Should().Equal(Started);

        pilot.EveryTick = body => body.MoveCharacter(new Vector3(-1.5f, 0, 0));
        Tick(world, 120);

        world.PoseInOwnFrame(person).Position.X.Should().BeLessThan(-2, "the character walked 3 m away");
        personLog.NewsOf(box).Should().Equal(Started, Ended);
        boxLog.NewsOf(person).Should().Equal(Started, Ended);
        personLog.NewsOf(floor).Should().Equal([Started], "the character stood on the floor the whole time");
        floorLog.NewsOf(person).Should().Equal([Started]);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A block that owns a frame, with no gravity, whose interior has the block's own shape, and a crate at rest on the interior.</summary>
    private static (Entity Owner, Entity Crate) CrateOnInterior(World world, CollisionLog ownerLog, params IComponent[] crateComponents)
    {
        FrameHandle space = Ground(world, 0);
        var block = new BoxShapeDesc(new Vector3(1));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(block, Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: block),
            AsFrame = new FrameSpec(FrameBounds.Sphere(3)),
            Components = [ownerLog],
        });
        Entity crate = world.Spawn(new SpawnDesc { Frame = world.GetOwnedFrame(owner), Pose = At(0, 1.5), Body = Cube(), Components = crateComponents });
        return (owner, crate);
    }

    /// <summary>A contact with the interior of a frame names the frame's owner, and it ends when the crate leaves the interior.</summary>
    [Fact]
    public void ACrateThatLeavesTheInteriorOfAFrameAndTheFrameOwnerEachHearTheEnd()
    {
        using World world = NewWorld(out _);
        var ownerLog = new CollisionLog();
        var crateLog = new CollisionLog();
        var pilot = new Pilot();
        (Entity owner, Entity crate) = CrateOnInterior(world, ownerLog, crateLog, pilot);
        Tick(world, 30);
        crateLog.NewsOf(owner).Should().Equal(Started);
        ownerLog.NewsOf(crate).Should().Equal(Started);

        pilot.Once = body => body.SetVelocity(new Double3(0, 1, 0));
        Tick(world, 10);

        crateLog.NewsOf(owner).Should().Equal(Started, Ended);
        ownerLog.NewsOf(crate).Should().Equal(Started, Ended);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>Destroying the owner of a frame removes its interior, which ends the interior's contact with the crate on it.</summary>
    [Fact]
    public void DestroyingAFrameOwnerEndsTheContactOfItsInteriorWithTheCrateOnIt()
    {
        using World world = NewWorld(out _);
        var crateLog = new CollisionLog();
        (Entity owner, Entity crate) = CrateOnInterior(world, new CollisionLog(), crateLog);
        Tick(world, 30);
        crateLog.NewsOf(owner).Should().Equal(Started);

        world.Destroy(owner);
        Tick(world, 3);

        world.IsAlive(crate).Should().BeTrue("the crate outlives the frame that it was in");
        crateLog.NewsOf(owner).Should().Equal(Started, Ended);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, two crates side by side touch and drift together into a frame's bubble. Both move into the frame in
    /// the same step, and their contact goes on there.
    /// </summary>
    [Fact]
    public void TwoTouchingCratesThatEnterAFrameInOneStepKeepTheirContact()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        FrameHandle inside = Bubble(world, space, 20);
        var firstLog = new CollisionLog();
        var secondLog = new CollisionLog();
        Entity first = world.Spawn(new SpawnDesc { Frame = space, Pose = At(13, 0.5), LinearVelocity = new Vector3(2, 0, 0), Body = Cube(), Components = [firstLog] });
        Entity second = world.Spawn(new SpawnDesc { Frame = space, Pose = At(13, -0.5), LinearVelocity = new Vector3(2, 0, 0), Body = Cube(), Components = [secondLog] });

        Tick(world, 90);

        world.GetFrame(first).Should().Be(inside);
        world.GetFrame(second).Should().Be(inside);
        firstLog.NewsOf(second).Should().Equal(Started);
        secondLog.NewsOf(first).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, two crates touch end to end and drift together into a frame's bubble. The front crate moves into
    /// the frame 30 steps before the other. In between, the back crate touches the front crate's ghost, which sticks out
    /// of the bubble, and a contact with a ghost is a contact of the two entities. So the contact goes on through the
    /// ghost, and then in the frame once both crates are in: each crate hears one Collision and no CollisionEnded. The
    /// order of the two entities does not matter.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACrateThatEntersAFrameAheadOfTheCrateThatTouchesItKeepsTheContactThroughItsGhost(bool frontFirst)
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        FrameHandle inside = Bubble(world, space, 20);
        var frontLog = new CollisionLog();
        var backLog = new CollisionLog();
        var frontSpawn = new SpawnDesc { Frame = space, Pose = At(14), LinearVelocity = new Vector3(2, 0, 0), Body = Cube(), Components = [frontLog] };
        var backSpawn = new SpawnDesc { Frame = space, Pose = At(13), LinearVelocity = new Vector3(2, 0, 0), Body = Cube(), Components = [backLog] };
        Entity front = frontFirst ? world.Spawn(frontSpawn) : Entity.Null;
        Entity back = world.Spawn(backSpawn);
        if (!frontFirst)
            front = world.Spawn(frontSpawn);

        Tick(world, 90);

        world.GetFrame(front).Should().Be(inside);
        world.GetFrame(back).Should().Be(inside);
        frontLog.NewsOf(back).Should().Equal([Started], "the back crate touched the front crate's ghost until it entered too");
        backLog.NewsOf(front).Should().Equal([Started]);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Under gravity, a crate that nothing slows down slides on a static floor at 2 m/s into a frame's bubble. The frame has
    /// the same gravity and no floor of its own, but the floor reaches its bubble, so the floor has a ghost there (ticket
    /// ter-xxmj). The hand-off carries the contact to that ghost, and the crate slides on over it with its one contact, as
    /// it slides on the floor in one frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACrateThatSlidesOnAStaticFloorIntoABubbleKeepsItsContactWithTheFloor(bool acrossFrames)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        FrameHandle inside = !acrossFrames ? ground : world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(20, 0.5),
            AsFrame = new FrameSpec(FrameBounds.Sphere(5), GravityField.Uniform(new Vector3(0, -9.81f, 0))),
        }));
        var floorLog = new CollisionLog();
        Entity floor = LoggedFloor(world, ground, floorLog);
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(13, 0.5),
            LinearVelocity = new Vector3(2, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, Friction: 0, LinearDamping: 0, AngularDamping: 0),
            Components = [crateLog],
        });

        Tick(world, 60);
        world.GetFrame(crate).Should().Be(inside);
        Tick(world, 60);

        world.GetFrame(crate).Should().Be(inside, "the crate is 2 m into the bubble");
        DPose pose = world.PoseIn(crate, ground);
        pose.Position.X.Should().BeApproximately(17, 0.05);
        pose.Position.Y.Should().BeApproximately(0.5, 0.02, "the crate slides on the floor");
        crateLog.NewsOf(floor).Should().Equal(Started);
        floorLog.NewsOf(crate).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABallThatRollsOnTheFloorThroughTwoGrowthsOfItsSystemHearsNoEnd()
    {
        var warnings = new List<string>();
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { MaxBodiesPerFrame = 8, WorkerThreads = 2, Log = warnings.Add });
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, backend);
        FrameHandle ground = Ground(world, 9.81f);
        var floorLog = new CollisionLog();
        Entity floor = LoggedFloor(world, ground, floorLog);
        var ballLog = new CollisionLog();
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), LinearVelocity = new Vector3(1, 0, 0), Body = Ball(), Components = [ballLog] });
        Tick(world, 30);

        // One batch of 15 static crates far below the floor grows the ground's system twice.
        for (int i = 0; i < 15; i++)
            world.Spawn(new SpawnDesc { Frame = ground, Pose = At(3 * i, -100), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        Tick(world, 10);

        warnings.Count(warning => warning.Contains("so it grew")).Should().Be(2);
        ballLog.NewsOf(floor).Should().Equal(Started);
        floorLog.NewsOf(ball).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }
}
