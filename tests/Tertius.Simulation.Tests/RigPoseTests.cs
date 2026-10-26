using System.Collections.Immutable;
using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// An entity's rig pose is its state: it writes the pose of its own joints and the weights of its own morphs in its tick,
/// the sync point publishes them with its render row, and each render snapshot carries the rig pose of the tick and of the
/// build before (docs/plans/skinning-and-morphs.md, "The rig pose in the simulation" and "The rig pose in the snapshot").
/// </summary>
public sealed class RigPoseTests
{
    private const int Joints = 3;
    private const int Weights = 2;

    /// <summary>The joint pose that <see cref="TickPoser"/> writes for <paramref name="joint"/> in the tick <paramref name="tick"/>.</summary>
    private static Pose JointAt(long tick, int joint) =>
        new(new(tick, joint, 0.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, tick * 0.01f + joint));

    /// <summary>The weight that <see cref="TickPoser"/> writes for <paramref name="morph"/> in the tick <paramref name="tick"/>.</summary>
    private static float WeightAt(long tick, int morph) => tick + 0.25f * morph;

    /// <summary>The rig pose that <see cref="TickPoser"/> writes in the tick <paramref name="tick"/>.</summary>
    private static RigPose PoseAt(long tick, int joints = Joints, int weights = Weights) =>
        new(
            [.. Enumerable.Range(0, joints).Select(joint => JointAt(tick, joint))],
            [.. Enumerable.Range(0, weights).Select(morph => WeightAt(tick, morph))]
        );

