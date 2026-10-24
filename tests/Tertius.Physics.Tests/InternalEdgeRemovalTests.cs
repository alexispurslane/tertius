using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Which Jolt bodies remove the internal edges of their contacts (ter-fb3w), and where that acts. Every body whose own
/// shape is made of sharp pieces sets Jolt's <c>EnhancedInternalEdgeRemoval</c>, and every other body leaves it off.
/// Such a shape is a mesh, or a compound whose every child is a box or a convex hull of convex radius 0, a mesh, or
/// such a compound. That holds for each Jolt body of an entity by its own shape: the body itself, its ghosts, and the
/// interior of the frame that it owns. Between two dynamic bodies, the flag removes the edges of the one with the
/// higher Jolt body ID only. Every character sets the flag for its own contact query, which acts on a rounded groove
/// too. What the flag does on a ring's floor is in the host's RevolvedSolidJointTests: it stops a ball that lands near
/// a joint from being pushed sideways, a box that slides across joints from tripping, and a character that walks across
/// them from being lifted.
/// </summary>
/// <remarks>
/// The first tests read the flag from Jolt. The behavior that depends on a convex body leaving it off is in
/// ConvexRadiusTests: a ball across the joint of two boxes or two hulls of the default convex radius rests in their
/// groove, 4.2 mm below their flat tops, because neither the ball nor that compound sets the flag. A ball that set it
/// would keep only one of the groove's two contacts and rest 0.8 mm deeper, and four of those tests fail. On a mesh or
/// a compound that sets the flag, a convex body's own flag changes nothing, because Jolt uses the flag of either body
/// of a pair.
/// </remarks>
public class InternalEdgeRemovalTests
{
    /// <summary>
    /// How far, in meters, a ball that lands 10 mm from a flat joint may move away from it when edge removal acts. On
    /// the raft of <see cref="BallOnRaft"/>, it moves 0.00 mm when Jolt removes the raft's edges, 14.7 mm when the raft
    /// has the flag but Jolt removes the ball's edges, and 19.5 mm when neither body has the flag. So 0.5 mm is a
    /// margin.
    /// </summary>
    private const double Sideways = 0.0005;

    private const float PersonHalfHeight = 0.6f, PersonRadius = 0.3f;

    /// <summary>The length of a tick of the test world, in s.</summary>
    private const double TickSeconds = 1.0 / 60;

    private static readonly MeshShapeDesc Triangle = new([new(-5, 0, -5), new(5, 0, -5), new(0, 0, 5)], [0, 2, 1]);

    private static readonly BoxShapeDesc SharpBox = new(new(1), ConvexRadius: 0);

    /// <summary>Two boxes of convex radius 0 side by side along X, whose faces meet flat at x = 0.</summary>
    private static readonly CompoundShapeDesc TwoSharpBoxes = new(
    [
        new(SharpBox, new(new(-1, 0, 0), Quaternion.Identity)),
        new(SharpBox, new(new(1, 0, 0), Quaternion.Identity))
    ]);

    private static readonly Vector3[] Tetrahedron = [new(1, 0, 0), new(-1, 0, 0), new(0, 2, 0), new(0, 0, -3)];

    /// <summary>A plank of 1 m by 0.25 m by 2 m, of convex radius 0.</summary>
    private static readonly BoxShapeDesc SharpPlank = new(new(0.5f, 0.125f, 1), ConvexRadius: 0);

    /// <summary>
    /// How deep, in meters, the demo's person sinks below its standing height on a flat floor when it stands across the
    /// groove of <see cref="DefaultBoxFloor"/> and keeps both contacts: as deep as a sphere of its radius and Jolt's
    /// character padding of 2 cm, 0.32 m, sinks between two edges rounded by 5 cm, whose centers of rounding lie 5 cm
    /// on each side of the joint and 5 cm below the tops: 0.32 + 0.05 - √(0.37² - 0.05²) = 3.39 mm.
    /// </summary>
    private static readonly double GrooveDepth = 0.37 - Math.Sqrt(0.37 * 0.37 - 0.05 * 0.05);

    /// <summary>
    /// A floor of two boxes of the default convex radius, 2 m by 0.5 m by 4 m each, side by side along X, so that their
    /// flat tops meet at x = 0 and the height 0, in the groove that their rounded edges leave. The floor's body leaves
    /// the flag off, because it has a rounded child.
    /// </summary>
    private static readonly CompoundShapeDesc DefaultBoxFloor = new(
    [
        new(new BoxShapeDesc(new(1, 0.25f, 2)), new(new(-1, -0.25f, 0), Quaternion.Identity)),
        new(new BoxShapeDesc(new(1, 0.25f, 2)), new(new(1, -0.25f, 0), Quaternion.Identity))
    ]);

