using System.Numerics;

namespace Tertius.Simulation.Tests;

public class RenderSnapshotTests
{
    private static readonly DQuaternion QuarterTurn = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);

    private static RenderData Visible(int mesh = 0, float radius = 1) => new()
    {
        Mesh = new MeshId(mesh),
        Material = new MaterialId(3),
        Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
        BoundingRadius = radius,
        Scale = new Vector3(2),
    };

    private static FrameHandle StaticFrame(World world, FrameHandle parent, DPose pose, float radius = 1e6f) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = parent, Pose = pose, AsFrame = new FrameSpec(FrameBounds.Sphere(radius), MotionDriver.Static) }));

    private static Entity Show(World world, FrameHandle frame, DPose pose, params IComponent[] components) =>
        world.Spawn(new SpawnDesc { Frame = frame, Pose = pose, Render = Visible(), Components = components });

    private static RenderSnapshot Latest(World world)
    {
        Assert.True(world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot));
        return snapshot;
    }

    private static RenderInstance InstanceOf(RenderSnapshot snapshot, Entity entity) =>
        snapshot.Instances.Single(i => i.EntityIndex == entity.Index);

    private static void AssertFloatClose(double expected, float actual)
    {
        // One float step at the magnitude of the expected value.
        double tolerance = Math.Max(Math.Abs(expected) * 1.2e-7, 1e-6);
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected:R}, got {actual:R}");
    }

    [Fact]
    public void FarAndNearObjectsGetPreciseFloatPositionsRelativeToTheCamera()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle home = StaticFrame(world, root, DPose.Identity);
        FrameHandle distant = StaticFrame(world, root, new DPose(new Double3(1e11, 0, 0), QuarterTurn));
        Entity camera = world.Spawn(new SpawnDesc { Frame = home, Pose = TestWorld.At(12345.678), Render = new RenderData() });
        Entity near = Show(world, home, TestWorld.At(12347.678));
        Entity far = Show(world, distant, TestWorld.At(3, 4, 5));
        world.Camera = camera;

        world.Tick();
        RenderSnapshot snapshot = Latest(world);

        Assert.Equal(1, snapshot.Tick);
        Assert.Equal(Vector3.Zero, snapshot.Camera.Position);
        RenderInstance n = InstanceOf(snapshot, near);
        AssertFloatClose(2, n.Position.X);
        Assert.Equal(0, n.Position.Y);
        Assert.Equal(0, n.Position.Z);
        Assert.Equal(new Vector3(2), n.Scale);
        Assert.Equal(new MaterialId(3), n.Material);

        // The quarter turn maps local (3, 4, 5) to (5, 4, -3) in the root frame.
        RenderInstance f = InstanceOf(snapshot, far);
        AssertFloatClose(1e11 + 5 - 12345.678, f.Position.X);
        AssertFloatClose(4, f.Position.Y);
        AssertFloatClose(-3, f.Position.Z);
        Assert.True(Math.Abs(Math.Abs(f.Rotation.Y) - (float)QuarterTurn.Y) < 1e-6f);
        Assert.Equal(2, snapshot.Instances.Count);
    }

    [Fact]
    public void PreviousTransformsUseThePreviousTickRelativeToTheCurrentOrigin()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity camera = world.Spawn(new SpawnDesc { Frame = root, Render = new RenderData(), Components = [new Mover(new Double3(0, 1, 0))] });
        Entity moving = Show(world, root, TestWorld.At(10, 0, -5), new Mover(new Double3(1, 0, 0)));
        Entity still = Show(world, root, TestWorld.At(0, 0, -10));
        world.Camera = camera;

        world.Tick();
        RenderSnapshot first = Latest(world);
        // The first snapshot has nothing to interpolate from.
        Assert.Equal(InstanceOf(first, moving).Position, InstanceOf(first, moving).PreviousPosition);

        world.Tick();
        RenderSnapshot second = Latest(world);
        RenderInstance m = InstanceOf(second, moving);
        Assert.Equal(new Vector3(12, -2, -5), m.Position);
        Assert.Equal(new Vector3(11, -2, -5), m.PreviousPosition);
        RenderInstance s = InstanceOf(second, still);
        Assert.Equal(new Vector3(0, -2, -10), s.Position);
        Assert.Equal(s.Position, s.PreviousPosition);
        Assert.Equal(new Vector3(0, -1, 0), second.Camera.PreviousPosition);
        Assert.Equal(Vector3.Zero, second.Camera.Position);
    }

    [Fact]
    public void ACameraFrameChangeDisablesInterpolationForOneTick()
    {
        var physics = new FakePhysics { Integrate = false };
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        FrameHandle a = StaticFrame(world, root, TestWorld.At(0), 100);
        FrameHandle b = StaticFrame(world, root, TestWorld.At(500), 100);
        Entity camera = world.Spawn(new SpawnDesc { Frame = a, Body = TestWorld.Ball, Render = new RenderData() });
        Entity moving = Show(world, a, TestWorld.At(0, 0, -10), new Mover(new Double3(1, 0, 0)));
        world.Camera = camera;
        physics.OnStep = (context, output) =>
        {
            if (context.Tick == 2)
                output.HandOffs.Add(new HandOff(camera, a, b, TestWorld.At(-500), default, default));
        };

        world.Run(2);
        RenderInstance before = InstanceOf(Latest(world), moving);
        Assert.NotEqual(before.Position, before.PreviousPosition);

        world.Tick();
        RenderSnapshot switched = Latest(world);
        RenderInstance during = InstanceOf(switched, moving);
        Assert.Equal(during.Position, during.PreviousPosition);
        Assert.Equal(Vector3.Zero, switched.Camera.PreviousPosition);
        Assert.Equal(switched.Camera.Rotation, switched.Camera.PreviousRotation);
        // The camera is at the same place in the root frame, so the object is still 10 m in front of it.
        Assert.True(Vector3.Distance(new Vector3(3, 0, -10), during.Position) < 1e-3f, during.Position.ToString());

        world.Tick();
        RenderInstance after = InstanceOf(Latest(world), moving);
        Assert.True(Vector3.Distance(after.Position - new Vector3(1, 0, 0), after.PreviousPosition) < 1e-3f);
    }

    [Fact]
    public void UnchangedObjectsHaveBitIdenticalPreviousTransformsWhileOtherFramesMove()
    {
        const double mu = 3.986004418e14;
        var orbit = KeplerOrbit.Circular(7e6, mu, spinRate: 0.01);
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle moon = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(1e5f), MotionDriver.Kepler, Orbit: orbit) }));
        FrameHandle dock = StaticFrame(world, root, new DPose(new Double3(-3e6, 1e5, 42.5), QuarterTurn));
        FrameHandle yard = StaticFrame(world, root, new DPose(new Double3(4e6, -2e5, 7.25), DQuaternion.FromAxisAngle(new Double3(1, 1, 0), 0.3)));
        Entity camera = world.Spawn(new SpawnDesc { Frame = dock, Pose = TestWorld.At(1.5, 2.5, 3.5), Render = new RenderData() });
        Entity crate = Show(world, yard, TestWorld.At(11.1, 22.2, 33.3));
        Entity satellite = Show(world, moon, TestWorld.At(100, 0, 0));
        world.Camera = camera;

        world.Run(3);
        RenderSnapshot snapshot = Latest(world);

        RenderInstance c = InstanceOf(snapshot, crate);
        Assert.Equal(c.Position, c.PreviousPosition);
        Assert.Equal(c.Rotation, c.PreviousRotation);

        // The satellite rides the moon, so it moved between the two ticks, by about orbital speed times dt.
        RenderInstance s = InstanceOf(snapshot, satellite);
        float moved = Vector3.Distance(s.Position, s.PreviousPosition);
        double expected = Math.Sqrt(mu / 7e6) * world.DeltaTime;
        Assert.True(Math.Abs(moved - expected) < 0.02 * expected, $"moved {moved}, expected about {expected}");
    }

    [Fact]
    public void LightsSunOccludersAndTheViewOffset()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle planet = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = root,
            Pose = TestWorld.At(0, 0, -1e7),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e8f), MotionDriver.Static, GravityField.Point(3.986004418e14)),
            Render = Visible(radius: 6e6f),
        }));
        FrameHandle sun = StaticFrame(world, root, TestWorld.At(1e11), 1e9f);
        world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(1e11), Render = new RenderData() });
        Entity camera = world.Spawn(new SpawnDesc
        {
            Frame = root,
            Render = new RenderData { ViewOffset = new Pose(new Vector3(0, 1.7f, 0), QuarterTurn.ToQuaternion()) },
        });
        var lamp = new RenderData
        {
            Light = new LightData { Enabled = true, Kind = LightKind.Spot, Color = new Vector3(1, 0.5f, 0.25f), Intensity = 3, Range = 20, Offset = new Pose(new Vector3(0, 1, 0), Quaternion.Identity) },
        };
        world.Spawn(new SpawnDesc { Frame = root, Pose = new DPose(new Double3(5, 0, 0), QuarterTurn), Render = lamp });
        world.Camera = camera;
        world.SunFrame = sun;
        world.SunColor = new Vector3(1, 0.9f, 0.8f);
        world.SunIntensity = 7;
        world.Ambient = new AmbientLight { SkyColor = new Vector3(0.2f), GroundColor = new Vector3(0.1f), Intensity = 0.5f };
        world.VerticalFov = 1.1f;
        // Make the sun owner a visible body with a radius, like a star.
        world.Tick();
        RenderSnapshot snapshot = Latest(world);

        Assert.Equal(1.1f, snapshot.Camera.VerticalFov);
        Assert.True(Math.Abs(snapshot.Camera.Rotation.Y - (float)QuarterTurn.Y) < 1e-6f);
        RenderLight light = Assert.Single(snapshot.Lights);
        Assert.Equal(LightKind.Spot, light.Kind);
        Assert.True(Vector3.Distance(new Vector3(5, 1 - 1.7f, 0), light.Position) < 1e-5f, light.Position.ToString());
        // A quarter turn about +Y maps the forward axis -Z to -X.
        Assert.True(Vector3.Distance(new Vector3(-1, 0, 0), light.Direction) < 1e-6f, light.Direction.ToString());
        Assert.Equal(3, light.Intensity);
        Assert.Equal(20, light.Range);

        Assert.True(Vector3.Distance(Vector3.UnitX, snapshot.Sun.Direction) < 1e-6f);
        Assert.Equal(new Vector3(1, 0.9f, 0.8f), snapshot.Sun.Color);
        Assert.Equal(7, snapshot.Sun.Intensity);

        RenderOccluder occluder = Assert.Single(snapshot.Occluders);
        Assert.Equal(6e6f, occluder.Radius);
        Assert.True(Vector3.Distance(new Vector3(0, -1.7f, -1e7f), occluder.Center) < 1f);
        Assert.Equal(0.5f, snapshot.Ambient.Intensity);
        Assert.Equal(world.Time, snapshot.Time);
        Assert.True(snapshot.PublishedAt > 0);
        // The planet itself is drawn too, as its frame's pose.
        Assert.Contains(snapshot.Instances, i => i.EntityIndex == planet.Entity.Index && Math.Abs(i.Position.Z + 1e7f) < 1);
    }

    [Fact]
    public void WithoutACameraTheSnapshotIsEmptyAndRenderChangesApplyAtTheSyncPoint()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity e = world.Spawn(new SpawnDesc
        {
            Frame = root,
            Render = new RenderData(),
            Components = [new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            {
                if (self.Tick == 1)
                {
                    self.Render.Mesh = new MeshId(9);
                    self.Render.Flags = RenderFlags.Visible;
                }
            })],
        });
        world.PublishRenderSnapshot();
        Assert.Empty(Latest(world).Instances);

        world.Camera = world.SpawnWith(root);
        world.Tick();
        Assert.Empty(Latest(world).Instances);
        Assert.Equal(MeshId.None, world.Snapshot.RenderOf(e).Mesh);

        world.Tick();
        RenderInstance instance = Assert.Single(Latest(world).Instances);
        Assert.Equal(new MeshId(9), instance.Mesh);
        Assert.Equal(new MeshId(9), world.GetRender(e).Mesh);
    }

    [Fact]
    public void TheOriginIsTheCameraPositionInItsFrameAndTheFrameIdFollowsTheCamera()
    {
        var physics = new FakePhysics { Integrate = false };
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        FrameHandle a = StaticFrame(world, root, TestWorld.At(0), 100);
        FrameHandle b = StaticFrame(world, root, TestWorld.At(500), 100);
        Entity camera = world.Spawn(new SpawnDesc
        {
            Frame = a,
            Pose = TestWorld.At(1234567.25, 2, 3),
            Body = TestWorld.Ball,
            Render = new RenderData { ViewOffset = new Pose(new Vector3(0, 1.5f, 0), Quaternion.Identity) },
        });
        Show(world, a, TestWorld.At(4, 5, 6));
        // A default render row has no mesh, so it draws nothing even when it is visible.
        world.Spawn(new SpawnDesc { Frame = a, Render = default(RenderData) with { Flags = RenderFlags.Visible } });
        world.Camera = camera;
        long idOfA = ((long)a.Generation << 32) | (uint)a.Index;

        world.PublishRenderSnapshot();
        RenderSnapshot first = Latest(world);
        Assert.True(TestWorld.RoughlyEqual(new Double3(1234567.25, 3.5, 3), first.OriginInFrame), first.OriginInFrame.ToString());
        Assert.Equal(idOfA, first.OriginFrameId);
        RenderInstance only = Assert.Single(first.Instances);
        Assert.Equal(0, only.Part);
        Assert.Equal(new MeshId(0), only.Mesh);

        physics.OnStep = (context, output) =>
        {
            if (context.Tick == 1)
                output.HandOffs.Add(new HandOff(camera, a, b, TestWorld.At(7, 8, 9), default, default));
        };
        world.Tick();
        Assert.Equal(idOfA, Latest(world).OriginFrameId);
        world.Tick();
        RenderSnapshot moved = Latest(world);
        Assert.Equal(((long)b.Generation << 32) | (uint)b.Index, moved.OriginFrameId);
        Assert.True(TestWorld.RoughlyEqual(new Double3(7, 9.5, 9), moved.OriginInFrame), moved.OriginInFrame.ToString());

        world.Camera = Entity.Null;
        world.Tick();
        RenderSnapshot none = Latest(world);
        Assert.Equal(0, none.OriginFrameId);
        Assert.Equal(Double3.Zero, none.OriginInFrame);
        Assert.Empty(none.Instances);
    }

    [Fact]
    public void TheOwnerOfARootFrameLooksOutFromTheFrameOrigin()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity box = Show(world, root, TestWorld.At(3, 4, 5));
        // A planet frame with no render radius casts no eclipse.
        world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(0, 0, -1e7), AsFrame = new FrameSpec(FrameBounds.Sphere(1e8f), MotionDriver.Static, GravityField.Point(3.986004418e14)) });
        world.Camera = root.Entity;

        world.Tick();

        RenderSnapshot snapshot = Latest(world);
        Assert.Equal(new Vector3(3, 4, 5), InstanceOf(snapshot, box).Position);
        Assert.Equal(Double3.Zero, snapshot.OriginInFrame);
        Assert.Equal(((long)root.Generation << 32) | (uint)root.Index, snapshot.OriginFrameId);
        Assert.Empty(snapshot.Occluders);
    }

    [Fact]
    public void TheParallelBuildMatchesTheSequentialBuild()
    {
        RenderSnapshot Build(int maxDop)
        {
            var world = TestWorld.Create(out FrameHandle root, maxDop: maxDop);
            FrameHandle side = StaticFrame(world, root, new DPose(new Double3(1e9, 5, 5), QuarterTurn));
            for (int i = 0; i < 12_000; i++)
            {
                FrameHandle frame = i % 3 == 0 ? side : root;
                world.Spawn(new SpawnDesc
                {
                    Frame = frame,
                    Pose = TestWorld.At(i * 0.5, i % 7, -i % 11),
                    Render = Visible(i % 5),
                    Components = i % 4 == 0 ? [new Mover(new Double3(0.25, 0, 0))] : [],
                });
            }
            world.Camera = world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(100), Render = new RenderData() });
            world.Run(2);
            Assert.True(world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot));
            world.Dispose();
            return snapshot;
        }

        RenderSnapshot sequential = Build(1);
        RenderSnapshot parallel = Build(8);
        Assert.Equal(12_000, sequential.Instances.Count);
        Assert.Equal(sequential.Instances, parallel.Instances);
        Assert.True(sequential.Instances.Zip(sequential.Instances.Skip(1)).All(p => p.First.EntityIndex < p.Second.EntityIndex));
    }
}
