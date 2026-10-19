using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Contacts through ghosts (ter-ejx9), on the World and the real backend. A contact between the body of one entity and
/// the ghost of another entity's body is a contact of the two entities, and it can start through the ghost. So is a
/// pair of two ghosts, which Jolt makes only when one of them is a sensor (see GhostPairTests). The backend counts, for each pair of entities,
/// the systems that hold their contact as started, so each pair hears one Collision when the first system starts it
/// and one CollisionEnded when the last system drops it.
/// </summary>
public class GhostContactTests
{
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    private static FrameHandle Space(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));

    /// <summary>A frame without a body and with no gravity, with a bubble of <paramref name="radius"/> m at (<paramref name="x"/>, <paramref name="y"/>, 0) in <paramref name="parent"/>.</summary>
    private static FrameHandle Bubble(World world, FrameHandle parent, double x, float radius, double y = 0) => world.GetOwnedFrame(world.Spawn(new SpawnDesc
    {
        Frame = parent,
        Pose = At(x, y),
        AsFrame = new FrameSpec(FrameBounds.Sphere(radius)),
    }));

    /// <summary>
    /// Two sibling bubbles of radius 10 m in space, with no gravity, 0.5 m apart along X: the first ends at x = -0.25 m,
    /// the second begins at x = 0.25 m. A ball of 1 kg and radius 0.25 m in the first frame, at x = 0.05 m in space, touches
    /// a box of 10 kg in the second frame, 2 m deep and 6 m wide and high, at x = 1.3 m, so that the two touch at x = 0.3 m
    /// with nothing moving. The ball's center is 0.3 m outside its own bubble, less than the 0.5 m at which it would leave,
    /// and 0.2 m from the second bubble, within its radius. The box's bounding sphere, of radius √19 m, crosses both
    /// bubbles. So each has a ghost in the other's frame, and one in space.
    /// </summary>
    private sealed record MirrorScene(FrameHandle First, FrameHandle Second, Entity Ball, Entity Box, CollisionLog BallLog, CollisionLog BoxLog, Pilot BallPilot);

    /// <remarks>The scene sits at the height <paramref name="y"/> in space. Each body's pose is in its own frame.</remarks>
    private static MirrorScene MirrorPair(World world, FrameHandle space, double y = 0)
    {
        FrameHandle first = Bubble(world, space, -10.25, 10, y);
        FrameHandle second = Bubble(world, space, 10.25, 10, y);
        var ballLog = new CollisionLog();
        var boxLog = new CollisionLog();
        var pilot = new Pilot();
        Entity ball = world.Spawn(new SpawnDesc { Frame = first, Pose = At(0.05 + 10.25), Body = Ball(0.25f), Components = [ballLog, pilot] });
        Entity box = world.Spawn(new SpawnDesc
        {
            Frame = second,
            Pose = At(1.3 - 10.25),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1, 3, 3)), Mass: 10, LinearDamping: 0, AngularDamping: 0),
            Components = [boxLog],
        });
        return new MirrorScene(first, second, ball, box, ballLog, boxLog, pilot);
    }

    /// <summary>
    /// In the scene of <see cref="MirrorPair"/>, the first frame's system holds the ball and the box's ghost, and the
    /// second frame's system holds the box and the ball's ghost: a mirror pair. Both systems start the contact in the
    /// same step, and the two entities hear it once, with the point and normal of the frame with the lower index. The
    /// two rest against each other for two seconds, which ends nothing. Then the ball slides along the box's face until it no
    /// longer touches the second bubble, so its ghost there goes, and the second system drops the pair: one system still
    /// holds it, so nothing ends. Last, the ball moves away from the box, the first system drops the pair too, and each
    /// entity hears one CollisionEnded.
    /// </summary>
    [Fact]
    public void AMirrorPairIsReportedOnceAndEndsOnceAfterTheLastSystemDropsIt()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        MirrorScene scene = MirrorPair(world, space);
        Tick(world, 3);
        faults.Jolt.HasGhostIn(scene.Ball, scene.Second).Should().BeTrue("the ball touches the second bubble");
        faults.Jolt.HasGhostIn(scene.Box, scene.First).Should().BeTrue("the box touches the first bubble");

        ContactStarted contact = faults.Outputs.SelectMany(output => output.Contacts).Should().ContainSingle().Subject;
        new[] { contact.A, contact.B }.Should().BeEquivalentTo([scene.Ball, scene.Box]);
        contact.Frame.Should().Be(scene.First.Index < scene.Second.Index ? scene.First : scene.Second,
            "both systems start the contact in the same step, and the frame with the lower index gives the point and normal");
        scene.BallLog.NewsOf(scene.Box).Should().Equal([Started]);
        scene.BoxLog.NewsOf(scene.Ball).Should().Equal([Started]);

        Tick(world, 120);
        scene.BallLog.NewsOf(scene.Box).Should().Equal([Started], "rest ends no contact");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        // At 0.5 m/s for 3 s, the ball slides 1.5 m along the face. Its center is then 0.31 m from the second bubble.
        scene.BallPilot.Once = body => body.SetVelocity(new Double3(0, 0.5, 0));
        Tick(world, 180);
        scene.BallPilot.Once = body => body.SetVelocity(Double3.Zero);
        Tick(world, 2);
        faults.Jolt.HasGhostIn(scene.Ball, scene.Second).Should().BeFalse("the ball no longer touches the second bubble");
        faults.Jolt.HasGhostIn(scene.Box, scene.First).Should().BeTrue();
        scene.BallLog.NewsOf(scene.Box).Should().Equal([Started], "the first system still holds the contact");
        scene.BoxLog.NewsOf(scene.Ball).Should().Equal([Started]);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        scene.BallPilot.Once = body => body.SetVelocity(new Double3(-0.5, 0, 0));
        Tick(world, 30);
        scene.BallLog.NewsOf(scene.Box).Should().Equal(Started, Ended);
        scene.BoxLog.NewsOf(scene.Ball).Should().Equal(Started, Ended);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, a kinematic block in a frame of radius 10 m sticks 1 m out of the bubble, so space holds a ghost of
    /// it. A person, a character of 70 kg in space, drifts at 2 m/s into the part that sticks out and never touches the
    /// bubble. Jolt makes no contact between the character's inner body and the ghost, which are both kinematic, but the
    /// character's own collisions hit the ghost, and a character's contact with a ghost counts by the rule of any other
    /// contact. The person and the block each hear one Collision, whose impulse is the person's speed into the block times
    /// the person's mass, because the block is kinematic. When the block moves fully inside, its ghost goes, which ends
    /// the contact.
    /// </summary>
    [Fact]
    public void APersonWhoDriftsIntoTheGhostOfABlockAndTheBlockEachHearTheContact()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        FrameHandle bubble = Bubble(world, space, 0, 10);
        var blockLog = new CollisionLog();
        var pilot = new Pilot();
        Entity block = world.Spawn(new SpawnDesc
        {
            Frame = bubble, Pose = At(10), Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic), Components = [blockLog, pilot],
        });
        var personLog = new CollisionLog();
        Entity person = world.Spawn(new SpawnDesc { Frame = space, Pose = At(14), LinearVelocity = new Vector3(-2, 0, 0), Body = Person, Components = [personLog] });

        Tick(world, 120);

        world.GetFrame(person).Should().Be(space, "the person never touched the bubble");
        world.PoseInOwnFrame(person).Position.X.Should().BeGreaterThan(11.2, "the ghost stopped the person");
        personLog.NewsOf(block).Should().Equal([Started]);
        blockLog.NewsOf(person).Should().Equal([Started]);
        personLog.Seen.Single().Impulse.Should().BeApproximately(140f, 14f, "the person of 70 kg moved into the kinematic block at 2 m/s");

        pilot.Once = body => body.Teleport(new DPose(new Double3(5, 0, 0), DQuaternion.Identity));
        Tick(world, 3);
        faults.Jolt.GhostCount(block).Should().Be(0);
        personLog.NewsOf(block).Should().Equal(Started, Ended);
        blockLog.NewsOf(person).Should().Equal(Started, Ended);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, a dynamic crate of 10 kg rests in a frame of radius 10 m and sticks 1 m out of the bubble, so space
    /// holds a ghost of it. A ball of 1 kg in space flies at the part that sticks out at 5 m/s and hits the ghost. A ghost
    /// stands for its real body, so the impulse uses the masses of both real bodies: the approach speed times the reduced
    /// mass of 1 kg and 10 kg, 10/11 kg, and not the ball's mass alone, as if the ghost had no mass. So the impulse does
    /// not depend on which system reports the contact.
    /// </summary>
    [Fact]
    public void TheImpulseOfAHitOnTheGhostOfADynamicBodyUsesTheMassesOfBothRealBodies()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        FrameHandle bubble = Bubble(world, space, 0, 10);
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = bubble, Pose = At(10), Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), Mass: 10, LinearDamping: 0, AngularDamping: 0), Components = [crateLog],
        });
        Entity ball = world.Spawn(new SpawnDesc { Frame = space, Pose = At(16), LinearVelocity = new Vector3(-5, 0, 0), Body = Ball(0.25f) });

        Tick(world, 90);

        world.GetFrame(ball).Should().Be(space);
        faults.Jolt.HasGhostIn(crate, space).Should().BeTrue();
        crateLog.NewsOf(ball).Should().Equal([Started]);
        crateLog.Seen.Single().Impulse.Should().BeApproximately(5f * 10f / 11f, 0.1f, "the ball of 1 kg approached the crate of 10 kg at 5 m/s");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, a light character of 2 kg stands still in a frame of radius 10 m, with its capsule of radius
    /// 0.3 m 0.3 m out of the bubble, so space holds a ghost of it. A ball of 1 kg in space hits the ghost at 5 m/s. A ghost
    /// stands for its real body, here a character, so the impulse is the approach speed times the reduced mass of 1 kg and
    /// 2 kg, 2/3 kg.
    /// </summary>
    [Fact]
    public void TheImpulseOfAHitOnTheGhostOfACharacterUsesTheCharactersMass()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        FrameHandle bubble = Bubble(world, space, 0, 10);
        var personLog = new CollisionLog();
        Entity person = world.Spawn(new SpawnDesc { Frame = bubble, Pose = At(10), Body = Person with { Mass = 2 }, Components = [personLog] });
        Entity ball = world.Spawn(new SpawnDesc { Frame = space, Pose = At(16), LinearVelocity = new Vector3(-5, 0, 0), Body = Ball(0.25f) });

        Tick(world, 90);

        world.GetFrame(ball).Should().Be(space);
        faults.Jolt.HasGhostIn(person, space).Should().BeTrue();
        personLog.NewsOf(ball).Should().Equal([Started]);
        personLog.Seen.Single().Impulse.Should().BeApproximately(5f * 2f / 3f, 0.1f, "the ball of 1 kg approached the character of 2 kg at 5 m/s");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The scene of <see cref="TheImpulseOfAHitOnTheGhostOfADynamicBodyUsesTheMassesOfBothRealBodies"/>: a crate of 10 kg
    /// sticks 1 m out of a frame of radius 10 m, and a ball of 1 kg in space hits it at 5 m/s. The crate's bounding sphere
    /// grows the frame's content 1.7 m beyond the bubble, and the ball comes within 1 m of the bubble. But a dynamic body
    /// reaches only the bubble (fork F2 of docs/plans/complete-ghosts.md): the crate meets it through the crate's own
    /// stick-out ghost in space, so the ball gets no ghost in the frame. Such a ghost would make a mirror pair of two
    /// kinematic ghosts, in which each real body meets the other as an immovable body: the crate would be shoved into its
    /// bubble at about the ball's full speed. So the ball stops at the crate's ghost, and the crate stays where it is, as
    /// before the content.
    /// </summary>
    [Fact]
    public void ABallThatHitsACrateThatSticksOutOfAFrameGetsNoGhostInTheFrameAndLeavesTheCrateWhereItIs()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        FrameHandle bubble = Bubble(world, space, 0, 10);
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = bubble, Pose = At(10), Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), Mass: 10, LinearDamping: 0, AngularDamping: 0),
        });
        Entity ball = world.Spawn(new SpawnDesc { Frame = space, Pose = At(16), LinearVelocity = new Vector3(-5, 0, 0), Body = Ball(0.25f) });

        for (int i = 0; i < 90; i++)
        {
            world.Tick();
            faults.Jolt.HasGhostIn(ball, bubble).Should().BeFalse("the ball's bounding sphere never touches the bubble");
        }

        world.PoseIn(ball, space).Position.X.Should().BeGreaterThan(11.2, "the ball stops at the crate's ghost");
        world.VelocityIn(crate, space).Length.Should().BeLessThan(0.05, "nothing pushes the crate");
        world.PoseIn(crate, space).Position.X.Should().BeApproximately(10, 0.01);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Under gravity, a frame without a body at the origin has a bubble of 5 m, and a static floor of the parent, with its
    /// top at y = 0, crosses it. A crate of 10 kg of the frame rests on the floor's ghost at x = 5.3 m, so it sticks out of
    /// the bubble, and its bounding sphere grows the frame's content 1.2 m beyond the bubble. A person of the parent walks
    /// along -X at 2 m/s into the crate, and its bounding sphere comes within 1.2 m of the bubble. A character reaches
    /// only the bubble, as a dynamic body does (fork F2): the crate meets the person through the crate's own stick-out ghost,
    /// so the person gets no ghost in the frame, which would shove the crate as an immovable body. So the person stops at
    /// the crate's ghost, and the crate stays where it is.
    /// </summary>
    [Fact]
    public void APersonWhoWalksIntoACrateThatSticksOutOfAFrameGetsNoGhostInTheFrameAndLeavesTheCrateWhereItIs()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var gravity = GravityField.Uniform(new Vector3(0, -9.81f, 0));
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), gravity));
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, -0.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(20, 0.5f, 20)), BodyMotion.Static) });
        FrameHandle bubble = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0), AsFrame = new FrameSpec(FrameBounds.Sphere(5), gravity) }));
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = bubble, Pose = At(5.3, 0.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, Restitution: 0),
        });
        var pilot = new Pilot { EveryTick = handle => handle.MoveCharacter(new Vector3(-2, 0, 0)) };
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(9, 0.9), Body = Person, Components = [pilot] });

        for (int i = 0; i < 150; i++)
        {
            world.Tick();
            faults.Jolt.HasGhostIn(person, bubble).Should().BeFalse("the person's bounding sphere never touches the bubble");
        }

        world.PoseIn(person, ground).Position.X.Should().BeInRange(6.0, 6.3, "the person stops at the crate's ghost");
        world.VelocityIn(crate, ground).Length.Should().BeLessThan(0.05, "nothing pushes the crate");
        world.PoseIn(crate, ground).Position.X.Should().BeApproximately(5.3, 0.01);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>True while Jolt simulates the ghost of <paramref name="entity"/> in the system of <paramref name="frame"/>.</summary>
    private static bool IsGhostAwake(JoltPhysicsBackend backend, FrameHandle frame, Entity entity) =>
        backend.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, entity)).Active;

    /// <summary>
    /// Under gravity, a kinematic block at rest in a frame of radius 10 m sticks 1 m out of the top of the bubble, so the
    /// parent holds a ghost of it, and a crate in the parent lands on the ghost. For comparison, a second crate lands on a
    /// kinematic block at rest in the parent itself. A held pair ends at the first update that simulates one of its bodies
    /// without finding the pair, so it matters whether Jolt simulates a ghost at rest. It does: the ghost pass sets the
    /// ghost's pose in every step, and Jolt resets the sleep timer of a body whose pose is set, so the ghost never falls
    /// asleep. Jolt joins an awake kinematic body to the island of each dynamic body that it touches, so the crate on it
    /// stays awake too, and Jolt finds their contact in every step: the contact never ends. The crate on the plain block
    /// falls asleep, and so does the block.
    /// </summary>
    [Fact]
    public void ACrateThatRestsOnTheGhostOfABlockAtRestKeepsItsContactAndNeitherFallsAsleep()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Ground(world, 9.81f);
        FrameHandle bubble = Bubble(world, space, 0, 10);
        var blockLog = new CollisionLog();
        Entity block = world.Spawn(new SpawnDesc
        {
            Frame = bubble, Pose = At(0, 10), Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic), Components = [blockLog],
        });
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, 11.6), Body = Cube(), Components = [crateLog] });
        Entity plainBlock = world.Spawn(new SpawnDesc { Frame = space, Pose = At(50, 10), Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic) });
        Entity plainCrate = world.Spawn(new SpawnDesc { Frame = space, Pose = At(50, 11.6), Body = Cube() });

        Tick(world, 360);

        world.GetFrame(crate).Should().Be(space);
        world.PoseInOwnFrame(crate).Position.Y.Should().BeApproximately(11.5, 1e-3, "the crate rests on the ghost");
        IsAwake(faults.Jolt, space, plainCrate).Should().BeFalse("a crate on a kinematic block at rest falls asleep");
        IsAwake(faults.Jolt, space, plainBlock).Should().BeFalse();
        IsGhostAwake(faults.Jolt, space, block).Should().BeTrue("the ghost pass sets the ghost's pose in every step");
        IsAwake(faults.Jolt, space, crate).Should().BeTrue("the crate shares an island with the awake ghost");
        crateLog.NewsOf(block).Should().Equal([Started]);
        blockLog.NewsOf(crate).Should().Equal([Started]);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Every contact, start and end of a scene with contacts through ghosts, as text: two crates end to end that drift
    /// into a bubble one after the other, the mirror pair of <see cref="MirrorPair"/>, and a ball that hits the ghost of a
    /// kinematic block. The world runs on <paramref name="threads"/> gameplay threads, and its backend on as many Jolt
    /// threads. Each line holds the step, the entities, the frame, and the bits of the point, the normal and the impulse.
    /// </summary>
    private static List<string> ContactsOfAGhostScene(int threads)
    {
        var faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = threads }));
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = threads, DebugChecks = true, DisposePhysics = true }, faults);
        FrameHandle space = Space(world);
        // The three parts lie 100 m apart, so that each meets only its own bubbles.
        Bubble(world, space, 20, 5);
        world.Spawn(new SpawnDesc { Frame = space, Pose = At(14), LinearVelocity = new Vector3(2, 0, 0), Body = Cube() });
        world.Spawn(new SpawnDesc { Frame = space, Pose = At(13), LinearVelocity = new Vector3(2, 0, 0), Body = Cube() });
        MirrorPair(world, space, y: 100);
        FrameHandle blockFrame = Bubble(world, space, 0, 10, y: -100);
        world.Spawn(new SpawnDesc { Frame = blockFrame, Pose = At(10), Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic) });
        world.Spawn(new SpawnDesc { Frame = space, Pose = At(16, -100), LinearVelocity = new Vector3(-5, 0, 0), Body = Ball(0.25f) });
        Tick(world, 90);
        world.Errors.Should().BeEmpty();

        var lines = new List<string>();
        for (int step = 0; step < faults.Outputs.Count; step++)
        {
            foreach (ContactStarted c in faults.Outputs[step].Contacts)
                lines.Add($"{step} start {c.A} {c.B} {c.Frame} {BitConverter.DoubleToInt64Bits(c.Point.X)} {BitConverter.DoubleToInt64Bits(c.Point.Y)} " +
                          $"{BitConverter.DoubleToInt64Bits(c.Point.Z)} {c.Normal.X:R} {c.Normal.Y:R} {c.Normal.Z:R} {c.Impulse:R}");
            foreach (ContactEnded e in faults.Outputs[step].ContactsEnded)
                lines.Add($"{step} end {e.A} {e.B}");
        }
        return lines;
    }

    [Fact]
    public void ASceneWithContactsThroughGhostsGivesTheSameContactsInEveryRunWithOneOrFourThreads()
    {
        List<string> first = ContactsOfAGhostScene(1);
        List<string> second = ContactsOfAGhostScene(1);
        List<string> parallel = ContactsOfAGhostScene(4);

        first.Count(line => line.Contains(" start ")).Should().BeGreaterThanOrEqualTo(3, "the crates, the mirror pair and the ball each start a contact");
        second.Should().Equal(first);
        parallel.Should().Equal(first);
    }

    // ---- Review of ter-ejx9 ----
    //
    // Numeric frames are an engine detail with no bubbles, so they must not change which bodies a body meets or which
    // contacts gameplay hears. A ghost stands for its real body, so it meets what that body meets. Each test that
    // involves numeric frames runs the same scene without them.

    /// <summary>A world on the wrapper with the default numeric frames or, when <paramref name="numericFrames"/> is false, with none.</summary>
    private static World WorldWith(bool numericFrames, out FaultInjectingBackend faults) =>
        NewWorldWithFaults(out faults, configure: numericFrames ? null : options => options.NumericFrames = WithoutNumericFrames);

    /// <summary>True while the numeric update names <paramref name="frame"/> to keep a ghost of the anchor <paramref name="anchor"/>.</summary>
    private static bool NamesAnchor(World world, Entity anchor, FrameHandle frame)
    {
        foreach (NumericGhost ghost in world.Frames.AnchorGhosts)
            if (ghost.Anchor == anchor && ghost.Frame == frame)
                return true;
        return false;
    }

    /// <summary>A kinematic sensor ball of radius 0.25 m.</summary>
    private static readonly BodyDesc KinematicSensorBall =
        new BodyDesc(new SphereShapeDesc(0.25f), BodyMotion.Kinematic, Mass: 1, LinearDamping: 0, AngularDamping: 0) { IsSensor = true };

    /// <summary>The speed of the kinematic ball of <see cref="FlyThroughAStaticBox"/>, in m/s: 20 m per step.</summary>
    private const double KinematicBallSpeed = 1_200;

    /// <summary>
    /// Runs a ball of <paramref name="ballBody"/> at 1,200 m/s along +X from (-600, 0, 0) through a static box of 4 m by 4 m
    /// by 4 m at the origin, with no gravity, for 3 s. The ball moves 20 m per step, so the step of tick 30 ends with its
    /// center at the box's center, and no other step ends with the two touching. A kinematic body never thrusts, so the ball
    /// needs the 1,000 m/s of a group of free bodies for a numeric frame of its own, and at 1,200 m/s it is fast enough. But
    /// the merge test links it to the box, an anchor, from the start, so it joins the box's home, the system of ground, and
    /// meets the real box there (ter-dhkf). Once the ball is out of the merge test's reach, 2.4 km past the box, it falls in
    /// a numeric frame of its own. The run checks both. Before, the ball fell in a frame of its own, which kept a kinematic
    /// ghost of the box. Returns what the box and the ball heard of each other, and whether the ball's physics home ever held
    /// a ghost of the box.
    /// </summary>
    private static (List<ContactNews> BoxHeard, List<ContactNews> BallHeard, bool GhostOfTheBox) FlyThroughAStaticBox(bool numericFrames,
        BodyDesc boxBody, BodyDesc ballBody)
    {
        using World world = WorldWith(numericFrames, out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 0);
        var boxLog = new CollisionLog();
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Body = boxBody, Components = [boxLog] });
        var ballLog = new CollisionLog();
        var flyer = new SpawnDesc
        {
            Frame = ground, Pose = At(-30 * KinematicBallSpeed * world.DeltaTime), LinearVelocity = new Double3(KinematicBallSpeed, 0, 0),
            Body = ballBody, Components = [ballLog],
        };
        // A kinematic body moves only by its motion intents, so it drifts by end poses.
        Entity ball = world.Spawn(ballBody.Motion == BodyMotion.Kinematic ? KinematicDrift.Drifting(flyer, world, flyer.LinearVelocity) : flyer);
        bool ghostOfTheBox = false;
        for (int tick = 0; tick < 180; tick++)
        {
            world.Tick();
            ghostOfTheBox |= faults.Jolt.HasGhostIn(box, world.PhysicsHomeOf(ball));
            if (world.TickNumber == 30)
                world.PhysicsHomeOf(ball).Should().Be(ground, "the ball runs in the box's home as it passes the box, with numeric frames or without");
        }
        world.PoseIn(ball, ground).Position.X.Should().BeGreaterThan(2_500, "the ball flew through the box");
        world.Frames.Numeric(world.PhysicsHomeOf(ball)).Should().Be(numericFrames ? NumericMode.Falling : NumericMode.None,
            "out of the merge test's reach, the ball at 1,200 m/s falls in a numeric frame of its own, when there are numeric frames");
        world.Errors.Should().BeEmpty();
        return (boxLog.NewsOf(ball), ballLog.NewsOf(box), ghostOfTheBox);
    }

    /// <summary>
    /// A kinematic sensor ball flies through a static box (see <see cref="FlyThroughAStaticBox"/>). Jolt makes no contact
    /// between a kinematic body and a static one, sensor or not, so the sensor never detects the box without numeric
    /// frames. With numeric frames the sensor joins the box's home, so it does not detect the box either. Before ter-dhkf,
    /// the sensor fell in a frame of its own, whose ghost of the box stood for the box and was not detected.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AKinematicSensorThatFliesThroughAStaticBoxJoinsItsHomeAndDetectsNothingWithNumericFramesOrWithout(bool numericFrames)
    {
        var heard = FlyThroughAStaticBox(numericFrames, new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static), KinematicSensorBall);

        heard.GhostOfTheBox.Should().BeFalse("the sensor joins the home of the box, an anchor, so no frame keeps a ghost of the box");
        heard.BallHeard.Should().BeEmpty("a kinematic sensor never detects a static body");
        heard.BoxHeard.Should().BeEmpty();
    }

    /// <summary>
    /// A kinematic ball flies through a static sensor box (see <see cref="FlyThroughAStaticBox"/>). Jolt makes a contact
    /// between a kinematic body and a sensor, so the sensor detects the ball, with numeric frames and without alike: the
    /// ball joins the sensor's home. Before ter-dhkf, the sensor detected the ball through its ghost in the ball's falling
    /// frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStaticSensorDetectsAKinematicBallThatJoinsItsHomeAndFliesThroughItWithNumericFramesOrWithout(bool numericFrames)
    {
        var heard = FlyThroughAStaticBox(numericFrames, new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static) { IsSensor = true },
            KinematicSensorBall with { IsSensor = false });

        heard.GhostOfTheBox.Should().BeFalse("the ball joins the home of the sensor, an anchor");
        heard.BoxHeard.Should().Equal(Started, Ended);
        heard.BallHeard.Should().Equal(Started, Ended);
    }

    /// <summary>
    /// The other ghost of a static body: the hull of a parked station, a static box of 4 m by 4 m by 4 m at the origin of
    /// space with a bubble of radius 40 m, has a ghost in an empty frame whose bubble of radius 30 m around (20, 0, 0)
    /// overlaps the station's, and which does not fit in it. A kinematic sensor ball in that frame flies at 4 m/s along -X
    /// through the ghost. With no numeric frame in the scene, the ghost of the hull is kinematic too, as for every hull
    /// that is not a mesh. It stands for a static hull, so the sensor does not detect it, as it does not detect the static
    /// interior of the station inside the station's bubble. The same holds when the station's hull was dynamic until the
    /// game parked it in the second tick, which made the hull static while it kept its ghost.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AKinematicSensorDoesNotDetectTheGhostOfAParkedStationsHull(bool parkedByTheGame)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        var stationLog = new CollisionLog();
        Entity stationOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), parkedByTheGame ? BodyMotion.Dynamic : BodyMotion.Static, Mass: 1000,
                LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(40)),
            Components = [stationLog],
        });
        FrameHandle beside = Bubble(world, space, 20, 30);
        var sensorLog = new CollisionLog();
        Entity sensor = world.Spawn(KinematicDrift.Drifting(new SpawnDesc
        {
            Frame = beside, Pose = At(-14), Body = KinematicSensorBall, Components = [sensorLog],
        }, world, new Double3(-4, 0, 0)));
        world.Tick();
        faults.Jolt.HasGhostIn(stationOwner, beside).Should().BeTrue("the bubbles overlap, and the station does not fit in the other frame");
        if (parkedByTheGame)
            world.SetMotionNextTick(stationOwner, BodyMotion.Static);

        bool ghostSeen = false;
        for (int tick = 0; tick < 150; tick++)
        {
            world.Tick();
            ghostSeen |= faults.Jolt.HasGhostIn(stationOwner, beside);
        }
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        ghostSeen.Should().BeTrue("the bubbles overlap, and the station does not fit in the other frame");
        world.GetFrame(sensor).Should().Be(beside);
        world.PoseIn(sensor, space).Position.X.Should().BeLessThan(-3, "the sensor flew through the ghost of the hull");
        sensorLog.NewsOf(stationOwner).Should().BeEmpty("a kinematic sensor never detects a static body");
        stationLog.NewsOf(sensor).Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A Kepler hull, a box of 2 m by 2 m by 2 m, flies at 2 km/s in an orbit frame of its own past a static rock of radius
    /// 1 m, 8 m to the side of its path (see <see cref="Scenes.FastKeplerHull"/>). No home holds both anchors at rest, so
    /// the merge test links them into a group that lives in the rock's home, the system of space, and the numeric update
    /// names that system to keep a ghost of the hull. Far past the rock, the update stops naming it, and the ghost goes at
    /// the same sync point, although nothing else changed the frame tree. So the backend keeps its invariants between
    /// ticks, and no ghost stands for the hull where the tree allows none. Before ter-dhkf rule 3, the falling frame of a
    /// fast ball beside a still Kepler hull kept the ghost, and the ball now joins the hull's system instead.
    /// </summary>
    [Fact]
    public void TheGhostOfAKeplerHullInTheHomeOfAStaticRockGoesAtTheSyncPointWhoseNumericUpdateStopsNamingIt()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity box = Scenes.FastKeplerHull(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic));
        world.Spawn(new SpawnDesc { Frame = space, Pose = At(5_008, 0, -100), Body = new BodyDesc(new SphereShapeDesc(1), BodyMotion.Static) });
        Tick(world, 2);
        world.Frames.Numeric(world.PhysicsHomeOf(box)).Should().Be(NumericMode.Orbiting);
        NamesAnchor(world, box, space).Should().BeTrue();
        faults.Jolt.HasGhostIn(box, space).Should().BeTrue();

        TickUntil(world, () => !NamesAnchor(world, box, space), 600);

        NamesAnchor(world, box, space).Should().BeFalse("the hull flies on until the merge test lets go of the rock");
        faults.Jolt.HasGhostIn(box, space).Should().BeFalse("the numeric update no longer names space to keep a ghost of the hull");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Tick();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, a ship of 1000 kg and a probe, a ball of radius 1 m, fly at 60 m/s along -X, 3 m apart, past a
    /// static mesh cube of 2 m by 2 m by 2 m at the origin. Both thrust gently across their flight (see <see cref="Thruster"/>)
    /// from their spawn on, so from tick 30 on, 70 m before the cube, they are fast enough for a falling numeric frame. With
    /// numeric frames, the merge test links both to the cube, an anchor, from the start, so they stay in the cube's home,
    /// the system of space, and no frame keeps a ghost of the cube (ter-dhkf). When the probe is 120 m past the cube, it
    /// stops and drifts down at 1.5 m/s across the lane of the cube. Nothing hits the probe, as without numeric frames. The
    /// ship flies on, out of the merge test's reach of the cube, and falls in a numeric frame. Before ter-dhkf, both fell in
    /// one numeric frame, which kept a ghost of the cube while the numeric update named it an anchor, and the ghost had gone
    /// when the place in that frame where it stood came past the probe.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AProbeThatStopsFarPastAStaticMeshCubeIsNotHitByTheCubeWithNumericFramesOrWithout(bool numericFrames)
    {
        using World world = WorldWith(numericFrames, out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        Entity cube = world.Spawn(new SpawnDesc { Frame = space, Body = new BodyDesc(MeshCube(1), BodyMotion.Static) });
        Entity ship = world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(100, 6), LinearVelocity = new Vector3(-60, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            Components = [new Thruster(1000) { Direction = Vector3.UnitZ }],
        });
        var probeLog = new CollisionLog();
        var pilot = new Pilot();
        Entity probe = world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(100, 3), LinearVelocity = new Vector3(-60, 0, 0), Body = Ball(1),
            Components = [probeLog, pilot, new Thruster(1) { Direction = Vector3.UnitZ }],
        });
        Tick(world, 30);
        world.PhysicsHomeOf(probe).Should().Be(space, "the probe thrusts, but it stays in the home of the cube, with numeric frames or without");
        world.PhysicsHomeOf(ship).Should().Be(space, "the ship thrusts, but it stays in the home of the cube, with numeric frames or without");
        faults.Jolt.GhostCount(cube).Should().Be(0);
        TickUntil(world, () => world.PoseIn(probe, space).Position.X < -120, 600);
        probeLog.Seen.Should().BeEmpty("the probe passed 3 m from the cube's center");

        pilot.Once = body => body.SetVelocity(new Double3(0, -1.5, 0));
        Tick(world, 150);

        world.PoseIn(probe, space).Position.Y.Should().BeLessThan(0, "the probe drifted across the lane of the cube");
        probeLog.Seen.Should().BeEmpty("the cube is more than 100 m away");
        world.Frames.Numeric(world.PhysicsHomeOf(ship)).Should().Be(numericFrames ? NumericMode.Falling : NumericMode.None,
            "the ship that thrusts at 60 m/s falls once it is out of the merge test's reach of the cube, when there are numeric frames");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, a crate of 10 kg flies at 3 m/s at a static wall, a cube of 1 m at the origin, a mesh or a box.
    /// With numeric frames that would make every moving body fall in a frame of its own, the merge test links the crate to
    /// the wall, an anchor, so the crate stays in the wall's home (ter-dhkf). 40 cm before the wall, the crate speeds up to
    /// v₁ ≈ 20 m/s, so that it is 1.2 cm from the wall at the next sync point. The contact starts in the next step, and its
    /// impulse is the crate's mass times v₁, the approach speed of the crate and the wall. Before ter-dhkf, the crate fell
    /// in a frame of its own, the sync point drained that frame, and Jolt simulated the approach to the wall's ghost.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void TheImpulseOfACrateThatSpeedsUpJustBeforeAStaticWallIsItsApproachToTheWallWithNumericFramesOrWithout(bool mesh, bool numericFrames)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults,
            configure: options => options.NumericFrames = numericFrames ? EveryGroupFalls : WithoutNumericFrames);
        FrameHandle ground = Ground(world, 0);
        ShapeDesc shape = mesh ? MeshCube(0.5f) : new BoxShapeDesc(new Vector3(0.5f));
        Entity wall = world.Spawn(new SpawnDesc { Frame = ground, Body = new BodyDesc(shape, BodyMotion.Static) });
        var crateLog = new CollisionLog();
        var pilot = new Pilot();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = ground, Pose = At(-3), LinearVelocity = new Double3(3, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0), Components = [crateLog, pilot],
        });
        double Gap() => -1 - world.PoseIn(crate, ground).Position.X;

        TickUntil(world, () => Gap() < 0.40, 200);
        world.PhysicsHomeOf(crate).Should().Be(ground, "the crate stays in the wall's home");
        faults.Jolt.GhostCount(wall).Should().Be(0);
        // The command acts in the step after the next one, and the next step closes 5 cm.
        double v1 = (Gap() - 0.05 - 0.012) / world.DeltaTime;
        pilot.Once = body => body.SetVelocity(new Double3(v1, 0, 0));
        Tick(world, 8);

        world.Errors.Should().BeEmpty();
        crateLog.Seen.Should().ContainSingle().Which.Other.Should().Be(wall);
        crateLog.Seen[0].Impulse.Should().BeApproximately((float)(10 * v1), 15f, "the crate of 10 kg approached the static wall at v₁");
    }

    /// <summary>
    /// With no gravity, a ship flies at 2 m/s along -X from (110, 0, 0) toward an empty frame, the station, with a bubble
    /// of radius 100 m at the origin of space. The ship's hull is a box of 4 m by 4 m by 4 m, its bubble has a radius of
    /// 30 m, so it fits in the station, and its interior is a small ball at its center. A crate of 2 m by 2 m by 2 m floats
    /// in the station at (103, 0, 0), 3 m outside the station's bubble but not far enough to leave it. While the bubbles
    /// overlap, the station's system holds a ghost of the hull, which reaches the crate before the hull touches the
    /// station's bubble, and pushes it ahead. Then the hull enters the station, and its ghost there goes. The hull and the
    /// crate are now in the same system, but a hull never meets a moving body (see Layers), and the crate does not touch the
    /// small interior. So no system can hold their contact any more, and it ends in the step of the hand-off, not later.
    /// </summary>
    [Fact]
    public void AContactThatNoSystemCanHoldAfterAHandOffEndsInTheStepOfTheHandOff()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        FrameHandle station = Bubble(world, space, 0, 100);
        var shipLog = new CollisionLog();
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(110),
            LinearVelocity = new Vector3(-2, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0)
            {
                InteriorShape = new SphereShapeDesc(0.05f),
            },
            AsFrame = new FrameSpec(FrameBounds.Sphere(30)),
            Components = [shipLog],
        });
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = station, Pose = At(103),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0), Components = [crateLog],
        });

        TickUntil(world, () => world.GetFrame(shipOwner) == station, 300);
        Tick(world, 30);

        int started = faults.Outputs.FindIndex(output => output.Contacts.Any(c => c.A == crate && c.B == shipOwner || c.A == shipOwner && c.B == crate));
        int handOff = faults.Outputs.FindIndex(output => output.HandOffs.Any(h => h.Entity == shipOwner && h.To == station));
        int ended = faults.Outputs.FindIndex(output => output.ContactsEnded.Any(e => e.A == crate && e.B == shipOwner || e.A == shipOwner && e.B == crate));
        started.Should().BeInRange(0, handOff - 1, "the ghost of the hull reached the crate before the hull entered the station");
        ended.Should().Be(handOff, "the hull never meets the crate in the station's system, and the crate never touches the interior");
        crateLog.NewsOf(shipOwner).Should().Equal(Started, Ended);
        shipLog.NewsOf(crate).Should().Equal(Started, Ended);
        world.Errors.Should().BeEmpty();
    }
}
