using System.Numerics;

namespace Tertius.Simulation.Tests;

public class TransformTests
{
    [Fact]
    public void TransformThrowsWhenPhysicsOwnsTheEntity()
    {
        using World world = TestWorld.Create(out FrameHandle root, physics: new FakePhysics());
        bool exists = false;
        world.Spawn(new SpawnDesc
        {
            Frame = root,
            Body = TestWorld.Ball,
            Components = [new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            {
                exists = self.Body.Exists;
                self.Transform = TestWorld.At(5);
            })],
        });
        bool bodilessExists = true;
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            bodilessExists = self.Body.Exists;
            self.Body.AddForce(Vector3.UnitX);
        }));

        world.Tick();

        Assert.True(exists);
        Assert.False(bodilessExists);
        Assert.Equal(2, world.Errors.Count);
        Assert.Contains(world.Errors, e => e.Contains(nameof(InvalidOperationException)) && e.Contains("physics owns its transform"));
        Assert.Contains(world.Errors, e => e.Contains("has no physics body"));
    }

    [Fact]
    public void TransformThrowsForAKeplerFrameOwner()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        world.Spawn(new SpawnDesc
        {
            Frame = root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Kepler, Orbit: KeplerOrbit.Circular(1000, 1e9)),
            Components = [new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) => self.Transform = DPose.Identity)],
        });
        world.Tick();
        Assert.Contains("Kepler frame", Assert.Single(world.Errors));
    }

    [Fact]
    public void ReadsSeeThePreviousTickAndWritesApplyAtTheSyncPoint()
    {
        using World world = TestWorld.Create(out FrameHandle root, maxDop: 4, chunkSize: 1);
        Entity mover = world.SpawnWith(root, new Mover(new Double3(1, 0, 0)));
        var seenByWatcher = new List<double>();
        var seenByMover = new List<double>();
        world.AddComponent(mover, new Lambda(EventMask.Of<Tick>(),
            (Self self, scoped ref EventList events, scoped ref Outbox outbox) => seenByMover.Add(self.Pose.Position.X), priority: 10));
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(),
            (Self self, scoped ref EventList events, scoped ref Outbox outbox) => seenByWatcher.Add(self.Snapshot.PoseOf(mover).Position.X)));

        world.Run(4);

        Assert.Equal([0, 1, 2, 3], seenByWatcher);
        // Self.Pose is the front snapshot too, even after the entity wrote its back row in the same run.
        Assert.Equal([0, 1, 2, 3], seenByMover);
        Assert.Equal(4, world.GetPose(mover).Position.X);
    }

    [Fact]
    public void AStaticFrameMovesWhenItsOwnerWritesItsTransform()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            Components = [new Mover(new Double3(0, 2, 0))],
        });
        FrameHandle frame = world.GetOwnedFrame(owner);
        Entity passenger = world.SpawnAt(frame, new Double3(1, 0, 0));

        world.Run(3);

        Assert.Equal(new Double3(0, 6, 0), world.Frames.LocalPose(frame).Position);
        Assert.Equal(new Double3(0, 6, 0), world.GetPose(owner).Position);
        Assert.Equal(new Double3(1, 0, 0), world.GetPose(passenger).Position);
        Assert.True(world.Snapshot.TryGetPoseIn(passenger, root, out DPose inRoot));
        Assert.Equal(new Double3(1, 6, 0), inRoot.Position);
    }

    [Fact]
    public void KeplerFramesFollowTheirOrbit()
    {
        const double mu = 3.986004418e14, radius = 7e6;
        var orbit = new KeplerOrbit(radius, 0.1, 0.2, 0.3, 0.4, 0.5, 0, mu, DQuaternion.Identity, 0.001, 0.25);
        using World world = TestWorld.Create(out FrameHandle root);
        Entity owner = world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(1000), MotionDriver.Kepler, Orbit: orbit) });
        FrameHandle frame = world.GetOwnedFrame(owner);
        Entity onboard = world.SpawnAt(frame, new Double3(10, 0, 0));

        // The frame is placed by its orbit from the moment it exists.
        Assert.Equal(orbit.StateAt(0).Position, world.Frames.LocalPose(frame).Position);

        world.Run(90);

        double t = world.Time;
        Assert.Equal(90 * (double)world.DeltaTime, t, 12);
        (Double3 position, Double3 velocity) = orbit.StateAt(t);
        DPose local = world.Frames.LocalPose(frame);
        Assert.Equal(position, local.Position);
        Assert.Equal(orbit.RotationAt(t), local.Rotation);
        Assert.Equal(velocity, world.Frames.LinearVelocity(frame));
        Assert.Equal(orbit.AngularVelocity(), world.Frames.AngularVelocity(frame));
        Assert.Equal(Vector3.Zero, world.Frames.ProperAcceleration(frame));
        // The owner's row mirrors the frame; the passenger keeps its local pose and moves with the frame.
        Assert.Equal(local, world.GetPose(owner));
        Assert.Equal(velocity.ToVector3(), world.GetVelocity(owner));
        Assert.Equal(new Double3(10, 0, 0), world.GetPose(onboard).Position);
        Assert.True(world.Snapshot.TryGetPoseIn(onboard, root, out DPose inRoot));
        Assert.True(TestWorld.RoughlyEqual(local.TransformPoint(new Double3(10, 0, 0)), inRoot.Position, 1e-6));
        Assert.True(position.Length > 0.9 * radius);
    }

    [Fact]
    public void WorldsCanStartAtAnEpoch()
    {
        var orbit = KeplerOrbit.Circular(1e7, 3.986004418e14);
        using var world = new World(new WorldOptions { StartTime = 5000 });
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e12f), MotionDriver.Static));
        FrameHandle moon = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(1e5f), MotionDriver.Kepler, Orbit: orbit) }));
        Assert.Equal(5000, world.Time);
        Assert.Equal(orbit.StateAt(5000).Position, world.Frames.LocalPose(moon).Position);
        world.Tick();
        Assert.Equal(orbit.StateAt(5000 + (double)world.DeltaTime).Position, world.Frames.LocalPose(moon).Position);
    }
}