    /// <summary>A rig pose of three joints and two weights that no tick of <see cref="TickPoser"/> writes.</summary>
    private static RigPose SpawnPose(float seed = 7) =>
        new(
            [
                new(new(seed, 0, 0), Quaternion.Identity),
                new(new(0, seed, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f)),
                new(new(0, 0, seed), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.25f)),
            ],
            [seed / 10, -seed]
        );

    /// <summary>Writes <see cref="PoseAt"/> of the tick into its entity's rig pose, in every tick, and notes the tick.</summary>
    private sealed class TickPoser : IComponent
    {
        public long LastTick = -1;
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            RigPoseWriter pose = self.RigPose;
            for (int joint = 0; joint < pose.Joints.Length; joint++)
                pose.Joints[joint] = JointAt(self.Tick, joint);
            for (int morph = 0; morph < pose.Weights.Length; morph++)
                pose.Weights[morph] = WeightAt(self.Tick, morph);
            LastTick = self.Tick;
        }
    }

    /// <summary>Spawns a visible entity with <paramref name="pose"/> at the origin of the root.</summary>
    private static Entity SpawnPosed(TestWorld test, RigPose pose, params IComponent[] components) =>
        test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Render = Visible(),
                RigPose = pose,
                Components = components,
            }
        );

    /// <summary>The one entry of <paramref name="snapshot"/> for the instance of <paramref name="entity"/>.</summary>
    private static RenderRigPose EntryOf(RenderSnapshot snapshot, Entity entity) =>
        snapshot
            .RigPoses.Should()
            .ContainSingle(pose => snapshot.Instances[pose.Instance].EntityIndex == entity.Index)
            .Subject;

    [Fact]
    public void SnapshotCarriesTheSpawnRigPoseOfANewEntity()
    {
        using var test = new TestWorld();
        RigPose spawn = SpawnPose();
        Entity posed = SpawnPosed(test, spawn);
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        RenderRigPose entry = EntryOf(snapshot, posed);
        snapshot.JointsOf(entry).ToArray().Should().Equal(spawn.Joints);
        snapshot.WeightsOf(entry).ToArray().Should().Equal(spawn.Weights);
    }

    [Fact]
    public void RigPoseWrittenInATickReachesTheNextSnapshot()
    {
        using var test = new TestWorld();
        var poser = new TickPoser();
        Entity posed = SpawnPosed(test, SpawnPose(), poser);
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));

        test.Run(2);

        RenderSnapshot snapshot = test.World.Latest();
        RenderRigPose entry = EntryOf(snapshot, posed);
        RigPose written = PoseAt(poser.LastTick);
        snapshot.JointsOf(entry).ToArray().Should().Equal(written.Joints);
        snapshot.WeightsOf(entry).ToArray().Should().Equal(written.Weights);
    }

    [Fact]
    public void SnapshotCarriesTheRigPoseOfTheBuildBeforeAsThePrevious()
    {
        using var test = new TestWorld();
        var poser = new TickPoser();
        Entity posed = SpawnPosed(test, SpawnPose(), poser);
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));

        test.Run(3);

        RenderSnapshot snapshot = test.World.Latest();
        RenderRigPose entry = EntryOf(snapshot, posed);
        RigPose before = PoseAt(poser.LastTick - 1);
        snapshot.PreviousJointsOf(entry).ToArray().Should().Equal(before.Joints);
        snapshot.PreviousWeightsOf(entry).ToArray().Should().Equal(before.Weights);
    }

    [Fact]
    public void FirstSnapshotOfAnEntityHasEqualCurrentAndPreviousRigPoses()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));
        test.Run(2);
        test.World.Latest();
        var poser = new TickPoser();
        Entity newcomer = SpawnPosed(test, SpawnPose(), poser);

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        RenderRigPose entry = EntryOf(snapshot, newcomer);
        poser.LastTick.Should().BeGreaterThanOrEqualTo(0, "the newcomer wrote a rig pose other than its spawn pose");
        snapshot.PreviousJointsOf(entry).ToArray().Should().Equal(snapshot.JointsOf(entry).ToArray());
        snapshot.PreviousWeightsOf(entry).ToArray().Should().Equal(snapshot.WeightsOf(entry).ToArray());
    }

    [Fact]
    public void FirstSnapshotOfAnEntityInTheBlockOfADeadOneHasNoPreviousRigPoseOfTheDeadOne()
    {
        using var test = new TestWorld();
        Entity dead = SpawnPosed(test, SpawnPose(seed: 1));
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));
        test.Run(2);
        test.World.Latest();
        test.World.Destroy(dead);
        var poser = new TickPoser();
        // Of the shape of the dead entity, so it takes the block that the dead one returned to the pool, which the last
        // build placed.
        Entity newcomer = SpawnPosed(test, SpawnPose(seed: 2), poser);

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        RenderRigPose entry = EntryOf(snapshot, newcomer);
        RigPose written = PoseAt(poser.LastTick);
        snapshot.PreviousJointsOf(entry).ToArray().Should().Equal(written.Joints);
        snapshot.PreviousWeightsOf(entry).ToArray().Should().Equal(written.Weights);
    }

    [Fact]
    public void PreviousRigPoseIsTheCurrentWithRenderInterpolationOff()
    {
        using var test = new TestWorld();
        var poser = new TickPoser();
        Entity posed = SpawnPosed(test, SpawnPose(), poser);
        Entity first = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));
        Entity second = test.SpawnCamera(test.Root, TestWorld.At(0, 5, 10));
        test.World.Camera = first;
        test.Run(2);
        test.World.Latest();

        test.World.Camera = second;
        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        RenderRigPose entry = EntryOf(snapshot, posed);
        snapshot.JointsOf(entry).ToArray().Should().Equal(PoseAt(poser.LastTick).Joints);
        snapshot.PreviousJointsOf(entry).ToArray().Should().Equal(snapshot.JointsOf(entry).ToArray());
        snapshot.PreviousWeightsOf(entry).ToArray().Should().Equal(snapshot.WeightsOf(entry).ToArray());
    }

    [Fact]
    public void RigPoseOfAnEntityThatWritesNothingStaysAsItWas()
    {
        using var test = new TestWorld();
        RigPose written = SpawnPose(seed: 3);
        // Writes its rig pose once, in its first tick, and never again.
        bool wrote = false;
        var once = Lambda.OnTick(
            (self, scoped ref _, scoped ref _) =>
            {
                if (wrote)
                    return;
                wrote = true;
                RigPoseWriter pose = self.RigPose;
                written.Joints.AsSpan().CopyTo(pose.Joints);
                written.Weights.AsSpan().CopyTo(pose.Weights);
            }
        );
        Entity posed = SpawnPosed(test, SpawnPose(), once);
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));

        test.Run(4);

        RenderSnapshot snapshot = test.World.Latest();
        RenderRigPose entry = EntryOf(snapshot, posed);
        snapshot.JointsOf(entry).ToArray().Should().Equal(written.Joints);
        snapshot.WeightsOf(entry).ToArray().Should().Equal(written.Weights);
        snapshot.PreviousJointsOf(entry).ToArray().Should().Equal(written.Joints);
        snapshot.PreviousWeightsOf(entry).ToArray().Should().Equal(written.Weights);
    }

    [Fact]
    public void RigPoseEntriesFollowTheOrderOfTheInstances()
    {
        using var test = new TestWorld();
        var posed = new List<(Entity Entity, RigPose Pose)>();
        for (int i = 0; i < 8; i++)
        {
            if (i % 3 == 1)
            {
                test.Show(test.Root, TestWorld.At(i));
                continue;
            }

            RigPose pose = SpawnPose(seed: i + 1);
            posed.Add((SpawnPosed(test, pose), pose));
        }

        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        snapshot
            .RigPoses.Select(entry => snapshot.Instances[entry.Instance].EntityIndex)
            .Should()
            .Equal(posed.Select(each => each.Entity.Index));
        foreach ((RenderRigPose entry, (Entity _, RigPose pose)) in snapshot.RigPoses.Zip(posed))
            snapshot.JointsOf(entry).ToArray().Should().Equal(pose.Joints);
    }

    [Fact]
    public void EntityWithoutARigPoseHasNoEntry()
    {
        using var test = new TestWorld();
        Entity plain = test.Show(test.Root, TestWorld.At(1));
        Entity posed = SpawnPosed(test, SpawnPose());
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.RigPoses.Should().ContainSingle();
        snapshot.Instances[snapshot.RigPoses[0].Instance].EntityIndex.Should().Be(posed.Index);
        snapshot.Instances.Should().Contain(instance => instance.EntityIndex == plain.Index);
    }

    [Fact]
    public void SelfRigPoseThrowsForAnEntityWithoutARigPose()
    {
        using var test = new TestWorld();
        Exception? thrown = null;
        test.SpawnWith(
            Lambda.OnTick(
                (self, scoped ref _, scoped ref _) =>
                {
                    try
                    {
                        _ = self.RigPose;
                    }
                    catch (Exception e)
                    {
                        thrown = e;
                    }
                }
            )
        );

        test.Run(1);

        thrown.Should().BeOfType<InvalidOperationException>();
    }

    public static TheoryData<string, RigPose> PosesThatAreNotFinite =>
        new()
        {
            { "a joint position of NaN", new([new(new(float.NaN, 0, 0), Quaternion.Identity)], []) },
            { "an infinite rotation", new([new(Vector3.Zero, new(x: float.PositiveInfinity, y: 0, z: 0, w: 1))], []) },
            { "an infinite weight", new([Pose.Identity], [float.NegativeInfinity]) },
            { "a weight of NaN", new([], [float.NaN]) },
        };

    [Theory]
    [MemberData(nameof(PosesThatAreNotFinite))]
    public void SpawnRefusesARigPoseThatIsNotFinite(string what, RigPose pose)
    {
        using var test = new TestWorld();
        int live = test.World.Stats.LiveEntities;

        Action spawn = () => SpawnPosed(test, pose);

        spawn.Should().Throw<ArgumentException>(what);
        test.World.Stats.LiveEntities.Should().Be(live, "the spawn changed nothing");
    }

    [Fact]
    public void SpawnRefusesARigPoseWithAZeroRotation()
    {
        using var test = new TestWorld();
        var pose = new RigPose([Pose.Identity, new(Vector3.One, default)], []);

        Action spawn = () => SpawnPosed(test, pose);

        spawn.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SpawnRefusesARigPoseOfMoreJointsThanARigHas()
    {
        using var test = new TestWorld();
        var pose = new RigPose([.. Enumerable.Repeat(Pose.Identity, Rig.MaxJoints + 1)], []);

        Action spawn = () => SpawnPosed(test, pose);

        spawn.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SpawnRefusesARigPoseWithoutARenderRow()
    {
        using var test = new TestWorld();

        Action spawn = () => test.World.Spawn(new() { Frame = test.Root, RigPose = SpawnPose() });

        spawn.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HandOffKeepsTheRigPose()
    {
        using var test = TestWorld.OnJolt(faults: out _);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        // Jolt hands the ball off from the root into the station in the step of tick 2.
        Scenes.Approach flight = Scenes.ApproachToBubble(world: test.World, into: station, tick: 2);
        var poser = new TickPoser();
        Entity ball = test.World.Spawn(
            new()
            {
                Frame = test.Root,
                Pose = flight.Pose,
                LinearVelocity = flight.Velocity,
                Body = WorldTesting.Ball(),
                Render = Visible(),
                RigPose = SpawnPose(),
                Components = [poser],
            }
        );
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));
        test.Run(2);
        test.World.Latest();
        test.World.GetFrame(ball).Should().Be(test.Root, "the ball reaches the bubble only in the step of tick 2");

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        test.World.GetFrame(ball).Should().Be(station);
        RenderRigPose entry = EntryOf(snapshot, ball);
        snapshot.JointsOf(entry).ToArray().Should().Equal(PoseAt(poser.LastTick).Joints);
        snapshot.PreviousJointsOf(entry).ToArray().Should().Equal(PoseAt(poser.LastTick - 1).Joints);
        snapshot.PreviousWeightsOf(entry).ToArray().Should().Equal(PoseAt(poser.LastTick - 1).Weights);
    }

    [Fact]
    public void RespawnOfAShapeInThePoolAllocatesNoBlock()
    {
        EntityTable table = TableSupport.NewTable();
        RigPose pose = SpawnPose();
        Entity[] gone = [table.SpawnLive(), table.SpawnLive(), table.SpawnLive(), table.SpawnLive()];
        foreach (Entity entity in gone)
            table.SetSpawnRigPose(entity, pose);
        foreach (Entity entity in gone)
            table.Release(entity);
        Entity[] respawned = [table.SpawnLive(), table.SpawnLive(), table.SpawnLive()];
        int next = 0;
        Action respawn = () => table.SetSpawnRigPose(respawned[next++], pose);

        long allocated = Allocations.Fewest(respawn);

        allocated.Should().Be(0, "the pool holds a block of the shape of each respawn");
    }

    [Fact]
    public void SnapshotBuildWithPosedEntitiesAllocatesNothingInTheSteadyState()
    {
        using var test = new TestWorld(debugChecks: false);
        for (int i = 0; i < 40; i++)
        {
            if (i % 4 == 0)
                test.Show(test.Root, TestWorld.At(i));
            else
                SpawnPosed(test, PoseAt(0, joints: 2 + i % 5, weights: i % 3), new TickPoser());
        }

        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));
        test.Run(60);

        long allocated = Allocations.Fewest(() => test.Run(100));

        allocated.Should().Be(0, "a steady-state tick with posed entities must not allocate");
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void DebugChecksHoldWhilePosedEntitiesComeAndGo()
    {
        using var test = new TestWorld();
        var spawned = new Queue<Entity>();
        // Spawns a posed entity of one of three shapes in every tick, and destroys the oldest of five.
        Lambda churn = Lambda.OnTick(
            (self, scoped ref _, scoped ref outbox) =>
            {
                int shape = (int)(self.Tick % 3);
                spawned.Enqueue(
                    self.Spawn(
                        new()
                        {
                            Frame = self.Frame,
                            Render = Visible(),
                            RigPose = PoseAt(self.Tick, joints: 1 + shape, weights: shape),
                            Components = [new TickPoser()],
                        }
                    )
                );
                if (spawned.Count > 5)
                    outbox.Send(spawned.Dequeue(), new Destroy());
            }
        );
        test.SpawnWith(churn);
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(0, 0, 10));

        test.Run(30);

        test.World.Errors.Should().BeEmpty();
        test.World.Stats.LiveEntities.Should().BeLessThan(2 + 30, "the churner destroyed some of the posed entities");
    }
}
