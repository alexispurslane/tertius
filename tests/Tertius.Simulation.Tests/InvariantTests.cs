using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The world's invariant checks over a seeded workload that churns every subsystem at once, with an independent audit
/// after every tick; the switch that runs the checks at every sync point; and proof that the audit finds the damage
/// it is there to find. The workload and the audit are in <see cref="ChaoticWorkload"/> and
/// <see cref="InvariantCrossChecks"/>.
/// </summary>
public sealed class InvariantTests(ITestOutputHelper output)
{
    private const int FixedSeed = 20261010;

    // ---- The hull motion ------------------------------------------------------------------------------------------

    /// <summary>A triangle mesh, which only a static body or the interior of a frame can have.</summary>
    private static readonly MeshShapeDesc Ramp = new(
        [new(-5, 0, -5), new(5, 0, -5), new(5, 2, 5), new(-5, 2, 5)],
        [0, 2, 1, 0, 3, 2]
    );

    // ---- The invariants hold ---------------------------------------------------------------------------------------

    // Each seed is a different run: different spawns, different hand-offs, different deaths. Run fails with the seed in its message.
    [Theory]
    [InlineData(FixedSeed)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(31337)]
    public void EverySubsystemKeepsItsInvariantsAfterEveryTickOfAChaoticWorkload(int seed)
    {
        using var workload = new ChaoticWorkload(seed: seed);

        workload.Run(150);

        output.WriteLine($"seed {seed}: {workload.World.Stats}");
        output.WriteLine(workload.Tally.ToString());
        output.WriteLine(workload.History.ToString());
        workload
            .World.Errors.Where(error => error.Contains("Exception"))
            .Should()
            .BeEmpty($"seed {seed}: a component or the engine threw");
    }

    [Fact]
    public void TheChaoticWorkloadReachesEverySubsystemAndTheAuditLooksAtEverySet()
    {
        // An audit that looks at empty sets agrees with anything. This test requires that the workload fills every set
        // and every kind of change happens many times, so that "no violations" means something.
        using var workload = new ChaoticWorkload(seed: FixedSeed);

        workload.Run(150);

        WorkloadTally tally = workload.Tally;
        CrossCheckHistory audit = workload.History;
        output.WriteLine(tally.ToString());
        output.WriteLine(audit.ToString());
        workload.World.Stats.LiveEntities.Should().BeGreaterThan(20);
        tally.Spawns.Should().BeGreaterThan(50);
        tally.AttachedSpawns.Should().BeGreaterThan(10);
        tally.BodySpawns.Should().BeGreaterThan(20);
        tally.FrameSpawns.Should().BeGreaterThan(5);
        tally.Destroys.Should().BeGreaterThan(50);
        tally.DestroyMessages.Should().BeGreaterThan(20);
        tally.Subscriptions.Should().BeGreaterThan(50);
        tally.ComponentChanges.Should().BeGreaterThan(20);
        tally.Moves.Should().BeGreaterThan(100);
        tally.OffsetChanges.Should().BeGreaterThan(5);
        tally.HandOffs.Should().BeGreaterThan(10);
        tally.FrameStates.Should().BeGreaterThan(50);
        tally.Contacts.Should().BeGreaterThan(5);
        tally.OutsideSpawns.Should().BeGreaterThan(50);
        tally.OutsideDestroys.Should().BeGreaterThan(10);
        tally.OutsideFrameDestroys.Should().BeGreaterThan(2);
        audit.Checks.Should().Be(300, "the audit runs after the tick and after the changes between ticks, 150 times");
        audit.MemberEntries.Should().BeGreaterThan(1000);
        audit.RecipientEntries.Should().BeGreaterThan(1000);
        audit.AttachedLinks.Should().BeGreaterThan(100);
        audit.InboxEvents.Should().BeGreaterThan(1000);
        audit.TopicDeliveries.Should().BeGreaterThan(100);
        audit.SpatialMatches.Should().BeGreaterThan(1000);
        audit.DeadHandlesProbed.Should().BeGreaterThan(1000);
        audit.StaleFramesProbed.Should().BeGreaterThan(50);
    }

    [Fact]
    public void ARunThatFindsAViolationNamesTheSeedAndTheTick()
    {
        using var workload = new ChaoticWorkload(seed: 99);
        workload.Run(5);
        // A frame destroyed behind the world's back: its owner and its contents point at a frame that is gone.
        workload.World.Frames.Destroy(workload.West);

        Action run = () => workload.Run(1);

        run.Should().Throw<Exception>().Where(ex => ex.Message.Contains("seed 99"));
    }