public class PhysicsSeamTests
{
    [Fact]
    public void BodyStatesAreCopiedIntoTheTransformTable()
    {
        var physics = new FakePhysics();
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        Entity ball = world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(1), LinearVelocity = new Vector3(6, 0, 0), Body = TestWorld.Ball });

        world.Run(3);

        FakePhysics.Body body = physics.Bodies[ball];
        Assert.Equal(body.Pose, world.GetPose(ball));
        Assert.Equal(body.Velocity, world.GetVelocity(ball));
        Assert.Equal(1 + 3 * 6 * (double)world.DeltaTime, world.GetPose(ball).Position.X, 6);
        Assert.Equal(root, world.GetFrame(ball));
        Assert.Contains($"body+ {ball} in {root.Index}", physics.Changes);
    }

    [Fact]
    public void IntentsReachPhysicsInTheNextStepWithAccumulateAndLastWriteRules()
    {
        var physics = new FakePhysics();
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        PhysicsIntent pendingAtEnd = default;
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = root,
            Body = TestWorld.Ball,
            Components = [new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            {
                if (self.Tick != 0)
                    return;
                BodyHandle body = self.Body;
                body.AddForce(new Vector3(1, 0, 0));
                body.AddForce(new Vector3(2, 0, 0));
                body.AddImpulse(new Vector3(0, 1, 0));
                body.AddImpulse(new Vector3(0, 1, 0));
                body.AddTorque(new Vector3(0, 0, 3));
                body.AddAngularImpulse(new Vector3(0, 0, 4));
                body.SetVelocity(new Vector3(9, 9, 9));
                body.SetVelocity(new Vector3(7, 0, 0));
                body.SetAngularVelocity(new Vector3(0, 1, 0));
                body.MoveKinematic(TestWorld.At(50));
                body.Teleport(TestWorld.At(100));
                body.MoveCharacter(new Vector3(0, 0, 2));
                body.Jump(4);
                pendingAtEnd = body.Pending;
            })],
        });

        world.Run(3);

        (long tick, Entity entity, PhysicsIntent intent) = Assert.Single(physics.Intents);
        Assert.Equal(1, tick);
        Assert.Equal(ball, entity);
        Assert.Equal(new Vector3(3, 0, 0), intent.Force);
        Assert.Equal(new Vector3(0, 2, 0), intent.Impulse);
        Assert.Equal(new Vector3(0, 0, 3), intent.Torque);
        Assert.Equal(new Vector3(0, 0, 4), intent.AngularImpulse);
        Assert.Equal(new Vector3(7, 0, 0), intent.Velocity);
        Assert.Equal(new Vector3(0, 1, 0), intent.AngularVelocity);
        Assert.Equal(100, intent.Target.Position.X);
        Assert.Equal(new Vector3(0, 0, 2), intent.CharacterVelocity);
        Assert.Equal(4, intent.JumpSpeed);
        Assert.Equal(IntentFlags.SetVelocity | IntentFlags.SetAngularVelocity | IntentFlags.KinematicTarget | IntentFlags.Teleport | IntentFlags.CharacterMove | IntentFlags.Jump, intent.Flags);
        Assert.Equal(intent.Force, pendingAtEnd.Force);
    }

    [Fact]
    public void PhysicsStepsOnOneDedicatedThread()
    {
        var physics = new FakePhysics();
        using (World world = TestWorld.Create(out FrameHandle root, maxDop: 4, physics: physics))
        {
            world.Spawn(new SpawnDesc { Frame = root, Body = TestWorld.Ball });
            world.Run(5);
            Assert.Equal([0L, 1, 2, 3, 4], physics.StepTicks);
            Assert.Equal(0, physics.StepTimes[0]);
            Assert.Equal(4 * (double)world.DeltaTime, physics.StepTimes[4], 12);
        }
        Assert.Single(physics.StepThreads.Distinct());
        Assert.NotEqual(Environment.CurrentManagedThreadId, physics.StepThreads[0]);
        Assert.All(physics.StepOnPool, onPool => Assert.False(onPool));
        Assert.False(physics.Disposed);
    }

    [Fact]
    public void TheWorldDisposesThePhysicsBackendOnlyWhenAsked()
    {
        var physics = new FakePhysics();
        new World(new WorldOptions { DisposePhysics = true }, physics).Dispose();
        Assert.True(physics.Disposed);
    }

    [Fact]
    public void WithoutPhysicsIntentsAreDroppedAndBodiesStayPut()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = root,
            Pose = TestWorld.At(2),
            LinearVelocity = new Vector3(5, 0, 0),
            Body = TestWorld.Ball,
            Components = [new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) => self.Body.AddForce(Vector3.UnitY))],
        });

        world.Run(5);

        Assert.Equal(2, world.GetPose(ball).Position.X);
        Assert.Empty(world.Errors);
    }

    [Fact]
    public void ContactsBecomeCollisionsForBothEntitiesInTheirOwnFrames()
    {
        var physics = new FakePhysics { Integrate = false };
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        FrameHandle turned = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = root,
            Pose = new DPose(new Double3(100, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static),
        }));
        var aLog = new Recorder(EventMask.Of<Collision>());
        var bLog = new Recorder(EventMask.Of<Collision>());
        Entity a = world.Spawn(new SpawnDesc { Frame = root, Body = TestWorld.Ball, Components = [aLog] });
        Entity b = world.Spawn(new SpawnDesc { Frame = turned, Body = TestWorld.Ball, Components = [bLog] });
        physics.OnStep = (context, output) =>
        {
            if (context.Tick == 0)
                output.Contacts.Add(new ContactStarted(a, b, root, new Double3(1, 2, 3), new Vector3(1, 0, 0), 5));
        };

        world.Run(2);

        (long aTick, Event aEvent) = Assert.Single(aLog.Seen);
        Collision forA = aEvent.As<Collision>();
        Assert.Equal(1, aTick);
        Assert.Equal(b, forA.Other);
        Assert.Equal(new Double3(1, 2, 3), forA.Point);
        Assert.Equal(new Vector3(1, 0, 0), forA.Normal);
        Assert.Equal(5, forA.Impulse);

        Collision forB = Assert.Single(bLog.Seen).Event.As<Collision>();
        Assert.Equal(a, forB.Other);
        // In the turned frame: R⁻¹((1, 2, 3) − (100, 0, 0)), where R is a quarter turn about +Y.
        Assert.True(TestWorld.RoughlyEqual(new Double3(-3, 2, -99), forB.Point), forB.Point.ToString());
        Assert.True(Vector3.Distance(new Vector3(0, 0, -1), forB.Normal) < 1e-6f, forB.Normal.ToString());
    }

    [Fact]
    public void FrameStatesDrivePhysicsFrames()
    {
        var physics = new FakePhysics { Integrate = false };
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Physics),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10)), Layer: BodyLayer.FrameHull),
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        physics.OnStep = (context, output) =>
            output.Frames.Add(new FrameState(ship, TestWorld.At(5, 6, 7), new Double3(1, 2, 3), new Vector3(0, 0.5f, 0), new Vector3(0, -2, 0)));

        world.Tick();

        Assert.Equal(new Double3(5, 6, 7), world.Frames.LocalPose(ship).Position);
        Assert.Equal(new Double3(1, 2, 3), world.Frames.LinearVelocity(ship));
        Assert.Equal(new Vector3(0, 0.5f, 0), world.Frames.AngularVelocity(ship));
        Assert.Equal(new Vector3(0, -2, 0), world.Frames.ProperAcceleration(ship));
        Assert.Equal(new Double3(5, 6, 7), world.GetPose(owner).Position);
        Assert.Equal(new Vector3(1, 2, 3), world.GetVelocity(owner));
        Assert.Contains($"frame+ {ship.Index}", physics.Changes);
        Assert.True(physics.Changes.IndexOf($"frame+ {ship.Index}") < physics.Changes.IndexOf($"body+ {owner} in {root.Index}"));
    }

    [Fact]
    public void AHandOffOfAFrameOwnerReparentsItsFrame()
    {
        var physics = new FakePhysics { Integrate = false };
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        FrameHandle carrier = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(1000), AsFrame = new FrameSpec(FrameBounds.Sphere(500), MotionDriver.Static) }));
        Entity fighterOwner = world.Spawn(new SpawnDesc
        {
            Frame = root,
            Pose = TestWorld.At(1100),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
            Body = new BodyDesc(new SphereShapeDesc(5), Layer: BodyLayer.FrameHull),
        });
        FrameHandle fighter = world.GetOwnedFrame(fighterOwner);
        Entity pilot = world.SpawnAt(fighter, new Double3(0, 1, 0));
        physics.OnStep = (context, output) =>
        {
            if (context.Tick == 0)
                output.HandOffs.Add(new HandOff(fighterOwner, root, carrier, TestWorld.At(100), default, default));
        };

        world.Tick();

        Assert.Equal(carrier, world.Frames.Parent(fighter));
        Assert.Equal(carrier, world.GetFrame(fighterOwner));
        Assert.True(TestWorld.RoughlyEqual(new Double3(100, 0, 0), world.GetPose(fighterOwner).Position));
        Assert.Equal(fighter, world.GetFrame(pilot));
        Assert.True(world.Snapshot.TryGetPoseIn(pilot, root, out DPose pilotInRoot));
        Assert.True(TestWorld.RoughlyEqual(new Double3(1100, 1, 0), pilotInRoot.Position));
    }

    [Fact]
    public void APhysicsFailureIsReportedAndTheWorldGoesOn()
    {
        var physics = new FakePhysics();
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        physics.OnStep = (context, output) =>
        {
            if (context.Tick == 1)
                throw new InvalidOperationException("solver exploded");
        };
        world.Run(3);
        Assert.Contains("solver exploded", Assert.Single(world.Errors));
        Assert.Equal(3, world.TickNumber);
    }
}
