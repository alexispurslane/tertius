using System.Diagnostics;
using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.WorldTestingGrowth;

namespace Tertius.Physics.Tests;

/// <summary>
/// A system keeps its body pairs by body as well (ter-9sri), so that a body or a ghost that leaves the system visits only
/// its own pairs. The first tests pin the index on its own. The others run the World on the real backend and check, after
/// each kind of change to the pair tables, that the index holds exactly the pairs of the tables (see
/// <see cref="FrameWorld.AssertInvariants"/>), and that a removal ends exactly the contacts of the body that goes.
/// </summary>
public sealed class BodyPairIndexTests
{
    private static readonly BodyID A = new(1);
    private static readonly BodyID B = new(2);
    private static readonly BodyID C = new(3);

    private static List<ulong> PairsOf(BodyPairIndex index, BodyID body)
    {
        var pairs = new List<ulong>();
        index.CopyPairsOf(body, pairs);
        return pairs;
    }

    [Fact]
    public void APairIsListedUnderEachOfItsTwoBodies()
    {
        var index = new BodyPairIndex();
        ulong ab = FrameWorld.PairKey(A, B);

        index.Add(ab);

        PairsOf(index, A).Should().Equal(ab);
        PairsOf(index, B).Should().Equal(ab);
        index.Contains(ab).Should().BeTrue();
        index.Invoking(i => i.AssertInvariants()).Should().NotThrow();
    }

    [Fact]
    public void ABodyIsListedWithItsOwnPairsOnly()
    {
        var index = new BodyPairIndex();
        ulong ab = FrameWorld.PairKey(A, B);
        ulong ac = FrameWorld.PairKey(A, C);
        ulong bc = FrameWorld.PairKey(B, C);

        index.Add(ab);
        index.Add(ac);
        index.Add(bc);

        PairsOf(index, A).Should().BeEquivalentTo([ab, ac]);
        PairsOf(index, C).Should().BeEquivalentTo([ac, bc]);
        index.Count.Should().Be(3);
    }

    [Fact]
    public void ARemovedPairIsListedUnderNeitherOfItsBodies()
    {
        var index = new BodyPairIndex();
        ulong ab = FrameWorld.PairKey(A, B);
        ulong ac = FrameWorld.PairKey(A, C);
        index.Add(ab);
        index.Add(ac);

        index.Remove(ab);

        PairsOf(index, A).Should().Equal(ac);
        PairsOf(index, B).Should().BeEmpty();
        index.Contains(ab).Should().BeFalse();
        index.Count.Should().Be(1);
        index.Invoking(i => i.AssertInvariants()).Should().NotThrow();
    }

