using System.Numerics;

namespace Tertius.Simulation.Tests;

public class AttachmentTests
{
    private static readonly DQuaternion QuarterTurn = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);

    [Fact]
    public void AChildFollowsItsParentWithAFixedOffset()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity parent = world.Spawn(new SpawnDesc
        {
            Frame = root,
            Pose = new DPose(new Double3(10, 0, 0), QuarterTurn),
            Components = [new Mover(new Double3(1, 0, 0))],
        });
        Entity child = world.Spawn(new SpawnDesc { Frame = root, AttachTo = parent, AttachOffset = new Pose(new Vector3(0, 0, 2), Quaternion.Identity) });
        Entity grandchild = world.Spawn(new SpawnDesc { Frame = root, AttachTo = child, AttachOffset = new Pose(new Vector3(0, 1, 0), Quaternion.Identity) });

        // Outside a tick the child is placed at once. The quarter turn maps the local +Z offset to +X.
        Assert.True(TestWorld.RoughlyEqual(new Double3(12, 0, 0), world.GetPose(child).Position), world.GetPose(child).ToString());
        Assert.True(TestWorld.RoughlyEqual(new Double3(12, 1, 0), world.GetPose(grandchild).Position));

        world.Run(3);

        Assert.True(TestWorld.RoughlyEqual(new Double3(15, 0, 0), world.GetPose(child).Position, 1e-6), world.GetPose(child).ToString());
        Assert.True(TestWorld.RoughlyEqual(new Double3(15, 1, 0), world.GetPose(grandchild).Position, 1e-6));
        DQuaternion rotation = world.GetPose(child).Rotation;
        Assert.True(Math.Abs(Math.Abs(rotation.Y) - QuarterTurn.Y) < 1e-6);
        Assert.Equal(root, world.GetFrame(child));
    }

    [Fact]
    public void AnAttachedEntityChangesItsOffsetButNotItsTransform()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity parent = world.SpawnAt(root, new Double3(10, 0, 0));
        Entity seenParent = default;
        Entity child = world.Spawn(new SpawnDesc
        {
            Frame = root,
            AttachTo = parent,
            AttachOffset = new Pose(new Vector3(1, 0, 0), Quaternion.Identity),
            Components = [new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            {
                seenParent = self.AttachedTo;
                if (self.Tick == 0)
                    self.AttachOffset = new Pose(new Vector3(4, 0, 0), Quaternion.Identity);
                if (self.Tick == 1)
                    self.Transform = TestWorld.At(0);
            })],
        });

        world.Tick();
        Assert.Equal(parent, seenParent);
        Assert.True(TestWorld.RoughlyEqual(new Double3(14, 0, 0), world.GetPose(child).Position));

        world.Tick();
        Assert.Contains("attached", Assert.Single(world.Errors));
        Assert.True(TestWorld.RoughlyEqual(new Double3(14, 0, 0), world.GetPose(child).Position));
    }

    [Fact]
    public void AnEntityThatIsNotAttachedCannotWriteAnOffset()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity loner = world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(),
            (Self self, scoped ref EventList events, scoped ref Outbox outbox) => self.AttachOffset = Pose.Identity));

        world.Tick();

        Assert.Contains("not attached", Assert.Single(world.Errors));
        Assert.True(world.IsAlive(loner));
    }

    [Fact]
    public void ChildrenAreDestroyedWithTheirParent()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity parent = world.SpawnWith(root);
        Entity child = world.Spawn(new SpawnDesc { Frame = root, AttachTo = parent });
        Entity grandchild = world.Spawn(new SpawnDesc { Frame = root, AttachTo = child });
        Entity sibling = world.Spawn(new SpawnDesc { Frame = root, AttachTo = parent });

        world.Destroy(sibling);
        Assert.True(world.IsAlive(child));
        world.Destroy(parent);

        Assert.False(world.IsAlive(child));
        Assert.False(world.IsAlive(grandchild));

        // The slots are clean: a new chain on a reused slot works.
        Entity again = world.SpawnAt(root, new Double3(5, 0, 0));
        Entity follower = world.Spawn(new SpawnDesc { Frame = root, AttachTo = again, AttachOffset = new Pose(Vector3.UnitY, Quaternion.Identity) });
        world.Tick();
        Assert.True(TestWorld.RoughlyEqual(new Double3(5, 1, 0), world.GetPose(follower).Position));
    }

    [Fact]
    public void ChildrenOfABodyInheritItsVelocityAndFollowItsHandOff()
    {
        var physics = new FakePhysics();
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(500), AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static) }));
        Entity parent = world.Spawn(new SpawnDesc
        {
            Frame = root,
            LinearVelocity = new Vector3(1, 0, 0),
            AngularVelocity = new Vector3(0, 1, 0),
            Body = TestWorld.Ball,
        });
        var childLog = new Recorder(EventMask.Of<FrameChanged>());
        Entity child = world.Spawn(new SpawnDesc { Frame = root, AttachTo = parent, AttachOffset = new Pose(new Vector3(0, 0, 2), Quaternion.Identity), Components = [childLog] });
        physics.OnStep = (context, output) =>
        {
            if (context.Tick == 1)
                output.HandOffs.Add(new HandOff(parent, root, station, TestWorld.At(-50), new Vector3(1, 0, 0), new Vector3(0, 1, 0)));
        };

        world.Tick();
        // v + ω × r = (1, 0, 0) + (0, 1, 0) × (0, 0, 2) = (3, 0, 0).
        Assert.True(Vector3.Distance(new Vector3(3, 0, 0), world.GetVelocity(child)) < 1e-5f, world.GetVelocity(child).ToString());
        Assert.Equal(new Vector3(0, 1, 0), world.GetAngularVelocity(child));

        world.Run(2);
        Assert.Equal(station, world.GetFrame(child));
        // Handed off at (-50, 0, 0) on tick 1, then one more step at 1 m/s.
        Double3 expected = new(-50 + (double)world.DeltaTime, 0, 2);
        Assert.True(TestWorld.RoughlyEqual(expected, world.GetPose(child).Position, 1e-5), world.GetPose(child).ToString());
        FrameChanged changed = Assert.Single(childLog.Seen).Event.As<FrameChanged>();
        Assert.Equal(root, changed.From);
        Assert.Equal(station, changed.To);
    }

    [Fact]
    public void AChildSpawnedDuringATickCanAttachToAParentSpawnedInTheSameTick()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity parent = default, child = default;
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick != 0)
                return;
            parent = self.Spawn(new SpawnDesc { Frame = self.Frame, Pose = TestWorld.At(7) });
            child = self.Spawn(new SpawnDesc { Frame = self.Frame, AttachTo = parent, AttachOffset = new Pose(new Vector3(0, 3, 0), Quaternion.Identity) });
        }));

        world.Tick();

        Assert.True(world.IsAlive(child));
        Assert.True(TestWorld.RoughlyEqual(new Double3(7, 3, 0), world.GetPose(child).Position));
    }

    [Fact]
    public void AttachingAcrossFramesIsRejected()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle other = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static) }));
        Entity parent = world.SpawnWith(other);
        Assert.Throws<ArgumentException>(() => world.Spawn(new SpawnDesc { Frame = root, AttachTo = parent }));

        // During a tick the check happens at the sync point, which reports it and removes the child.
        Entity child = default;
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick == 0)
                child = self.Spawn(new SpawnDesc { Frame = root, AttachTo = parent });
        }));
        world.Tick();
        Assert.False(world.IsAlive(child));
        Assert.Contains("attachment parent", Assert.Single(world.Errors));
    }
}