    /// <summary>A frame 100 m along +X in the root of <paramref name="test"/>, with a static hull of <paramref name="hull"/>, or without a body.</summary>
    private static FrameHandle StaticFrame(TestWorld test, BodyDesc? hull) =>
        test.World.GetOwnedFrame(
            test.World.Spawn(
                new()
                {
                    Frame = test.Root,
                    Pose = TestWorld.At(100),
                    AsFrame = new(FrameBounds.Sphere(10)),
                    Body = hull,
                }
            )
        );

    [Theory]
    [InlineData("a dynamic motion for a hull without mass")]
    [InlineData("a kinematic motion for a mesh hull")]
    [InlineData("the character motion")]
    [InlineData("no motion")]
    public void AHullMotionThatTheHullCannotTakeBreaksTheWorldsInvariants(string why)
    {
        using var test = new TestWorld();
        bool mesh = why == "a kinematic motion for a mesh hull";
        FrameHandle dock = StaticFrame(
            test,
            new(Shape: mesh ? Ramp : new BoxShapeDesc(new(2)), Motion: BodyMotion.Static, Mass: 0)
        );
        test.World.AssertInvariants();

        test.World.Frames.SetHullMotion(
            dock,
            why switch
            {
                "a dynamic motion for a hull without mass" => BodyMotion.Dynamic,
                "a kinematic motion for a mesh hull" => BodyMotion.Kinematic,
                "the character motion" => BodyMotion.Character,
                _ => null,
            }
        );

        test.World.Invoking(w => w.AssertInvariants()).Should().Throw<ContractException>(why);
    }

    [Fact]
    public void AHullMotionOfAFrameWhoseOwnerHasNoBodyBreaksTheWorldsInvariants()
    {
        using var test = new TestWorld();
        FrameHandle room = StaticFrame(test, hull: null);
        test.World.AssertInvariants();

        test.World.Frames.SetHullMotion(room, BodyMotion.Static);

        test.World.Invoking(w => w.AssertInvariants()).Should().Throw<ContractException>();
    }

    // ---- DebugChecks -----------------------------------------------------------------------------------------------

    [Fact]
    public void DebugChecksCatchABrokenInvariantAtTheNextSyncPoint()
    {
        // Destroying a frame behind the world's back leaves its owner and its contents pointing at a dead frame.
        static World Broken(bool debugChecks)
        {
            World world = TestWorld.Create(root: out FrameHandle root, debugChecks: debugChecks);
            FrameHandle bay = world.SpawnStaticFrame(root, TestWorld.At(0), radius: 10);
            world.SpawnAt(bay, new(1, 0, 0));
            world.Tick();
            world.Frames.Destroy(bay);
            return world;
        }

        using World checkedWorld = Broken(debugChecks: true);
        checkedWorld.Invoking(w => w.AssertInvariants()).Should().Throw<ContractException>();
        checkedWorld.Invoking(w => w.Tick()).Should().Throw<ContractException>();
    }

    [Fact]
    public void WithDebugChecksOffTheSyncPointDoesNotRunTheChecks()
    {
        // The checks cost O(capacity) every tick, so the default leaves them off. The same damage then goes unnoticed by the tick.
        World world = TestWorld.Create(root: out FrameHandle root, debugChecks: false);
        using (world)
        {
            FrameHandle bay = world.SpawnStaticFrame(root, TestWorld.At(0), radius: 10);
            world.SpawnAt(bay, new(1, 0, 0));
            world.Tick();
            world.Frames.Destroy(bay);

            world.Invoking(w => w.Tick()).Should().NotThrow();
        }
    }

    // ---- The audit finds what it is there to find -------------------------------------------------------------------------

    private static Entity[] LiveEntitiesOf(World world)
    {
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        return [.. live];
    }

    private static List<FrameHandle> FramesOf(World world)
    {
        var frames = new List<FrameHandle>();
        world.Frames.GetAll(frames);
        return frames;
    }

    /// <summary>Puts an entity of one frame into the member set of another, the way a bug in the sync point could.</summary>
    private static (Entity Entity, FrameHandle Right, FrameHandle Wrong) MoveSomeEntityIntoTheWrongMemberSet(
        World world
    )
    {
        Entity victim = LiveEntitiesOf(world).First(e => !world.GetFrame(e).IsNull && world.GetOwnedFrame(e).IsNull);
        FrameHandle right = world.GetFrame(victim);
        FrameHandle wrong = FramesOf(world).First(f => f != right);
        Inside.TopicsOf(world).MoveMember(victim, wrong, out _).Should().BeTrue();
        return (victim, right, wrong);
    }

    private static ChaoticWorkload WarmedUp(int ticks = 40)
    {
        var workload = new ChaoticWorkload(seed: FixedSeed);
        workload.Run(ticks);
        // One more tick with nothing changed after it, so that the inboxes match the sets they were routed from.
        workload.TickOnce();
        workload.FindViolations(afterTick: true).Should().BeEmpty("the world is sound before it is damaged");
        return workload;
    }