    private static CompoundShapeDesc Beside(ShapeDesc first, ShapeDesc second) => new(
    [
        new(first, new(new(-1, 0, 0), Quaternion.Identity)),
        new(second, new(new(1, 0, 0), Quaternion.Identity))
    ]);

    public static TheoryData<string, bool> Shapes() => new()
    {
        { "box", false },
        { "box of convex radius 0", false },
        { "sphere", false },
        { "capsule", false },
        { "convex hull", false },
        { "mesh", true },
        { "compound of boxes of convex radius 0", true },
        { "compound of hulls of convex radius 0", true },
        { "compound that holds a mesh", true },
        { "compound of a compound of boxes of convex radius 0", true },
        { "compound of boxes of convex radius 0 that do not meet", true },
        { "compound of default boxes", false },
        { "compound of default hulls", false },
        { "compound of a box of convex radius 0 and a default box", false },
        { "compound of a box of convex radius 0 and a sphere", false },
        { "compound of a box of convex radius 0 and a capsule", false },
        { "compound of a box of convex radius 0 and a compound of default boxes", false }
    };

    private static ShapeDesc Shape(string name) => name switch
    {
        "box" => new BoxShapeDesc(new(1)),
        "box of convex radius 0" => SharpBox,
        "sphere" => new SphereShapeDesc(1),
        "capsule" => new CapsuleShapeDesc(1, 0.5f),
        "convex hull" => new ConvexHullShapeDesc(Tetrahedron),
        "mesh" => Triangle,
        "compound of boxes of convex radius 0" => TwoSharpBoxes,
        "compound of hulls of convex radius 0" => Beside(new ConvexHullShapeDesc(Tetrahedron, 0),
            new ConvexHullShapeDesc(Tetrahedron, 0)),
        "compound that holds a mesh" => new CompoundShapeDesc([new(Triangle, new(Vector3.Zero, Quaternion.Identity))]),
        "compound of a compound of boxes of convex radius 0" => new CompoundShapeDesc([
            new(TwoSharpBoxes, Pose.Identity)
        ]),
        // The predicate checks what the pieces are, not that they meet: where no pieces meet, the flag drops nothing.
        "compound of boxes of convex radius 0 that do not meet" => new CompoundShapeDesc(
        [
            new(SharpBox, new(new(-5, 0, 0), Quaternion.Identity)),
            new(SharpBox, new(new(5, 0, 0), Quaternion.Identity))
        ]),
        "compound of default boxes" => Beside(new BoxShapeDesc(new(1)), new BoxShapeDesc(new(1))),
        "compound of default hulls" => Beside(new ConvexHullShapeDesc(Tetrahedron),
            new ConvexHullShapeDesc(Tetrahedron)),
        "compound of a box of convex radius 0 and a default box" => Beside(SharpBox, new BoxShapeDesc(new(1))),
        "compound of a box of convex radius 0 and a sphere" => Beside(SharpBox, new SphereShapeDesc(1)),
        "compound of a box of convex radius 0 and a capsule" => Beside(SharpBox, new CapsuleShapeDesc(1, 0.5f)),
        "compound of a box of convex radius 0 and a compound of default boxes" =>
            Beside(SharpBox, Beside(new BoxShapeDesc(new(1)), new BoxShapeDesc(new(1)))),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    /// <summary>The flag of the Jolt body of <paramref name="kind"/> that stands for <paramref name="entity"/> in the system of
    /// <paramref name="frame"/>.</summary>
    private static bool RemovesInternalEdges(JoltPhysicsBackend backend, FrameHandle frame, Entity entity,
        BodyKind kind) =>
        backend.BodiesIn(frame)
            .Single(facts => BodyUserData.Kind(facts.UserData) == kind && BodyUserData.Names(facts.UserData, entity))
            .EnhancedInternalEdgeRemoval;

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ABodyRemovesInternalEdgesExactlyWhenItsShapeIsMadeOfSharpPieces(string shape, bool removes)
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        // A mesh is only allowed on a static body.
        BodyMotion motion = Shape(shape).ContainsMesh() ? BodyMotion.Static : BodyMotion.Dynamic;
        Entity body = world.Spawn(new() { Frame = space, Body = new(Shape: Shape(shape), Motion: motion) });
        world.Tick();

        RemovesInternalEdges(backend: backend, frame: space, entity: body, kind: BodyKind.Primary).Should().Be(removes);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Two ships at rest 50 m apart, each a hull with a bubble of 30 m, so that each has a ghost in the other's frame: one
    /// hull a compound of two boxes of convex radius 0, the other a box.
    /// </summary>
    [Fact]
    public void AGhostRemovesInternalEdgesExactlyWhenItsShapeIsMadeOfSharpPieces()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        Entity compound = world.Spawn(new()
        {
            Frame = space,
            Body = new(Shape: TwoSharpBoxes, Mass: 1000),
            AsFrame = new(FrameBounds.Sphere(30))
        });
        Entity box = world.Spawn(new()
        {
            Frame = space,
            Pose = At(50),
            Body = new(Shape: new BoxShapeDesc(new(2)), Mass: 1000),
            AsFrame = new(FrameBounds.Sphere(30))
        });
        Tick(world, 3);

        RemovesInternalEdges(backend: backend, frame: world.GetOwnedFrame(box), entity: compound, kind: BodyKind.Ghost)
            .Should()
            .BeTrue("the ghost's shape is the hull of sharp boxes");
        RemovesInternalEdges(backend: backend, frame: world.GetOwnedFrame(compound), entity: box, kind: BodyKind.Ghost)
            .Should()
            .BeFalse("the ghost's shape is the box hull");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ship whose hull is a box and whose interior is a compound of boxes of convex radius 0, and a ship whose hull is
    /// such a compound and whose interior is a box. Each body follows its own shape.
    /// </summary>
    [Fact]
    public void TheInteriorOfAFrameRemovesInternalEdgesByItsOwnShapeAndNotByTheHulls()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        Entity boxHull = world.Spawn(new()
        {
            Frame = space,
            Body = new(Shape: new BoxShapeDesc(new(2)), Mass: 1000, InteriorShape: TwoSharpBoxes),
            AsFrame = new(FrameBounds.Sphere(10))
        });
        Entity compoundHull = world.Spawn(new()
        {
            Frame = space,
            Pose = At(100),
            Body = new(Shape: TwoSharpBoxes, Mass: 1000, InteriorShape: new BoxShapeDesc(new(2))),
            AsFrame = new(FrameBounds.Sphere(10))
        });
        world.Tick();

        RemovesInternalEdges(backend: backend, frame: space, entity: boxHull, kind: BodyKind.Primary).Should()
            .BeFalse("the hull is a box");
        RemovesInternalEdges(backend: backend, frame: world.GetOwnedFrame(boxHull), entity: boxHull,
                kind: BodyKind.Interior).Should()
            .BeTrue("the interior is a compound");
        RemovesInternalEdges(backend: backend, frame: space, entity: compoundHull, kind: BodyKind.Primary).Should()
            .BeTrue("the hull is a compound");
        RemovesInternalEdges(backend: backend, frame: world.GetOwnedFrame(compoundHull), entity: compoundHull,
                kind: BodyKind.Interior).Should()
            .BeFalse("the interior is a box");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Spawns a dynamic raft of 100 kg, two <see cref="SharpPlank"/>s side by side along X whose tops meet flat at
    /// x = 0, on a static floor, and a ball of radius 0.25 m and 1 kg 10 cm above the raft's top, 10 mm from that joint,
    /// the raft first when <paramref name="raftFirst"/> is true and the ball first otherwise. Jolt gives each body the
    /// next free body ID as the backend creates it, in the order of the spawns. After 3 s, returns how far the ball's
    /// center has moved away from the joint, along X relative to the raft.
    /// </summary>
    private static double BallOnRaft(bool raftFirst)
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var raft = new CompoundShapeDesc(
        [
            new(SharpPlank, new(new(-0.5f, 0, 0), Quaternion.Identity)),
            new(SharpPlank, new(new(0.5f, 0, 0), Quaternion.Identity))
        ]);
        const double landing = 0.010;

        Entity SpawnRaft() => world.Spawn(new()
            { Frame = ground, Pose = At(0, 0.125), Body = new(Shape: raft, Mass: 100) });

        Entity SpawnBall() => world.Spawn(new()
            { Frame = ground, Pose = At(landing, 0.25 + 0.25 + 0.1), Body = Ball(0.25f) });

        Entity first = raftFirst ? SpawnRaft() : SpawnBall();
        Entity second = raftFirst ? SpawnBall() : SpawnRaft();
        (Entity raftBody, Entity ball) = raftFirst ? (first, second) : (second, first);
        Tick(world, 180);

        List<JoltPhysicsBackend.BodyFacts> facts = backend.BodiesIn(ground);
        uint IdOf(Entity entity) => facts.Single(body => BodyUserData.Names(body.UserData, entity)).Id.Index;
        (IdOf(first) < IdOf(second)).Should().BeTrue("Jolt gives the body spawned first the lower ID");
        world.Errors.Should().BeEmpty();
        return world.PoseIn(ball, ground).Position.X - world.PoseIn(raftBody, ground).Position.X - landing;
    }