public class SpatialQueryTests
{
    [Fact]
    public void QueryRadiusIsExactSortedAndPerFrame()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle other = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(1000), MotionDriver.Static) }));
        var random = new Random(1234);
        // The owner of the inner frame is itself an entity of the root frame, at its origin.
        var points = new List<(Entity Entity, Double3 Position)> { (other.Entity, Double3.Zero) };
        for (int i = 0; i < 400; i++)
        {
            var p = new Double3(random.NextDouble() * 400 - 200, random.NextDouble() * 400 - 200, random.NextDouble() * 400 - 200);
            points.Add((world.SpawnAt(root, p), p));
            world.SpawnAt(other, p);
        }
        Entity onEdge = world.SpawnAt(root, new Double3(50, 0, 0));
        points.Add((onEdge, new Double3(50, 0, 0)));

        var results = new List<Entity>();
        foreach (double radius in new[] { 0.0, 10, 50, 75, 150, 1e6 })
        {
            Double3 center = Double3.Zero;
            world.Snapshot.QueryRadius(root, center, radius, results);
            List<Entity> expected = points.Where(p => (p.Position - center).LengthSquared <= radius * radius).Select(p => p.Entity).OrderBy(e => e.Index).ToList();
            Assert.Equal(expected, results);
        }
        world.Snapshot.QueryRadius(root, Double3.Zero, 50, results);
        Assert.Contains(onEdge, results);
        Assert.Throws<InvalidOperationException>(() => world.Snapshot.QueryRadius(FrameHandle.Null, Double3.Zero, 1, results));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Snapshot.QueryRadius(root, Double3.Zero, -1, results));
    }

    [Fact]
    public void AQueryInAFrameWithNothingInItFindsNothing()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle empty = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static) }));
        world.SpawnAt(root, Double3.Zero);
        var found = new List<Entity> { root.Entity };

        world.Snapshot.QueryRadius(empty, Double3.Zero, 100, found);

        Assert.Empty(found);
    }

    [Fact]
    public void TheGridFollowsMovesAndDestruction()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity runner = world.SpawnWith(root, new Mover(new Double3(40, 0, 0)));
        Entity post = world.SpawnAt(root, new Double3(200, 50, 0));
        var results = new List<Entity>();

        for (int tick = 0; tick < 8; tick++)
        {
            world.Snapshot.QueryRadius(root, new Double3(40 * tick, 0, 0), 1, results);
            Assert.Equal([runner], results);
            world.Tick();
        }
        world.Snapshot.QueryRadius(root, new Double3(200, 50, 0), 1, results);
        Assert.Equal([post], results);
        world.Destroy(post);
        world.Snapshot.QueryRadius(root, new Double3(200, 50, 0), 1, results);
        Assert.Empty(results);
    }

    [Fact]
    public void ComponentsQueryTheSnapshotFromWorkerThreads()
    {
        using World world = TestWorld.Create(out FrameHandle root, maxDop: 8, chunkSize: 4);
        var counts = new int[64];
        for (int i = 0; i < 64; i++)
        {
            int index = i;
            world.Spawn(new SpawnDesc
            {
                Frame = root,
                Pose = TestWorld.At(i * 10),
                Components = [new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
                {
                    var found = new List<Entity>();
                    self.Snapshot.QueryRadius(self.Frame, self.Pose.Position, 15, found);
                    counts[index] = found.Count;
                })],
            });
        }

        world.Tick();

        // Neighbors 10 m away on each side, and itself.
        Assert.Equal(2, counts[0]);
        Assert.Equal(2, counts[63]);
        Assert.All(counts[1..63], c => Assert.Equal(3, c));
    }
}
