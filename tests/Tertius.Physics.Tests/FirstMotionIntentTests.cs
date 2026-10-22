using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The first motion intent of a spawn, <see cref="SpawnDesc.FirstMove"/>, on the World and the real backend
/// (docs/plans/rails-components.md, "The first motion intent of a spawn"). It is the motion intent that the new entity
/// would have written in the spawn's tick, and the step after the spawn applies it, so that a frame on rails stands on its
/// rails from its first step. A spawn refuses a case that its entity would refuse.
/// </summary>
public class FirstMotionIntentTests
{
    private const double StationRadius = 6.771e6;
    private static readonly FrameSpec SmallBubble = new(FrameBounds.Sphere(0.01f));

    private static readonly DPose Turned = new(new(1, 0.5, -0.25), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.1));

    private static BodyDesc Box(BodyMotion motion) =>
        new(new BoxShapeDesc(new(2, 0.5f, 2)), motion, Mass: 1000, LinearDamping: 0, AngularDamping: 0);

    private static FrameHandle Space(World world) => world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));

    [Theory]
    [InlineData("kinematic hull", false)]
    [InlineData("kinematic hull", true)]
    [InlineData("no body", false)]
    [InlineData("no body", true)]
    public void ANewFrameOnRailsWithAFirstMotionIntentStandsOnItsRailsAtTheFirstSyncPointAfterItsSpawn(string body,
        bool inTick)
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        // A circular orbit of 50 km moves at 632 m/s, too slow and too near for a numeric frame.
        KeplerOrbit orbit = KeplerOrbit.Circular(50_000, phase: 0.3);
        double mu = KeplerOrbit.MuFrom(world.Frames.Gravity(space));
        BodyDesc? hull = body == "kinematic hull" ? Box(BodyMotion.Kinematic) : null;
        var desc = new SpawnDesc { Frame = space, Body = hull, AsFrame = SmallBubble };
        Entity owner;
        if (inTick)
        {
            var spawner = new OrbitSpawner(desc, orbit);
            world.Spawn(new() { Frame = space, Components = [spawner] });
            world.Tick();
            owner = spawner.Spawned;
        }
        else
        {
            owner = world.Spawn(desc.OnKeplerOrbit(world, orbit, firstMove: true));
        }

        FrameHandle frame = world.GetOwnedFrame(owner);

        world.Tick();

        world.Frames.Followed(frame).Should()
            .Be(FollowKind.Rails, "the frame followed its first motion intent in its first step");
        for (int tick = 0; tick < 10; tick++)
        {
            world.Frames.LocalPose(frame).Should().Be(
                new DPose(orbit.StateAt(world.Time, mu).Position, orbit.RotationAt(world.Time, mu)),
                "the frame stands on its orbit from the first sync point after its spawn on, and its KeplerMotion's first teleport moves nothing");
            world.Tick();
        }

        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheContentsOfAStationSpawnedInOrbitWithItsFirstMotionIntentFeelNoGravity()
    {
        using World world = NewWorld(out _);
        FrameHandle planet = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(Scenes.EarthMu)));
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = Box(BodyMotion.Kinematic),
            AsFrame = new(FrameBounds.Sphere(20))
        }.OnKeplerOrbit(world, KeplerOrbit.Circular(StationRadius), firstMove: true)));
        var start = new Double3(0, 3, 0);
        Entity crate = world.Spawn(new()
            { Frame = station, Pose = new(start, DQuaternion.Identity), Body = Ball(0.25f) });

        for (int tick = 0; tick < 120; tick++)
        {
            world.Tick();
            world.Frames.ProperAcceleration(station).Should().Be(Vector3.Zero,
                "the station falls freely on its orbit from its first step");
        }

        world.GetFrame(crate).Should().Be(station);
        (world.PoseInOwnFrame(crate).Position - start).Length.Should()
            .BeLessThan(1e-9, "nothing pulled on the crate in the station");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("end pose on a kinematic body", false)]
    [InlineData("end pose on a kinematic body", true)]
    [InlineData("steer target on a dynamic body", false)]
    [InlineData("steer target on a dynamic body", true)]
    public void AFirstMotionIntentActsInTheFirstStepAsAnIntentOfTheSpawnsTickWould(string kind, bool inTick)
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        bool steer = kind == "steer target on a dynamic body";
        var desc = new SpawnDesc
        {
            Frame = space,
            Body = steer ? Ball() : Ball() with { Motion = BodyMotion.Kinematic },
            FirstMove = steer ? new(new SteerTarget(Turned)) : new MotionIntent(new EndPose(Turned))
        };
        Entity body = SpawnThenTick(world, space, desc, inTick);

        world.Tick();

        // Nothing stands between the ball and its pose, and no gravity acts in space, so a steer target carries the dynamic
        // ball there too, to the rounding of Jolt's float velocities.
        DPose pose = world.PoseInOwnFrame(body);
        (pose.Position - Turned.Position).Length.Should()
            .BeLessThan(steer ? 1e-4 : 1e-6, "the body ends its first step on the pose");
        Math.Abs(Dot(pose.Rotation, Turned.Rotation)).Should()
            .BeApproximately(1, 1e-6, "the body turned to the pose's rotation");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("kinematic body with an end pose")]
    [InlineData("frame without a body with a full sample")]
    public void AFirstMotionIntentActsInOneStepOnly(string kind)
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        bool frame = kind == "frame without a body with a full sample";
        var moved = new DPose(new(0.3, 0, 0), DQuaternion.Identity);
        var sample = new MotionSample(moved, new(18, 0, 0), new(18, 0, 0), Double3.Zero, Double3.Zero, Double3.Zero);
        Entity entity = world.Spawn(new()
        {
            Frame = space,
            Body = frame ? null : Ball() with { Motion = BodyMotion.Kinematic },
            AsFrame = frame ? SmallBubble : null,
            FirstMove = frame ? new(sample) : new MotionIntent(new EndPose(moved))
        });
        world.Tick();
        (world.PoseIn(entity, space).Position - moved.Position).Length.Should()
            .BeLessThan(1e-6, "the first step carried it");

        world.Tick();

        (world.PoseIn(entity, space).Position - moved.Position).Length.Should()
            .BeLessThan(1e-6, "no motion intent followed the first");
        world.VelocityIn(entity, space).Should().Be(Double3.Zero, "it holds still in its second step");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFastHullLandsItsFollowingFrameOnItsFirstMotionIntentAtTheFirstSyncPoint()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        double mu = KeplerOrbit.MuFrom(world.Frames.Gravity(space));
        KeplerOrbit orbit = KeplerOrbit.Circular(5_000);
        FrameHandle frame = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = Box(BodyMotion.Kinematic),
            AsFrame = SmallBubble
        }.OnKeplerOrbit(world, orbit, firstMove: true)));
        MotionSample first = orbit.Sample(mu, world.TimeAt(world.TickNumber + 1), world.DeltaTime);

        world.Tick();

        world.Frames.InFollowingFrame(frame).Should().BeTrue("a hull at 2 km/s gets a following frame at its spawn");
        FrameHandle following = world.Frames.TreeParent(frame);
        world.Frames.TreeLocalPose(following).Position.Should().Be(first.Pose.Position,
            "the following frame landed on the first motion intent");
        world.Frames.LinearVelocity(following).Should().Be(first.ChordVelocity);
        world.Frames.TreeLocalPose(frame).Position.Should()
            .Be(Double3.Zero, "the hull stands at the origin of its following frame");
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("attached entity", false)]
    [InlineData("attached entity", true)]
    [InlineData("character", false)]
    [InlineData("character", true)]
    [InlineData("end pose on a dynamic body", false)]
    [InlineData("end pose on a dynamic body", true)]
    [InlineData("full sample on a dynamic hull", false)]
    [InlineData("full sample on a dynamic hull", true)]
    [InlineData("end pose on a static body", false)]
    [InlineData("end pose on a static body", true)]
    [InlineData("full sample on a static hull", false)]
    [InlineData("full sample on a static hull", true)]
    [InlineData("steer target on a kinematic body", false)]
    [InlineData("steer target on a kinematic body", true)]
    [InlineData("steer target on an entity without a body", false)]
    [InlineData("steer target on an entity without a body", true)]
    [InlineData("steer target on the owner of a root frame", false)]
    [InlineData("steer target on the owner of a root frame", true)]
    public void ASpawnRefusesAFirstMotionIntentThatItsEntityWouldRefuse(string kind, bool inTick)
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        Entity parent = world.Spawn(new() { Frame = space });
        var endPose = new MotionIntent(new EndPose(Turned));
        var sample = new MotionIntent(new MotionSample(Turned, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero,
            Double3.Zero));
        var steer = new MotionIntent(new SteerTarget(Turned));
        (SpawnDesc desc, string reason) = kind switch
        {
            "attached entity" => (new() { AttachTo = parent, FirstMove = endPose }, "*attached*"),
            "character" => (
                new()
                {
                    Frame = space, Body = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character),
                    FirstMove = endPose
                },
                "*character*"),
            "end pose on a dynamic body" => (new() { Frame = space, Body = Ball(), FirstMove = endPose },
                "*Dynamic body*end pose*"),
            "full sample on a dynamic hull" => (
                new() { Frame = space, Body = Box(BodyMotion.Dynamic), AsFrame = SmallBubble, FirstMove = sample },
                "*Dynamic body*full sample*"),
            "end pose on a static body" => (
                new() { Frame = space, Body = Ball() with { Motion = BodyMotion.Static }, FirstMove = endPose },
                "*Static body*end pose*"),
            "full sample on a static hull" => (
                new() { Frame = space, Body = Box(BodyMotion.Static), AsFrame = SmallBubble, FirstMove = sample },
                "*Static body*full sample*"),
            "steer target on a kinematic body" => (
                new() { Frame = space, Body = Ball() with { Motion = BodyMotion.Kinematic }, FirstMove = steer },
                "*Kinematic body*steer target*"),
            "steer target on an entity without a body" => (new() { Frame = space, FirstMove = steer },
                "*no body*steer target*"),
            _ => (new SpawnDesc { Body = Box(BodyMotion.Dynamic), AsFrame = SmallBubble, FirstMove = steer },
                "*root frame*steer target*")
        };

        Exception? refusal = SpawnRefusal(world, space, desc, inTick);

        refusal.Should().BeOfType<ArgumentException>().Which.Message.Should().Match(reason);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A spawn takes a first motion intent that its entity would take from its writer, by the same guard
    /// (see <see cref="World.MotionIntentProblem"/>): an end pose and a full sample on a kinematic body, hull or plain, on an
    /// entity without a body, and on the owner of a root frame.
    /// </summary>
    [Theory]
    [InlineData("full sample on a plain kinematic body", false)]
    [InlineData("full sample on a plain kinematic body", true)]
    [InlineData("end pose on a kinematic hull", false)]
    [InlineData("end pose on a kinematic hull", true)]
    [InlineData("end pose on an entity without a body", false)]
    [InlineData("full sample on an entity without a body", true)]
    [InlineData("end pose on the owner of a root frame without a body", false)]
    public void ASpawnTakesAFirstMotionIntentThatItsEntityTakes(string kind, bool inTick)
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var endPose = new MotionIntent(new EndPose(Turned));
        var sample = new MotionIntent(new MotionSample(Turned, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero,
            Double3.Zero));
        SpawnDesc desc = kind switch
        {
            "full sample on a plain kinematic body" => new()
                { Frame = space, Body = Ball() with { Motion = BodyMotion.Kinematic }, FirstMove = sample },
            "end pose on a kinematic hull" => new()
                { Frame = space, Body = Box(BodyMotion.Kinematic), AsFrame = SmallBubble, FirstMove = endPose },
            "end pose on an entity without a body" => new() { Frame = space, FirstMove = endPose },
            "full sample on an entity without a body" => new() { Frame = space, FirstMove = sample },
            _ => new() { AsFrame = SmallBubble, FirstMove = endPose }
        };

        SpawnRefusal(world, space, desc, inTick).Should().BeNull();
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("end pose at a place that is not finite")]
    [InlineData("end pose with a zero rotation")]
    [InlineData("full sample with a velocity that is not finite")]
    [InlineData("full sample with a zero rotation")]
    [InlineData("steer target with a rotation that is not finite")]
    public void ASpawnRefusesAFirstMotionIntentWithANumberThatIsNotFiniteOrAZeroRotation(string kind)
    {
        using World world = NewWorld(out _);
        FrameHandle space = Space(world);
        var zero = new DQuaternion(0, 0, 0, 0);
        BodyDesc kinematic = Ball() with { Motion = BodyMotion.Kinematic };
        var at = new DPose(Double3.Zero, DQuaternion.Identity);
        SpawnDesc desc = kind switch
        {
            "end pose at a place that is not finite" => new()
            {
                Frame = space, Body = kinematic,
                FirstMove = new(new EndPose(new(new(double.NaN, 0, 0), DQuaternion.Identity)))
            },
            "end pose with a zero rotation" => new()
                { Frame = space, Body = kinematic, FirstMove = new(new EndPose(new(Double3.Zero, zero))) },
            "full sample with a velocity that is not finite" => new()
            {
                Frame = space, AsFrame = SmallBubble,
                FirstMove = new(new MotionSample(at, Double3.Zero, new(0, double.PositiveInfinity, 0), Double3.Zero,
                    Double3.Zero, Double3.Zero))
            },
            "full sample with a zero rotation" => new()
            {
                Frame = space, AsFrame = SmallBubble,
                FirstMove = new(new MotionSample(new(Double3.Zero, zero), Double3.Zero, Double3.Zero, Double3.Zero,
                    Double3.Zero, Double3.Zero))
            },
            _ => new()
            {
                Frame = space, Body = Ball(),
                FirstMove = new(new SteerTarget(new(Double3.Zero, new(0, double.NaN, 0, 1))))
            }
        };

        world.Invoking(w => w.Spawn(desc)).Should().Throw<ArgumentException>();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFirstMotionIntentTakesItsRotationAtUnitLength()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        DQuaternion turn = DQuaternion.FromAxisAngle(Double3.UnitZ, 0.4);
        var doubled = new DQuaternion(2 * turn.X, 2 * turn.Y, 2 * turn.Z, 2 * turn.W);
        FrameHandle frame = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = space,
            AsFrame = SmallBubble,
            FirstMove = new(new MotionSample(new(Double3.Zero, doubled), Double3.Zero, Double3.Zero, Double3.Zero,
                Double3.Zero, Double3.Zero))
        }));
        Entity body = world.Spawn(new()
        {
            Frame = space,
            Pose = At(5),
            Body = Ball() with { Motion = BodyMotion.Kinematic },
            FirstMove = new(new EndPose(new(new(5, 0, 0), doubled)))
        });

        world.Tick();

        world.Frames.LocalPose(frame).Rotation.Should().Be(doubled.Normalized(),
            "the frame takes its first motion intent with a unit rotation");
        faults.Intents.Single(record => record.Entity == body).Intent.Move.AimPose.Rotation.IsUnit.Should().BeTrue(
            "the World stores the first motion intent with a unit rotation");
        world.Errors.Should().BeEmpty();
    }

    private static double Dot(DQuaternion a, DQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    /// <summary>
    /// Spawns <paramref name="desc"/> outside a tick, or with <paramref name="inTick"/> by a component in a tick, so that it
    /// becomes live at the sync point of that tick. Either way the next tick's step is the entity's first step.
    /// </summary>
    private static Entity SpawnThenTick(World world, FrameHandle space, SpawnDesc desc, bool inTick)
    {
        if (!inTick)
            return world.Spawn(desc);
        var spawner = new OnceSpawner(desc);
        world.Spawn(new() { Frame = space, Components = [spawner] });
        world.Tick();
        spawner.Refusal.Should().BeNull();
        return spawner.Spawned;
    }

    /// <summary>The exception that a spawn of <paramref name="desc"/> throws, outside a tick or through
    /// <see cref="Self.Spawn(SpawnDesc)"/>.</summary>
    private static Exception? SpawnRefusal(World world, FrameHandle space, SpawnDesc desc, bool inTick)
    {
        if (!inTick)
            try
            {
                world.Spawn(desc);
                return null;
            }
            catch (Exception refusal)
            {
                return refusal;
            }

        var spawner = new OnceSpawner(desc);
        world.Spawn(new() { Frame = space, Components = [spawner] });
        world.Tick();
        return spawner.Refusal;
    }

    /// <summary>Spawns its description once, in its first tick, and keeps the new entity or the exception that the spawn threw.</summary>
    private sealed class OnceSpawner(SpawnDesc desc) : IComponent
    {
        private bool done;

        public Entity Spawned { get; private set; }

        public Exception? Refusal { get; private set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (done)
                return;
            done = true;
            try
            {
                Spawned = self.Spawn(desc);
            }
            catch (Exception refusal)
            {
                Refusal = refusal;
            }
        }
    }

    /// <summary>
    /// Spawns, once, in its first tick, the frame of its description on <paramref name="orbit"/> around the description's
    /// frame: at the orbit's pose where the frame becomes live, at the sync point of the tick, and with the orbit's sample
    /// of the step after it as its first motion intent, as a component that spawns a station in orbit does.
    /// </summary>
    private sealed class OrbitSpawner(SpawnDesc desc, KeplerOrbit orbit) : IComponent
    {
        public Entity Spawned { get; private set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (!Spawned.IsNull)
                return;
            double mu = KeplerOrbit.MuFrom(self.Snapshot.Frames.Gravity(desc.Frame));
            double live = self.TimeAt(self.Tick + 1);
            (Double3 place, Double3 velocity) = orbit.StateAt(live, mu);
            // Only a kinematic hull takes the orbit's velocities: a spawn refuses a velocity on an entity without a body,
            // which only its motion intents move.
            bool hull = desc.Body is not null;
            Spawned = self.Spawn(new()
            {
                Frame = desc.Frame,
                Pose = new(place, orbit.RotationAt(live, mu)),
                LinearVelocity = hull ? velocity : Double3.Zero,
                AngularVelocity = hull ? orbit.AngularVelocityAt(live, mu).ToVector3() : Vector3.Zero,
                FirstMove = new(orbit.Sample(mu, self.TimeAt(self.Tick + 2), self.DeltaTime)),
                Body = desc.Body,
                AsFrame = desc.AsFrame,
                Components = [new KeplerMotion { Orbit = orbit }]
            });
        }
    }
}