    /// <summary>
    /// Of two dynamic bodies, Jolt puts the one with the lower body ID first in their pair, and its internal edge
    /// removal removes only the internal edges of the pair's second body (PhysicsSystem::ProcessBodyPair and
    /// InternalEdgeRemovingCollector, Jolt 5.6). Here the ball comes first, so the raft's edges at its joint are
    /// removed.
    /// </summary>
    [Fact]
    public void ADynamicCompoundRemovesItsInternalEdgesForADynamicBodyOfALowerJoltBodyId()
    {
        double side = BallOnRaft(raftFirst: false);

        side.Should().BeInRange(-Sideways, Sideways,
            "the raft is the pair's second body, so its flag removes the edge at its joint");
    }

    /// <summary>
    /// The raft comes first in the pair, so Jolt would remove the internal edges of the ball. It removes none, because
    /// the face of a sphere has fewer than three corners, so the ball meets the edge at the raft's joint and moves
    /// 14.7 mm away from it, though the raft has the flag.
    /// </summary>
    [Fact]
    public void ADynamicCompoundKeepsItsInternalEdgesForADynamicBodyOfAHigherJoltBodyId()
    {
        double side = BallOnRaft(raftFirst: true);

        side.Should().BeGreaterThan(0.005, "the raft is the pair's first body, and Jolt removes no edge of a sphere");
    }