    [Fact]
    public void ClearingForgetsEveryPair()
    {
        var index = new BodyPairIndex();
        index.Add(FrameWorld.PairKey(A, B));
        index.Add(FrameWorld.PairKey(B, C));

        index.Clear();

        index.Count.Should().Be(0);
        PairsOf(index, B).Should().BeEmpty();
        index.Invoking(i => i.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>
    /// A pair that starts and ends again and again, such as a ball that bounces between two walls, must not allocate once
    /// each of its bodies has had a pair before.
    /// </summary>
    [Fact]
    public void PairsThatComeAndGoAgainAllocateNothing()
    {
        var index = new BodyPairIndex();
        ulong ab = FrameWorld.PairKey(A, B);
        ulong bc = FrameWorld.PairKey(B, C);
        var pairs = new List<ulong>(4);
        void churn()
        {
            for (int i = 0; i < 100; i++)
            {
                index.Add(ab);
                index.Add(bc);
                pairs.Clear();
                index.CopyPairsOf(B, pairs);
                index.Remove(ab);
                index.Remove(bc);
            }
        }
        churn();

        Allocations.Fewest(churn).Should().Be(0);
        index.Count.Should().Be(0);
    }

    /// <summary>
    /// A hub body loses its 100,000 pairs, and its empty set waits in the pool. The next body that gets a pair takes that
    /// set. It reads its one pair about as fast as a body with a new set: a read costs time in proportion to the pairs of
    /// the body, not to the pairs of the set's earlier owner.
    /// </summary>
    [Fact]
    public void ABodyThatTakesTheSetOfABodyWithManyPairsReadsItsPairsAsFastAsWithANewSet()
    {
        const uint Many = 100_000;
        var hub = new BodyID(Many + 1);
        var reused = new BodyPairIndex();
        for (uint i = 1; i <= Many; i++)
            reused.Add(FrameWorld.PairKey(new BodyID(i), hub));
        // The hub has the higher ID of each pair, so the last removal pools its set last, on top of the pool.
        for (uint i = 1; i <= Many; i++)
            reused.Remove(FrameWorld.PairKey(new BodyID(i), hub));
        var body = new BodyID(Many + 2);
        ulong pair = FrameWorld.PairKey(body, new BodyID(Many + 3));
        reused.Add(pair);
        var fresh = new BodyPairIndex();
        fresh.Add(pair);

        double reusedTime = FastestThousandReads(reused, body);
        double freshTime = FastestThousandReads(fresh, body);

        PairsOf(reused, body).Should().Equal(pair);
        reusedTime
            .Should()
            .BeLessThan(20 * freshTime, "a pooled set keeps no trace of the 100,000 pairs that it held before");
    }

    /// <summary>The fewest microseconds, of five runs, that 1,000 reads of the pairs of <paramref name="body"/> take.</summary>
    private static double FastestThousandReads(BodyPairIndex index, BodyID body)
    {
        var pairs = new List<ulong>(1);
        double fastest = double.MaxValue;
        for (int run = 0; run < 5; run++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int read = 0; read < 1000; read++)
            {
                pairs.Clear();
                index.CopyPairsOf(body, pairs);
            }

            fastest = Math.Min(fastest, Stopwatch.GetElapsedTime(start).TotalMicroseconds);
        }

        return fastest;
    }

#if TERTIUS_CONTRACTS
    /// <summary>Only a bug removes a pair that the index does not hold, so a build with contracts fails the precondition.</summary>
    [Fact]
    public void RemovingAPairThatTheIndexDoesNotHoldFailsAContract()
    {
        var index = new BodyPairIndex();
        index.Add(FrameWorld.PairKey(A, B));

        index
            .Invoking(i => i.Remove(FrameWorld.PairKey(A, C)))
            .Should()
            .Throw<ContractException>()
            .WithMessage("Precondition failed*");
    }
#else
    /// <summary>
    /// Only a bug removes a pair that the index does not hold. A Release build skips it, keeps the index as it was, and
    /// says so, so that the system records the bug (STYLE.md, "Errors and contracts").
    /// </summary>
    [Fact]
    public void RemovingAPairThatTheIndexDoesNotHoldChangesNothingInRelease()
    {
        var index = new BodyPairIndex();
        ulong ab = FrameWorld.PairKey(A, B);
        index.Add(ab);

        bool removed = index.Remove(FrameWorld.PairKey(A, C));

        removed.Should().BeFalse();
        PairsOf(index, A).Should().Equal(ab);
        index.Count.Should().Be(1);
        index.Invoking(i => i.AssertInvariants()).Should().NotThrow();
    }
#endif

    /// <summary>
    /// Three cubes stand side by side on the floor, each face against the next. The middle one is destroyed. Its contacts
    /// with the floor and with both neighbors end, and the contacts of the neighbors with the floor go on: neither ends nor
    /// starts again. A lock-in: the removal behaved so before the index.
    /// </summary>
    [Fact]
    public void DestroyingTheMiddleCubeOfARowEndsItsOwnContactsAndKeepsTheOthers()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        var floorLog = new CollisionLog();
        Entity floor = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, -0.5),
                Body = new(Shape: new BoxShapeDesc(new(50, 0.5f, 50)), Motion: BodyMotion.Static),
                Components = [floorLog],
            }
        );
        var leftLog = new CollisionLog();
        var rightLog = new CollisionLog();
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(-1, 0.5),
                Body = Cube(),
                Components = [leftLog],
            }
        );
        Entity middle = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, 0.5),
                Body = Cube(),
            }
        );
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(1, 0.5),
                Body = Cube(),
                Components = [rightLog],
            }
        );
        Tick(world, 60);
        leftLog.NewsOf(middle).Should().Equal(Started);
        rightLog.NewsOf(middle).Should().Equal(Started);

        world.Destroy(middle);
        Tick(world, 60);

        floorLog.NewsOf(middle).Should().Equal(Started, Ended);
        leftLog.NewsOf(middle).Should().Equal(Started, Ended);
        rightLog.NewsOf(middle).Should().Equal(Started, Ended);
        leftLog.NewsOf(floor).Should().Equal([Started], "the left cube still stands on the floor");
        rightLog.NewsOf(floor).Should().Equal([Started], "the right cube still stands on the floor");
        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A ball falls onto the floor. When their contact starts, the index holds the pair. A lock-in.</summary>
    [Fact]
    public void TheIndexHoldsThePairsOfTheTablesAfterAContactStarts()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Entity floor = Floor(world, ground);
        var log = new CollisionLog();
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, 1),
                Body = Ball(),
                Components = [log],
            }
        );

        TickUntil(world, () => log.CountWith(floor) == 1, 120);

        log.NewsOf(floor).Should().Equal(Started);
        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>
    /// With no gravity, a ball that does not lose speed in a bounce flies into a static wall and away again. When their
    /// contact ends, the index no longer holds the pair. A lock-in.
    /// </summary>
    [Fact]
    public void TheIndexHoldsThePairsOfTheTablesAfterAContactEnds()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = Ground(world, 0);
        Entity wall = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(2),
                Body = new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static, Restitution: 1),
            }
        );
        var log = new CollisionLog();
        world.Spawn(
            new()
            {
                Frame = space,
                LinearVelocity = new Vector3(3, 0, 0),
                Body = Ball() with { Restitution = 1 },
                Components = [log],
            }
        );

        TickUntil(world, () => log.Ended.Count == 1, 120);

        log.NewsOf(wall).Should().Equal(Started, Ended);
        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>
    /// With no gravity, a ball starts 1 cm from a static wall, within Jolt's speculative contact distance, and drifts away
    /// slowly. Jolt finds the pair without a touch, so it waits for its first touch, and then drops it. The pair leaves
    /// both tables without a report. A lock-in.
    /// </summary>
    [Fact]
    public void TheIndexHoldsThePairsOfTheTablesAfterAPairThatNeverTouchedGoes()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = Ground(world, 0);
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(1.01),
                Body = new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static),
            }
        );
        var log = new CollisionLog();
        world.Spawn(
            new()
            {
                Frame = space,
                LinearVelocity = new Vector3(-0.1f, 0, 0),
                Body = Ball(),
                Components = [log],
            }
        );
        Tick(world, 1);
        backend.PendingPairCount(space).Should().Be(1);

        TickUntil(world, () => backend.PendingPairCount(space) == 0, 60);

        backend.PendingPairCount(space).Should().Be(0, "Jolt dropped the pair once the ball was far enough");
        log.Seen.Should().BeEmpty();
        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>
    /// A character walks against a heavy box. Jolt never simulates the character's inner body, so the character's hit
    /// starts the pair as a held pair. A lock-in.
    /// </summary>
    [Fact]
    public void TheIndexHoldsThePairsOfTheTablesAfterACharacterHitsABody()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var boxLog = new CollisionLog();
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(1, 0.5),
                Body = new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 500),
                Components = [boxLog],
            }
        );
        var pilot = new Pilot { EveryTick = body => body.MoveCharacter(new(0.5f, 0, 0)) };
        Entity person = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, 0.95),
                Body = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70),
                Components = [pilot],
            }
        );

        TickUntil(world, () => boxLog.CountWith(person) == 1, 120);

        boxLog.NewsOf(person).Should().Equal(Started);
        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>
    /// With no gravity, two crates side by side touch and drift together into a frame's bubble. Their bodies leave the
    /// system of space, and the frame's system takes their contact over. A lock-in.
    /// </summary>
    [Fact]
    public void TheIndexHoldsThePairsOfTheTablesAfterASystemTakesAContactOver()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = Ground(world, 0);
        FrameHandle inside = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = At(20),
                    AsFrame = new(FrameBounds.Sphere(5)),
                }
            )
        );
        var log = new CollisionLog();
        Entity first = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(13, 0.5),
                LinearVelocity = new Vector3(2, 0, 0),
                Body = Cube(),
                Components = [log],
            }
        );
        Entity second = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(13, -0.5),
                LinearVelocity = new Vector3(2, 0, 0),
                Body = Cube(),
            }
        );

        TickUntil(world, () => world.GetFrame(first) == inside && world.GetFrame(second) == inside, 120);
        Tick(world, 1);

        log.NewsOf(second).Should().Equal(Started);
        world.GetFrame(second).Should().Be(inside);
        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>
    /// The cubes of a row on the floor rest against each other. The middle one is destroyed, so its body leaves the system
    /// and its pairs go. A lock-in.
    /// </summary>
    [Fact]
    public void TheIndexHoldsThePairsOfTheTablesAfterABodyLeaves()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var log = new CollisionLog();
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(-1, 0.5),
                Body = Cube(),
                Components = [log],
            }
        );
        Entity middle = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, 0.5),
                Body = Cube(),
            }
        );
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(1, 0.5),
                Body = Cube(),
            }
        );
        Tick(world, 60);

        world.Destroy(middle);
        Tick(world, 3);

        log.NewsOf(middle).Should().Equal(Started, Ended);
        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>
    /// With no gravity, a kinematic block in a frame of radius 10 m sticks 1 m out of the bubble, so space holds a ghost of
    /// it. A ball in space drifts into the ghost and stops there. The block is destroyed, so its ghost leaves the system of
    /// space with the pair. A lock-in.
    /// </summary>
    [Fact]
    public void TheIndexHoldsThePairsOfTheTablesAfterAGhostLeaves()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle bubble = world.GetOwnedFrame(
            world.Spawn(new() { Frame = space, AsFrame = new(FrameBounds.Sphere(10)) })
        );
        Entity block = world.Spawn(
            new()
            {
                Frame = bubble,
                Pose = At(10),
                Body = new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Kinematic),
            }
        );
        var log = new CollisionLog();
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(11.5),
                LinearVelocity = new Vector3(-0.5f, 0, 0),
                Body = Ball(radius: 0.1f) with { Restitution = 0 },
                Components = [log],
            }
        );
        TickUntil(world, () => log.CountWith(block) == 1, 120);
        Tick(world, 10);
        backend.HasGhostIn(block, space).Should().BeTrue();

        world.Destroy(block);
        Tick(world, 3);

        log.NewsOf(block).Should().Equal(Started, Ended);
        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A cube rests on the floor in a system of 8 bodies. Static crates that touch nothing fill the system, so it grows,
    /// and the new system holds the contact as a held pair under the cube's new ID. A lock-in.
    /// </summary>
    [Fact]
    public void TheIndexHoldsThePairsOfTheTablesAfterTheSystemGrows()
    {
        var warnings = new List<string>();
        using World world = NewWorldWithFaultsAndSmallSystems(
            maxBodiesPerFrame: 8,
            faults: out FaultInjectingBackend faults,
            warnings: warnings
        );
        FrameHandle ground = Ground(world, 9.81f);
        Entity floor = Floor(world, ground);
        var log = new CollisionLog();
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, 0.5),
                Body = Cube(),
                Components = [log],
            }
        );
        Tick(world, 10);

        for (int i = 0; i < 15; i++)
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = At(3 * i, 10),
                    Body = new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static),
                }
            );
        Tick(world, 10);

        warnings.Should().Contain(warning => warning.Contains("so it grew"));
        log.NewsOf(floor).Should().Equal([Started], "a growth ends no contact");
        faults.Jolt.Invoking(b => b.AssertInvariants()).Should().NotThrow();
    }
}
