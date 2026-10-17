using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Contacts through ghosts (ter-ejx9), on the World and the real backend. A contact between the body of one entity and
/// the ghost of another entity's body is a contact of the two entities, and it can start through the ghost. A pair of
/// two ghosts is not reported: the real bodies' own systems report it. The backend counts, for each pair of entities,
/// the systems that hold their contact as started, so each pair hears one Collision when the first system starts it
/// and one CollisionEnded when the last system drops it.
/// </summary>
public class GhostContactTests
{
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    private static FrameHandle Space(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));

    /// <summary>A frame without a body and with no gravity, with a bubble of <paramref name="radius"/> m at (<paramref name="x"/>, <paramref name="y"/>, 0) in <paramref name="parent"/>.</summary>
    private static FrameHandle Bubble(World world, FrameHandle parent, double x, float radius, double y = 0) => world.GetOwnedFrame(world.Spawn(new SpawnDesc
    {
        Frame = parent,
        Pose = At(x, y),
        AsFrame = new FrameSpec(FrameBounds.Sphere(radius), MotionDriver.Static),
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
}