    /// <summary>Walks its character along +X at <see cref="Speed"/>, in m/s, and stands it still while that is
    /// 0.</summary>
    private sealed class Walker : IComponent
    {
        public float Speed;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.Body.MoveCharacter(new(Speed, 0, 0));
    }

    /// <summary>
    /// Spawns <see cref="DefaultBoxFloor"/> under gravity of 9.81 m/s² along -Y and the demo's person, a character
    /// capsule of half height 0.6 m and radius 0.3 m and 70 kg, upright, 1 cm above the floor at <paramref name="x"/>.
    /// Physics sets the flag on every character. After the first tick, which creates the Jolt character, the reference
    /// run of a test, whose <paramref name="removesEdges"/> is false, clears it.
    /// </summary>
    private static Entity PersonOnGroove(World world, JoltPhysicsBackend backend, FrameHandle ground, double x,
        bool removesEdges, Walker walker)
    {
        world.Spawn(new() { Frame = ground, Body = new(Shape: DefaultBoxFloor, Motion: BodyMotion.Static) });
        Entity person = world.Spawn(new()
        {
            Frame = ground,
            Pose = At(x, PersonRadius + PersonHalfHeight + 0.01),
            Body = new(Shape: new CapsuleShapeDesc(PersonHalfHeight, PersonRadius), Motion: BodyMotion.Character,
                Mass: 70),
            Components = [walker]
        });
        world.Tick();
        lock (backend.Gate)
        {
            CharacterVirtual character = backend.CharacterOf(person) ??
                                         throw new InvalidOperationException("The person is not a walking character.");
            character.EnhancedInternalEdgeRemoval.Should().BeTrue("physics sets the flag on every character");
            if (!removesEdges)
                character.EnhancedInternalEdgeRemoval = false;
        }

        return person;
    }

    /// <summary>The height of the bottom of a person's capsule above the floor's flat top, from the pose of its
    /// center.</summary>
    private static double Lift(DPose pose) => pose.Position.Y - PersonHalfHeight - PersonRadius;

