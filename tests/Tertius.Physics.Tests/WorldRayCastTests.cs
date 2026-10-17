using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Ray casts from components, on the real World and Jolt. A component asks with <see cref="Self.Physics"/> during a
/// tick, the sync point casts the ray against Jolt and the snapshot of the end of that tick, and the answer, a
/// <see cref="RayHit"/>, arrives in the next tick. Every expected distance, point and normal comes from the geometry of
/// the scene, worked out in the test.
/// </summary>
public class WorldRayCastTests
{
    private const double Tolerance = 1e-4;

    /// <summary>A crew member, for the queries that filter by component.</summary>
    private sealed class Crew : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => default;

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
        }
    }

    /// <summary>One ray that a component asks for, on tick <see cref="OnTick"/>.</summary>
    private sealed record Ray(Double3 From, Double3 Direction, double Length, EntityQuery Query, int Tag, bool All = false, long OnTick = 0);

    /// <summary>Asks for its rays on their ticks, and keeps every RayHit that it hears, with the tick it heard it on.</summary>
    private sealed class Aim(params Ray[] rays) : IComponent
    {
        public List<(long Tick, RayHit Hit)> Answers { get; } = new();

        /// <summary>When set, the component asks for every ray on every tick.</summary>
        public bool EveryTick { get; init; }

        /// <summary>When cleared, the component counts the answers but keeps none, so that it allocates nothing itself.</summary>
        public bool Keeps { get; init; } = true;

        /// <summary>How many answers the component heard.</summary>
        public int Heard { get; private set; }

        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tertius.Tick>().And<RayHit>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref RayHit hit in events.Of<RayHit>())
            {
                Heard++;
                if (Keeps)
                    Answers.Add((self.Tick, hit));
            }
            foreach (Ray ray in rays)
            {
                if (!EveryTick && ray.OnTick != self.Tick)
                    continue;
                if (ray.All)
                    self.Physics.CastRayAll(ray.From, ray.Direction, ray.Length, ray.Query, ray.Tag);
                else
                    self.Physics.CastRay(ray.From, ray.Direction, ray.Length, ray.Query, ray.Tag);
            }
        }

        public RayHit[] AnswersTo(int tag) => Answers.Where(a => a.Hit.Tag == tag).Select(a => a.Hit).ToArray();
    }

    private static BodyDesc StaticBox(float half = 0.5f) => new(new BoxShapeDesc(new Vector3(half)), BodyMotion.Static);

    private static Entity Spawn(World world, FrameHandle frame, Double3 at, BodyDesc? body, params IComponent[] components) =>
        world.Spawn(new SpawnDesc { Frame = frame, Pose = new DPose(at, DQuaternion.Identity), Body = body, Components = components });

    private static void ShouldBeAt(RayHit hit, Entity entity, Double3 point, Vector3 normal, double distance)
    {
        hit.Entity.Should().Be(entity);
        (hit.Point - point).Length.Should().BeLessThan(Tolerance, $"the hit point {hit.Point} should be {point}");
        Vector3.Distance(hit.Normal, normal).Should().BeLessThan(1e-4f, $"the normal {hit.Normal} should be {normal}");
        ((double)hit.Distance).Should().BeApproximately(distance, Tolerance);
    }

    [Fact]
    public void ARayAnswersInTheNextTickWithTheNearestBodyItsPointItsNormalAndItsDistance()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 4));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity near = Spawn(world, ground, new Double3(10, 0, 0), StaticBox());
        Spawn(world, ground, new Double3(20, 0, 0), StaticBox());

        Tick(world, 3);

        aim.Answers.Should().ContainSingle();
        aim.Answers[0].Tick.Should().Be(1, "the ray asked on tick 0, and its answer arrives with the next tick");
        ShouldBeAt(aim.Answers[0].Hit, near, new Double3(9.5, 0, 0), -Vector3.UnitX, 9.5);
        aim.Answers[0].Hit.Tag.Should().Be(4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARayThatMeetsNothingAnswersWithAMissAtTheEndOfTheRay()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(new Double3(1, 2, 3), Double3.UnitY, 50, default, Tag: 9));
        Spawn(world, ground, new Double3(1, 2, 3), null, aim);
        Spawn(world, ground, new Double3(10, 0, 0), StaticBox());

        Tick(world, 2);

        RayHit miss = aim.AnswersTo(9).Should().ContainSingle().Subject;
        miss.IsHit.Should().BeFalse();
        miss.Entity.Should().Be(Entity.Null);
        (miss.Point - new Double3(1, 52, 3)).Length.Should().BeLessThan(1e-9);
        miss.Distance.Should().Be(50);
        miss.Normal.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void AQueryByComponentSkipsTheBodiesOfEntitiesWithoutTheComponent()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, EntityQuery.With<Crew>(), Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        Spawn(world, ground, new Double3(10, 0, 0), StaticBox());
        Entity crew = Spawn(world, ground, new Double3(20, 0, 0), StaticBox(), new Crew());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), crew, new Double3(19.5, 0, 0), -Vector3.UnitX, 19.5);
    }

    [Fact]
    public void AQueryByCollisionGroupSkipsTheBodiesOutsideTheGroup()
    {
        var registry = new CollisionGroupRegistry();
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, EntityQuery.InAny(CollisionGroup.Solid), Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        Spawn(world, ground, new Double3(10, 0, 0), StaticBox() with { CollisionGroups = CollisionGroups.Of(glass) });
        Entity wall = Spawn(world, ground, new Double3(20, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), wall, new Double3(19.5, 0, 0), -Vector3.UnitX, 19.5);
    }

    [Fact]
    public void ARaySkipsTheBodyOfTheEntityThatAsked()
    {
        // The ray starts inside the asker's own box, which Jolt treats as solid, so without the skip it would hit the
        // asker at once.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 1));
        Spawn(world, ground, Double3.Zero, StaticBox(2), aim);
        Entity target = Spawn(world, ground, new Double3(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), target, new Double3(9.5, 0, 0), -Vector3.UnitX, 9.5);
    }

    /// <summary>A closed cube of triangles with <paramref name="half"/> m from its center to each face, every face toward the outside.</summary>
    private static MeshShapeDesc MeshCube(float half)
    {
        Vector3[] v =
        [
            new(-half, -half, -half), new(half, -half, -half), new(half, half, -half), new(-half, half, -half),
            new(-half, -half, half), new(half, -half, half), new(half, half, half), new(-half, half, half),
        ];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2];
        return new MeshShapeDesc(v, faces);
    }

    [Fact]
    public void ARayThatStartsInsideAMeshHullPassesOutThroughItButARayFromOutsideHitsIt()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var inside = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 1));
        var outside = new Aim(new Ray(new Double3(-10, 0, 0), Double3.UnitX, 50, default, Tag: 2));
        Spawn(world, ground, Double3.Zero, null, inside);
        Spawn(world, ground, new Double3(-10, 0, 0), null, outside);
        Entity hull = Spawn(world, ground, Double3.Zero, new BodyDesc(MeshCube(3), BodyMotion.Static));
        Entity target = Spawn(world, ground, new Double3(10, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(inside.AnswersTo(1).Single(), target, new Double3(9.5, 0, 0), -Vector3.UnitX, 9.5);
        ShouldBeAt(outside.AnswersTo(2).Single(), hull, new Double3(-3, 0, 0), -Vector3.UnitX, 7);
    }

    [Fact]
    public void CastRayAllAnswersWithEveryEntityAlongTheRayByDistance()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 3, All: true));
        Spawn(world, ground, Double3.Zero, null, aim);
        // Spawned far to near, so that entity order is not distance order.
        Entity far = Spawn(world, ground, new Double3(30, 0, 0), StaticBox());
        Entity hull = Spawn(world, ground, new Double3(20, 0, 0), new BodyDesc(MeshCube(1), BodyMotion.Static));
        Entity near = Spawn(world, ground, new Double3(10, 0, 0), StaticBox());
        Spawn(world, ground, new Double3(60, 0, 0), StaticBox());

        Tick(world, 2);

        RayHit[] hits = aim.AnswersTo(3);
        hits.Select(h => h.Entity).Should().Equal(near, hull, far);
        ShouldBeAt(hits[0], near, new Double3(9.5, 0, 0), -Vector3.UnitX, 9.5);
        ShouldBeAt(hits[1], hull, new Double3(19, 0, 0), -Vector3.UnitX, 19);
        ShouldBeAt(hits[2], far, new Double3(29.5, 0, 0), -Vector3.UnitX, 29.5);
    }

    [Fact]
    public void CastRayAllWithNothingAlongTheRayAnswersWithOneMiss()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitY, 50, default, Tag: 3, All: true));
        Spawn(world, ground, Double3.Zero, null, aim);
        Spawn(world, ground, new Double3(10, 0, 0), StaticBox());

        Tick(world, 2);

        aim.AnswersTo(3).Should().ContainSingle().Which.IsHit.Should().BeFalse();
    }

    /// <summary>
    /// A static frame at (100, 0, 0) in <paramref name="parent"/>, turned by 90° about +Y, so that its +Z is the parent's
    /// +X, with a bubble of 20 m. Its owner has no body, so the parent's system holds nothing of it.
    /// </summary>
    private static FrameHandle TurnedChildFrame(World world, FrameHandle parent)
    {
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = parent,
            Pose = new DPose(new Double3(100, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Static),
        });
        return world.GetOwnedFrame(owner);
    }

    [Fact]
    public void ARayThatCrossesIntoTheBubbleOfAChildFrameHitsABodyInThatFrame()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        // At (0, 0, 5) in the child, which is (105, 0, 0) in the ground. Its face toward the ray is the child's -Z face.
        Entity inside = Spawn(world, child, new Double3(0, 0, 5), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), inside, new Double3(104.5, 0, 0), -Vector3.UnitX, 104.5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARayFromTheParentHitsABodyOfAChildFrameThatLiesJustOutsideTheChildsBubble()
    {
        // A static box of the turned child, 21 m along the child's -Z: (79, 0, 0) in the ground. Its near face, at x = 78.5,
        // is 1.5 m outside the bubble, which starts at x = 80. A static body never leaves its frame and has no ghost, so
        // only the child's system holds it.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity outside = Spawn(world, child, new Double3(0, 0, -21), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), outside, new Double3(78.5, 0, 0), -Vector3.UnitX, 78.5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CastRayAllReportsABodyOfAChildFrameThatStraddlesTheBubbleOnceAtItsNearFace()
    {
        // A static box with faces 1 m from its center, 20 m along the child's -Z: (80, 0, 0) in the ground, across the edge
        // of the bubble. Its near face is at x = 79, outside the bubble, and a ray that starts inside a box hits it at once.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 1, All: true));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity straddler = Spawn(world, child, new Double3(0, 0, -20), StaticBox(1));
        Entity inside = Spawn(world, child, new Double3(0, 0, 5), StaticBox());

        Tick(world, 2);

        RayHit[] hits = aim.AnswersTo(1);
        hits.Select(h => h.Entity).Should().Equal(straddler, inside);
        ShouldBeAt(hits[0], straddler, new Double3(79, 0, 0), -Vector3.UnitX, 79);
    }

    [Fact]
    public void AMovingBodyThatStraddlesTheBubbleIsInBothSystemsAndCastRayAllReportsItOnce()
    {
        // A cube at rest across the edge of the child's bubble, with no gravity: the child's system holds it, and from the
        // first step on the ground's system holds its ghost.
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 1, All: true, OnTick: 2));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity cube = Spawn(world, child, new Double3(0, 0, -20), Cube());

        Tick(world, 4);

        world.Snapshot.FrameOf(cube).Should().Be(child);
        backend.BodiesIn(ground).Should().Contain(f => BodyUserData.Kind(f.UserData) == BodyKind.Ghost && BodyUserData.Names(f.UserData, cube),
            "the ground must hold a ghost of the cube for this test to mean anything");
        RayHit hit = aim.AnswersTo(1).Should().ContainSingle().Subject;
        ShouldBeAt(hit, cube, new Double3(79.5, 0, 0), -Vector3.UnitX, 79.5);
    }

    [Fact]
    public void ARayFromAFrameHitsABodyOfASiblingFrameThatLiesInsideTheBubbleTheRayStartsIn()
    {
        // Two frames side by side in the ground: the asker's at (100, 0, 0), with its bubble from x = 80 to x = 120, and a
        // sibling at (150, 0, 0). The sibling's static box at (-35, 0, 0) in the sibling is (115, 0, 0) in the ground,
        // inside the asker's bubble, and its near face is 14.5 m from the asker.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle home = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = ground, Pose = At(100), AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Static),
        }));
        FrameHandle sibling = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = ground, Pose = At(150), AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Static),
        }));
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 100, default, Tag: 1));
        Spawn(world, home, Double3.Zero, null, aim);
        Entity box = Spawn(world, sibling, new Double3(-35, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), box, new Double3(14.5, 0, 0), -Vector3.UnitX, 14.5);
    }

    [Fact]
    public void ARayThatLeavesTheBubbleOfItsFrameHitsABodyInTheParentFrameInTheAxesOfTheAsker()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        // From the child's origin along its +Z, which is the ground's +X.
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitZ, 100, default, Tag: 1));
        Spawn(world, child, Double3.Zero, null, aim);
        Entity outside = Spawn(world, ground, new Double3(150, 0, 0), StaticBox());

        Tick(world, 2);

        // The face at x = 149.5 in the ground is 49.5 m along the child's +Z, and its normal is the child's -Z.
        ShouldBeAt(aim.AnswersTo(1).Single(), outside, new Double3(0, 0, 49.5), -Vector3.UnitZ, 49.5);
    }

    [Fact]
    public void CastRayAllMergesTheHitsOfEveryFrameItCrossesByDistanceWithEachEntityOnce()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        // A turned frame at (100, 0, 0) whose owner's static hull is a cube with faces 2 m from its center. Its interior is
        // the same cube in the frame's own system, so the ray meets the owner in both systems at the same face.
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(100, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2)),
            Body = StaticBox(2),
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Static),
        });
        FrameHandle child = world.GetOwnedFrame(owner);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 200, default, Tag: 5, All: true));
        Spawn(world, ground, Double3.Zero, null, aim);
        Entity near = Spawn(world, ground, new Double3(50, 0, 0), StaticBox());
        Entity far = Spawn(world, ground, new Double3(150, 0, 0), StaticBox());
        // At (0, 0, 5) in the child: (105, 0, 0) in the ground, between the hull and the far box.
        Entity inside = Spawn(world, child, new Double3(0, 0, 5), StaticBox());

        Tick(world, 2);

        RayHit[] hits = aim.AnswersTo(5);
        hits.Select(h => h.Entity).Should().Equal(near, owner, inside, far);
        ShouldBeAt(hits[1], owner, new Double3(98, 0, 0), -Vector3.UnitX, 98);
        ShouldBeAt(hits[2], inside, new Double3(104.5, 0, 0), -Vector3.UnitX, 104.5);
        ShouldBeAt(hits[3], far, new Double3(149.5, 0, 0), -Vector3.UnitX, 149.5);
    }

    [Fact]
    public void ARayFromInsideAShipPassesOutThroughItsSolidHullToTheBodiesBeyond()
    {
        // The hull is a solid box in the ground's system, and the ray starts inside it. The ground's part of the ray starts
        // where the ray leaves the ship's bubble, so the hull does not stop it.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, ground);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 100, default, Tag: 1));
        Spawn(world, ship.Interior, Double3.Zero, null, aim);
        Entity beyond = Spawn(world, ground, new Double3(80, 0, 0), StaticBox());

        Tick(world, 2);

        ShouldBeAt(aim.AnswersTo(1).Single(), beyond, new Double3(79.5, 0, 0), -Vector3.UnitX, 79.5);
    }

    /// <summary>Destroys its own entity on tick <paramref name="onTick"/>.</summary>
    private sealed class DestroySelf(long onTick) : IComponent
    {
        public int Priority => 1;
        public EventMask Wants => EventMask.Of<Tertius.Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick == onTick)
                self.Destroy();
        }
    }

    [Fact]
    public void AnAskerThatIsDestroyedInTheTickItAsksGetsNoAnswerAndCausesNoError()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 1));
        Spawn(world, ground, Double3.Zero, null, aim, new DestroySelf(0));
        Spawn(world, ground, new Double3(10, 0, 0), StaticBox());

        Tick(world, 3);

        aim.Answers.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARayWhoseFrameDiesInTheTickItAsksAnswersWithAMissAtAZeroPoint()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(100, 0, 0), DQuaternion.Identity),
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Static),
            Components = [new DestroySelf(0)],
        });
        FrameHandle child = world.GetOwnedFrame(owner);
        var aim = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, default, Tag: 1));
        Entity asker = Spawn(world, child, Double3.Zero, null, aim);
        Spawn(world, child, new Double3(10, 0, 0), StaticBox());

        Tick(world, 3);

        world.Snapshot.FrameOf(asker).Should().Be(ground, "the asker moved up when its frame died");
        RayHit miss = aim.AnswersTo(1).Should().ContainSingle().Subject;
        miss.IsHit.Should().BeFalse();
        miss.Point.Should().Be(Double3.Zero);
        miss.Distance.Should().Be(50);
    }

    [Fact]
    public void AnAnswerArrivesInTheFrameThatTheAskerIsInWhenItArrives()
    {
        // A ball asks on tick 2, from the ground, and physics hands it into the turned child frame in the step of that tick.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        var aim = new Aim(new Ray(new Double3(0, 10, 0), Double3.UnitX, 100, default, Tag: 1, OnTick: 2));
        Entity ball = Scenes.BallApproachingBubble(world, ground, child, 2, aim);
        Entity box = Spawn(world, ground, new Double3(50, 10, 0), StaticBox());

        Tick(world, 4);

        world.Snapshot.FrameOf(ball).Should().Be(child);
        aim.Answers.Should().ContainSingle().Which.Tick.Should().Be(3);
        // The face at (49.5, 10, 0) in the ground is (0, 10, -50.5) in the child, and the ground's -X is the child's -Z.
        ShouldBeAt(aim.Answers[0].Hit, box, new Double3(0, 10, -50.5), -Vector3.UnitZ, 49.5);
    }

    private sealed class BadRay(Double3 from, Double3 direction, double length) : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tertius.Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.Physics.CastRay(from, direction, length, default, 1);
    }

    [Theory]
    [InlineData(double.NaN, 1, 10, "origin")]
    [InlineData(0, 0, 10, "direction")]
    [InlineData(0, 1, 0, "length")]
    [InlineData(0, 1, double.PositiveInfinity, "length")]
    public void ARayWithAnOriginDirectionOrLengthThatIsNotUsableIsRefusedAtOnce(double x, double dx, double length, string what)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        var aim = new Aim();
        Spawn(world, ground, Double3.Zero, null, aim, new BadRay(new Double3(x, 0, 0), new Double3(dx, 0, 0), length));

        Tick(world, 2);

        world.Errors.Should().Contain(e => e.Contains("BadRay") && e.Contains(what));
        aim.Answers.Should().BeEmpty();
    }

    [Fact]
    public void TheOwnerOfARootFrameIsInNoFrameAndCannotCastARay()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        world.AddComponent(ground.Entity, new BadRay(Double3.Zero, Double3.UnitX, 10));

        Tick(world, 1);

        world.Errors.Should().Contain(e => e.Contains("BadRay") && e.Contains("no frame"));
    }

    [Fact]
    public void AQueryWhosePredicateThrowsReportsTheErrorAndGivesNoAnswerWhileTheWorldGoesOn()
    {
        // Jolt calls the filter from native code, which an exception must never cross.
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        EntityQuery throws = EntityQuery.Where(static (_, _) => throw new InvalidOperationException("the predicate broke"));
        var broken = new Aim(new Ray(Double3.Zero, Double3.UnitX, 50, throws, Tag: 1));
        var fine = new Aim(new Ray(new Double3(0, 0.1, 0), Double3.UnitX, 50, default, Tag: 2));
        Spawn(world, ground, Double3.Zero, null, broken);
        Spawn(world, ground, new Double3(0, 0.1, 0), null, fine);
        Entity box = Spawn(world, ground, new Double3(10, 0, 0), StaticBox());

        Tick(world, 3);

        world.Errors.Should().ContainSingle().Which.Should().Contain("the predicate broke");
        broken.Answers.Should().BeEmpty();
        fine.AnswersTo(2).Should().ContainSingle().Which.Entity.Should().Be(box);
    }

    [Fact]
    public void ARayInAFrameHitsABodyThatPhysicsKeepsInANumericFrameAndAnswersInTheSemanticFrame()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 0);
        // A crate at rest 20 km out moves into a resting numeric frame of its own.
        Entity crate = Spawn(world, ground, new Double3(20_000, 0, 0), Cube());
        var aim = new Aim(new Ray(new Double3(19_990, 0, 0), Double3.UnitX, 50, default, Tag: 1, OnTick: 3));
        Spawn(world, ground, new Double3(19_990, 0, 0), null, aim);

        Tick(world, 5);

        world.Frames.IsNumeric(world.PhysicsHomeOf(crate)).Should().BeTrue("the crate must be in a numeric frame for this test to mean anything");
        ShouldBeAt(aim.AnswersTo(1).Single(), crate, new Double3(19_999.5, 0, 0), -Vector3.UnitX, 9.5);
    }

    /// <summary>
    /// A scene with bodies in a frame and in a child frame, and eight askers that each cast both kinds of ray every tick.
    /// Returns every answer that they heard, in the order heard, as raw bits.
    /// </summary>
    private static List<string> AnswersOfASceneOfMany(int maxDegreeOfParallelism, int ticks)
    {
        using World world = NewWorld(out _, configure: options => options.MaxDegreeOfParallelism = maxDegreeOfParallelism);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        for (int i = 0; i < 24; i++)
            Spawn(world, ground, new Double3(5 + 7 * i, (i % 5) - 2, (i % 3) - 1), StaticBox(0.5f + 0.1f * (i % 4)), i % 2 == 0 ? [new Crew()] : []);
        for (int i = 0; i < 6; i++)
            Spawn(world, child, new Double3(i - 3, 0, 3 * i - 8), StaticBox(), new Crew());
        var askers = new List<Aim>();
        for (int i = 0; i < 8; i++)
        {
            var from = new Double3(0, (i % 5) - 2, (i % 3) - 1);
            Double3 direction = new Double3(1, 0.002 * i, -0.001 * i).Normalized();
            var aim = new Aim(
                new Ray(from, direction, 300, default, Tag: 1),
                new Ray(from, direction, 300, EntityQuery.With<Crew>(), Tag: 2, All: true)) { EveryTick = true };
            askers.Add(aim);
            Spawn(world, ground, from, StaticBox(0.25f), aim);
        }

        Tick(world, ticks);

        var heard = new List<string>();
        foreach (Aim aim in askers)
            foreach ((long tick, RayHit hit) in aim.Answers)
                heard.Add($"{tick} {hit.Tag} {hit.Entity} {BitConverter.DoubleToInt64Bits(hit.Point.X)} {BitConverter.DoubleToInt64Bits(hit.Point.Y)} " +
                          $"{BitConverter.DoubleToInt64Bits(hit.Point.Z)} {BitConverter.SingleToInt32Bits(hit.Distance)} {hit.Normal.X:R} {hit.Normal.Y:R} {hit.Normal.Z:R}");
        return heard;
    }

    [Fact]
    public void TheSameWorldGivesTheSameHitsInEveryRun()
    {
        List<string> first = AnswersOfASceneOfMany(1, 6);
        List<string> second = AnswersOfASceneOfMany(1, 6);
        List<string> parallel = AnswersOfASceneOfMany(4, 6);

        first.Should().HaveCountGreaterThan(8 * 5 * 2, "every asker hears at least one answer to each ray on each tick after the first");
        first.Where(line => !line.Contains("Entity(null)")).Should().NotBeEmpty();
        second.Should().Equal(first);
        parallel.Should().Equal(first);
    }

    [Fact]
    public void ARayRequestAllocatesNothingOnceTheWorldIsWarm()
    {
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle ground = Ground(world, 0);
        FrameHandle child = TurnedChildFrame(world, ground);
        for (int i = 0; i < 16; i++)
            Spawn(world, ground, new Double3(5 + 9 * i, 0, 0), StaticBox(), new Crew());
        Spawn(world, child, new Double3(0, 0, 5), StaticBox(), new Crew());
        const int Askers = 8;
        var askers = new List<Aim>();
        for (int i = 0; i < Askers; i++)
        {
            var from = new Double3(0, 0.01 * i, 0);
            var aim = new Aim(
                new Ray(from, Double3.UnitX, 300, EntityQuery.With<Crew>().Near(500), Tag: 1),
                new Ray(from, Double3.UnitX, 300, default, Tag: 2, All: true)) { EveryTick = true, Keeps = false };
            askers.Add(aim);
            Spawn(world, ground, from, null, aim);
        }
        Tick(world, 60);

        // Each window of the measure collects garbage every 10 ticks: an object that a cast needs and that only a weak
        // reference holds would be made again after each collection. An allocation in the cast shows in both windows. The
        // runtime can compile a hot method again during one window, and that one compilation lands on this thread, but in
        // one window at most, so the test takes the smaller window.
        const int Ticks = 200;
        int heardBefore = askers.Sum(a => a.Heard);
        long first = BytesPerRequest(world, Ticks, Askers * 2);
        long second = BytesPerRequest(world, Ticks, Askers * 2);

        Math.Min(first, second).Should().Be(0, $"a ray request must not allocate once the world is warm (the windows gave {first} and {second} bytes per request)");
        // One nearest answer and 17 hits of the other ray, for each asker on each tick: the measure covers real casts.
        (askers.Sum(a => a.Heard) - heardBefore).Should().Be(2 * Ticks * Askers * 18);
        world.Errors.Should().BeEmpty();
    }

    private static long BytesPerRequest(World world, int ticks, int requestsPerTick)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 1; t <= ticks; t++)
        {
            world.Tick();
            if (t % 10 == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        return (GC.GetAllocatedBytesForCurrentThread() - before) / (ticks * requestsPerTick);
    }
}