    [Fact]
    public void TheAuditFindsAnEntityInTheMemberSetOfAFrameItIsNotIn()
    {
        using ChaoticWorkload workload = WarmedUp();
        (Entity victim, _, _) = MoveSomeEntityIntoTheWrongMemberSet(workload.World);

        List<string> problems = workload.FindViolations(afterTick: true);

        problems.Should().Contain(p => p.Contains(victim.ToString()) && p.Contains("member of"));
        workload
            .World.Invoking(w => w.AssertInvariants())
            .Should()
            .Throw<ContractException>("the topic sets check the same rule on their own");
    }

    [Fact]
    public void TheAuditFindsABackRowThatWasNeverCopiedForward()
    {
        using ChaoticWorkload workload = WarmedUp();
        World world = workload.World;
        Entity victim = LiveEntitiesOf(world).First(e => !world.GetFrame(e).IsNull && world.Entities.Body(e) == null);
        TransformRow row = world.Entities.BackRow(victim);
        row.Pose = row.Pose with { Position = row.Pose.Position + new Double3(5, 0, 0) };

        world.Entities.SetBackTransform(victim, row);

        workload
            .FindViolations(afterTick: true)
            .Should()
            .Contain(p => p.Contains(victim.ToString()) && p.Contains("front row"));
    }

    [Fact]
    public void TheAuditFindsAnAttachmentAcrossFrames()
    {
        using ChaoticWorkload workload = WarmedUp();
        World world = workload.World;
        Entity[] candidates =
        [
            .. LiveEntitiesOf(world).Where(e => !world.GetFrame(e).IsNull && world.Attachments.ParentOf(e).IsNull),
        ];
        Entity child = candidates[0];
        Entity parent = candidates.First(e => world.GetFrame(e) != world.GetFrame(child));

        // The world attaches only a new child, which it makes live in the frame that Attachments.CheckParent gives. Attaching
        // an entity that is live in another frame skips that, and that is the damage.
        world.Attachments.Attach(child, parent, Pose.Identity);

        workload
            .FindViolations(afterTick: true)
            .Should()
            .Contain(p => p.Contains(child.ToString()) && p.Contains("its parent"));
    }

    [Fact]
    public void TheAuditFindsMailThatWasRoutedToAnEntityThatNoLongerHearsTheTopic()
    {
        using ChaoticWorkload workload = WarmedUp();
        World world = workload.World;
        Topic chatter = Topics.Get("tests.invariants.chatter");
        TopicSets topics = Inside.TopicsOf(world);
        Router router = Inside.RouterOf(world);

        // Someone whose inbox holds a message to the chatter topic.
        Entity listener = default;
        var buffer = new EventBuffer();
        foreach (Entity candidate in LiveEntitiesOf(world))
        {
            EventList events = buffer.Begin(candidate);
            router.CopyInbox(candidate, events);
            for (int i = 0; i < events.Count && listener.IsNull; i++)
                if (events[i].Target.TryGetValue(out Topic topic) && topic == chatter)
                    listener = candidate;
            if (!listener.IsNull)
                break;
        }

        listener.IsNull.Should().BeFalse("the workload sends to the chatter topic every tick");

        topics.Unsubscribe(listener, chatter).Should().BeTrue();

        workload
            .FindViolations(afterTick: true)
            .Should()
            .Contain(p => p.Contains(listener.ToString()) && p.Contains("does not hear that topic"));
        workload
            .FindViolations(afterTick: false)
            .Should()
            .NotContain(
                p => p.Contains("does not hear that topic"),
                "after a change between ticks the inboxes still hold the old mail, so the audit does not compare them with the sets"
            );
    }

    [Fact]
    public void TheAuditFindsAnEntityThatTheSpatialIndexStillFilesInItsOldFrame()
    {
        using ChaoticWorkload workload = WarmedUp();
        World world = workload.World;
        Entity victim = LiveEntitiesOf(world)
            .First(e => !world.GetFrame(e).IsNull && world.Entities.Body(e) == null && world.GetOwnedFrame(e).IsNull);
        FrameHandle oldFrame = world.GetFrame(victim);
        FrameHandle otherFrame = FramesOf(world).First(f => f != oldFrame);

        // The rows move forward and the index is not updated: the step that the sync point must not skip.
        TransformRow row = world.Entities.BackRow(victim);
        row.Frame = otherFrame;
        world.Entities.SetBackTransform(victim, row);
        world.Entities.CopyDirtyRows();

        workload.FindViolations(afterTick: true).Should().Contain(p => p.Contains("A query over all of"));
    }
}