    /// <summary>Where a person ends 3 s after it was spawned above the middle of the groove, and how high: (x, lift),
    /// in m.</summary>
    private static (double X, double Lift) StandInGroove(bool removesEdges)
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Entity person = PersonOnGroove(world: world, backend: backend, ground: ground, x: 0, removesEdges: removesEdges,
            walker: new());
        Tick(world, 180);
        world.IsStanding(person).Should().BeTrue();
        world.Errors.Should().BeEmpty();
        DPose pose = world.PoseIn(person, ground);
        return (pose.Position.X, Lift(pose));
    }

    /// <summary>
    /// A Jolt character finds its contacts by a query of its own, which reads only the character's own flag, so physics
    /// sets the flag on every character, and it acts on every floor, a rounded groove included. In the middle of the
    /// groove, the character's query meets both rounded edges, and edge removal keeps only one of the two contacts. So
    /// the character moves 1.08 mm aside and rests 0.15 mm higher than it does with both, which it keeps without the
    /// flag. The engine accepts that: the character moves once and then stands, and a body in the rigid form of a
    /// character keeps both contacts, because neither it nor this floor sets the flag.
    /// </summary>
    [Fact]
    public void ACharacterThatStandsInTheMiddleOfAGrooveOfTwoDefaultBoxesKeepsOneContactAndMovesAboutAMillimeterAside()
    {
        (double x, double lift) = StandInGroove(removesEdges: true);
        (double xWithout, double liftWithout) = StandInGroove(removesEdges: false);

        Math.Abs(x).Should().BeInRange(0.0005, 0.002,
            "the character keeps the contact with one rounded edge and moves off the other");
        lift.Should().BeInRange(-GrooveDepth, -GrooveDepth + 0.0005);
        Math.Abs(xWithout).Should().BeLessThan(0.00001, "without the flag the character keeps both contacts");
        liftWithout.Should().BeApproximately(-GrooveDepth, 0.00005);
    }

    /// <summary>
    /// A person stands still for 1 s 1.5 m before the groove, and then walks at <paramref name="speed"/>, in m/s,
    /// across it to 1.5 m beyond it. Returns its lowest and its highest lift on the walk, in m, and its slowest speed
    /// along X, as a share of <paramref name="speed"/>.
    /// </summary>
    private static (double Lowest, double Highest, double Slowest) WalkAcrossGroove(bool removesEdges, float speed)
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        var walker = new Walker();
        Entity person = PersonOnGroove(world: world, backend: backend, ground: ground, x: -1.5,
            removesEdges: removesEdges, walker: walker);
        Tick(world, 59);
        walker.Speed = speed;
        double lowest = double.MaxValue, highest = double.MinValue, slowest = double.MaxValue;
        double last = world.PoseIn(person, ground).Position.X;
        for (int t = 0; t < 1000 && last < 1.5; t++)
        {
            world.Tick();
            DPose pose = world.PoseIn(person, ground);
            lowest = Math.Min(lowest, Lift(pose));
            highest = Math.Max(highest, Lift(pose));
            // The step of the walk's first tick does not know the walk yet.
            if (t > 0)
                slowest = Math.Min(slowest, (pose.Position.X - last) / TickSeconds / speed);
            last = pose.Position.X;
        }

        last.Should().BeGreaterThanOrEqualTo(1.5, "the person has crossed the groove");
        world.Errors.Should().BeEmpty();
        return (lowest, highest, slowest);
    }

    /// <summary>
    /// A person that walks across the groove passes its middle, where edge removal keeps one of the two contacts,
    /// within a tick. Its walk is the same as without the flag to 0.01 mm and 0.1 %: at 3 m/s it rises 3.30 mm as it
    /// leaves the groove, and at 1 m/s it sinks 3.39 mm into it, and it keeps within 2 % of its walking speed.
    /// </summary>
    [Theory]
    [InlineData(3f)]
    [InlineData(1f)]
    public void ACharacterThatWalksAcrossAGrooveOfTwoDefaultBoxesWalksAsWithoutEdgeRemoval(float speed)
    {
        (double lowest, double highest, double slowest) = WalkAcrossGroove(removesEdges: true, speed);
        (double lowestWithout, double highestWithout, double slowestWithout) =
            WalkAcrossGroove(removesEdges: false, speed);

        lowest.Should().BeApproximately(lowestWithout, 0.00001);
        highest.Should().BeApproximately(highestWithout, 0.00001);
        slowest.Should().BeApproximately(slowestWithout, 0.001);
        slowest.Should().BeGreaterThan(0.98, "the groove slows the walk by less than 2 %");
    }
}
