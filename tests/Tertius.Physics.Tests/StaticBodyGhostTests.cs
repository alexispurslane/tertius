using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A static body that is not a hull has a ghost in each child frame whose content it reaches, as a hull has, so that the
/// bodies of the child meet it (ticket ter-xxmj, docs/plans/complete-ghosts.md). The content of a frame is its bubble, grown
/// until it holds the bounding sphere of every member that can move. The ghost has the static body's own layer, so it meets
/// hulls too (fork F1), and it is static in Jolt while it holds still in its system. Each scene runs across frames and with
/// the same bodies in one frame, and both runs hear the same events.
/// </summary>
public class StaticBodyGhostTests
{
    private const float G = 9.81f;

    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    /// <summary>A static box with half extents <paramref name="half"/> at <paramref name="pose"/>, with <paramref name="log"/> on it.</summary>
    private static Entity StaticBox(World world, FrameHandle frame, DPose pose, Vector3 half, CollisionLog? log = null) => world.Spawn(new SpawnDesc
    {
        Frame = frame,
        Pose = pose,
        Body = new BodyDesc(new BoxShapeDesc(half), BodyMotion.Static),
        Components = log is null ? [] : [log],
    });

    /// <summary>A frame without a body in <paramref name="parent"/> at <paramref name="pose"/>, with a bubble of <paramref name="radius"/> m.</summary>
    private static FrameHandle Bubble(World world, FrameHandle parent, DPose pose, float radius, GravityField gravity = default) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = parent, Pose = pose, AsFrame = new FrameSpec(FrameBounds.Sphere(radius), gravity) }));

    // ---- A person boards a parked shuttle ----

    /// <summary>
    /// The station's frame holds a static slab of 40 m by 40 m whose top is at y = 0. A parked shuttle, with a static hull
    /// 5 m above the slab and a bubble of 6 m, holds part of the slab in its bubble. A person walks along +X at 2 m/s from
    /// the station's origin, into the shuttle's bubble, and moves into the shuttle's frame at x = 5.6 m. The slab's ghost in
    /// the shuttle carries the person's contact with the slab, so the person keeps standing on it and hears no
    /// CollisionEnded, as on the slab in one frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APersonWhoWalksIntoAParkedShuttlesBubbleStaysOnTheSlabUnderItAndHearsNoEnd(bool acrossFrames)
    {
        using World world = NewWorld(out _);
        var gravity = GravityField.Uniform(new Vector3(0, -G, 0));
        FrameHandle station = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), gravity));
        var slabLog = new CollisionLog();
        Entity slab = StaticBox(world, station, At(0, -0.5), new Vector3(20, 0.5f, 20), slabLog);
        FrameHandle shuttle = FrameHandle.Null;
        if (acrossFrames)
        {
            shuttle = world.GetOwnedFrame(world.Spawn(new SpawnDesc
            {
                Frame = station,
                Pose = At(10, 5),
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static, InteriorShape: new SphereShapeDesc(0.5f)),
                AsFrame = new FrameSpec(FrameBounds.Sphere(6), gravity),
            }));
        }
        var personLog = new CollisionLog();
        var pilot = new Pilot { EveryTick = handle => handle.MoveCharacter(new Vector3(2, 0, 0)) };
        Entity person = world.Spawn(new SpawnDesc { Frame = station, Pose = At(0, 0.9), Body = Person, Components = [personLog, pilot] });

        for (int i = 0; i < 6; i++)
        {
            Tick(world, 60);
            world.PoseIn(person, station).Position.Y.Should().BeInRange(0.85, 1.0, "the person stands on the slab");
        }

        world.PoseIn(person, station).Position.X.Should().BeInRange(11, 12.5);
        world.GetFrame(person).Should().Be(acrossFrames ? shuttle : station);
        personLog.NewsOf(slab).Should().Equal(Started);
        slabLog.NewsOf(person).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }

    // ---- A crate at the edge of a bubble ----

    /// <summary>
    /// Space, with no gravity. A frame without a body at the origin has a bubble of 5 m. A crate of the frame slides along
    /// +X at 1 m/s from x = 3 m. A static rock of space lies at x = 6.5 m, with its near face at x = 6 m: its bounding sphere
    /// of 0.87 m ends 0.63 m short of the bubble. Once the crate sticks out of the bubble, the frame's content reaches the
    /// rock, so the rock has a ghost in the frame (fork F2), and the crate stops at it with its center 0.5 m outside the
    /// bubble, still in the frame, as it stops at the rock in one frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACrateThatSticksOutOfABubbleStopsAtAStaticRockOfTheParentBeyondTheBubblesEdge(bool acrossFrames)
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle home = acrossFrames ? Bubble(world, space, At(0), 5) : space;
        var rockLog = new CollisionLog();
        Entity rock = StaticBox(world, space, At(6.5), new Vector3(0.5f), rockLog);
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = home,
            Pose = At(3),
            LinearVelocity = new Vector3(1, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            Components = [crateLog],
        });

        double farthest = double.MinValue;
        for (int i = 0; i < 240; i++)
        {
            world.Tick();
            farthest = Math.Max(farthest, world.PoseIn(crate, space).Position.X);
        }

        farthest.Should().BeLessThan(5.55, "the crate stops with its front at the rock's face");
        world.GetFrame(crate).Should().Be(home);
        crateLog.NewsOf(rock).Should().Equal(Started);
        rockLog.NewsOf(crate).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Space, with no gravity. A frame without a body at the origin has a bubble of 5 m. A kinematic ball of radius 0.5 m
    /// moves along +X at 1 m/s from x = -8 m, and pushes a crate that rests against it into the bubble. When the crate moves
    /// into the frame, the ball's bounding sphere does not reach the bubble yet, but it reaches the frame's content, which the
    /// crate grows as it sticks out (fork F2). So the ball has a ghost in the frame already, which carries the contact and
    /// pushes the crate on at 1 m/s, as the ball pushes it in one frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AKinematicPusherKeepsPushingACrateThatEntersAFrameAheadOfIt(bool acrossFrames)
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle frame = acrossFrames ? Bubble(world, space, At(0), 5) : FrameHandle.Null;
        Entity pusher = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(-8),
            LinearVelocity = new Vector3(1, 0, 0),
            Body = new BodyDesc(new SphereShapeDesc(0.5f), BodyMotion.Kinematic),
        });
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(-7),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, Friction: 0, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            Components = [crateLog],
        });

        Tick(world, 300);

        if (acrossFrames)
            world.GetFrame(crate).Should().Be(frame);
        double gap = world.PoseIn(crate, space).Position.X - world.PoseIn(pusher, space).Position.X;
        gap.Should().BeApproximately(1, 0.05, "the crate rests against the ball, which pushes it");
        world.VelocityIn(crate, space).X.Should().BeApproximately(1, 0.05);
        crateLog.NewsOf(pusher).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Space, with no gravity. Two frames without bodies lie side by side with bubbles of 5 m, at x = 0 and x = 11, so their
    /// bubbles are 1 m apart. A crate of the first frame rests at x = 5.3, sticking out of its bubble. A kinematic ball of
    /// radius 0.5 m of the second frame moves along -X at 1 m/s from x = 6.6, sticking out of its own bubble. It meets the
    /// crate 1.3 m outside the first bubble, beyond its radius but within the first frame's content, which the crate grows.
    /// So the ball's stick-out ghost in the first frame pushes the crate on at 1 m/s (fork F2), as the ball pushes it in one
    /// frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AKinematicBallThatSticksOutOfAFramePushesACrateThatSticksOutOfASiblingFrame(bool acrossFrames)
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle first = acrossFrames ? Bubble(world, space, At(0), 5) : space;
        FrameHandle second = acrossFrames ? Bubble(world, space, At(11), 5) : space;
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = first,
            Pose = At(5.3),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, Friction: 0, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            Components = [crateLog],
        });
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = second,
            Pose = acrossFrames ? At(6.6 - 11) : At(6.6),
            LinearVelocity = new Vector3(-1, 0, 0),
            Body = new BodyDesc(new SphereShapeDesc(0.5f), BodyMotion.Kinematic),
        });

        // The ball reaches the crate after 0.3 s, and pushes it from then on.
        for (int i = 0; i < 2; i++)
        {
            Tick(world, 45);
            double gap = world.PoseIn(ball, space).Position.X - world.PoseIn(crate, space).Position.X;
            gap.Should().BeApproximately(1, 0.05, "the crate rests against the ball, which pushes it");
            world.VelocityIn(crate, space).X.Should().BeApproximately(-1, 0.05);
        }
        crateLog.NewsOf(ball).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }

    // ---- Queries ----

    /// <summary>The asker of one ray from <paramref name="origin"/> in its frame, which keeps the hits that it hears.</summary>
    private sealed class Aim(Double3 origin, Double3 direction, double length) : IComponent
    {
        public List<RayHit> Hits { get; } = new();

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tertius.Tick>().And<RayHit>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref RayHit hit in events.Of<RayHit>())
                Hits.Add(hit);
            if (self.Tick == 1)
                self.Physics.CastRay(origin, direction, length, default, 1);
        }
    }

    /// <summary>
    /// Space, with no gravity. A frame without a body at (100, 0, 0) has a bubble of 20 m. A static box of space at
    /// (115, 0, 0) lies inside the bubble. A ray along +X from the frame's origin hits the box's ghost in the frame at 14.5 m,
    /// as a ray from the same place in space hits the box (the known limit of queries-and-sensors.md goes).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARayThatStartsInsideABubbleHitsAStaticBodyOfTheParentInsideTheBubbleAtTheSameDistance(bool acrossFrames)
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle home = acrossFrames ? Bubble(world, space, At(100), 20) : space;
        Entity box = StaticBox(world, space, At(115), new Vector3(0.5f));
        Double3 origin = acrossFrames ? Double3.Zero : new Double3(100, 0, 0);
        var aim = new Aim(origin, Double3.UnitX, 50);
        world.Spawn(new SpawnDesc { Frame = home, Pose = new DPose(origin, DQuaternion.Identity), Components = [aim] });

        Tick(world, 3);

        RayHit hit = aim.Hits.Should().ContainSingle().Subject;
        hit.Entity.Should().Be(box);
        ((double)hit.Distance).Should().BeApproximately(14.5, 1e-4);
        world.Errors.Should().BeEmpty();
    }

    // ---- Numeric frames ----

    /// <summary>
    /// Space, with no gravity. A frame without a body at the origin has a bubble of 600 m. A static slab of space of 600 m
    /// by 600 m lies 200 m below the origin, inside the bubble. A crate of the frame 300 m above the origin is thrown at the
    /// slab at 1,100 m/s, fast enough to fall in a numeric frame of its own. The numeric update links it to the slab's ghost
    /// in the frame's own system, so it stops at the slab, as it stops at the slab in one frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACrateThatFallsFastInsideAFrameStopsAtTheParentsStaticSlab(bool acrossFrames)
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        FrameHandle home = acrossFrames ? Bubble(world, space, At(0), 600) : space;
        var slabLog = new CollisionLog();
        Entity slab = StaticBox(world, space, At(0, -201), new Vector3(300, 1, 300), slabLog);
        var crateLog = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = home,
            Pose = At(0, 300),
            LinearVelocity = new Vector3(0, -1100, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            Components = [crateLog],
        });

        Tick(world, 120);

        world.PoseIn(crate, space).Position.Y.Should().BeApproximately(-199.5, 0.1, "the crate rests on the slab");
        world.GetFrame(crate).Should().Be(home);
        crateLog.NewsOf(slab).Should().Equal(Started);
        slabLog.NewsOf(crate).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }

    // ---- Sleep ----

    /// <summary>
    /// Gravity of 1 g. A static floor of 100 m by 100 m has its top at y = 0. A frame without a body, with the same gravity,
    /// hangs at (0, 2, 0) with a bubble of 5 m, which holds part of the floor. Three crates of the frame rest on the floor.
    /// The floor's ghost in the frame holds still, so it is static in Jolt: the crates fall asleep on it, and the frame's
    /// system stops stepping, as the crates fall asleep on the floor in one frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCratesThatRestOnTheGhostOfAStaticFloorFallAsleepAndTheFramesSystemStopsStepping(bool acrossFrames)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var gravity = GravityField.Uniform(new Vector3(0, -G, 0));
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), gravity));
        Entity floor = Floor(world, ground);
        FrameHandle home = acrossFrames ? Bubble(world, ground, At(0, 2), 5, gravity) : ground;
        double y = acrossFrames ? -1.5 : 0.5;
        Entity[] crates = [.. new[] { -2.0, 0, 2 }.Select(x => world.Spawn(new SpawnDesc { Frame = home, Pose = At(x, y), Body = Cube() }))];

        Tick(world, 180);

        foreach (Entity crate in crates)
        {
            world.PoseIn(crate, ground).Position.Y.Should().BeApproximately(0.5, 0.03, "the crate rests on the floor");
            IsAwake(faults.Jolt, home, crate).Should().BeFalse("the crate rests on a floor that holds still");
        }
        faults.Jolt.SystemOf(home)!.GetNumActiveBodies(BodyType.Rigid).Should().Be(0);
        if (acrossFrames)
            faults.Jolt.HasGhostIn(floor, home).Should().BeTrue();
        // Among them: Jolt can turn the static ghost kinematic, for the time that the frame moves.
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Space, with no gravity of its own. A static floor of 40 m by 40 m, a plain body, has its top at y = 0. A ship hangs at
    /// rest at (12, 20, 0): a dynamic hull, a ball of radius 2 m, with a bubble of 30 m and its own gravity of 1 g along -Y.
    /// The ship's bubble takes in the floor, so the ship's system holds a ghost of it, and a crate of the ship stands on that
    /// ghost. The ghost of a static body is static while it holds still in its system, so that the crate falls asleep on it
    /// as on the real floor, and kinematic while it moves, so that Jolt moves it with its velocity: when the ship rises at
    /// 1 m/s, the ghost moves down at 1 m/s in the ship, and the crate wakes. When the ship stops again, the ghost holds
    /// still again, and the crate falls asleep again, as on the ghost of a mesh (see MeshGhostTests).
    /// </summary>
    [Fact]
    public void TheGhostOfAStaticBodyIsStaticWhileItHoldsStillAndKinematicWhileItMoves()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        Entity floor = StaticBox(world, space, At(0, -0.5), new Vector3(20, 0.5f, 20));
        var pilot = new Pilot();
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(12, 20),
            Body = new BodyDesc(new SphereShapeDesc(2), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30), GravityField.Uniform(new Vector3(0, -G, 0))),
            Components = [pilot],
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(0, -19.5), Body = Cube() });
        JoltPhysicsBackend.BodyFacts Ghost() =>
            faults.Jolt.BodiesIn(ship).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, floor));
        Tick(world, 120);

        Ghost().Motion.Should().Be(MotionType.Static, "the ship hangs at rest above the floor");
        IsAwake(faults.Jolt, ship, crate).Should().BeFalse("the crate stands on a ghost that holds still");
        world.PoseInOwnFrame(crate).Position.Y.Should().BeApproximately(-19.5, 0.03);

        pilot.Once = body => body.SetVelocity(new Double3(0, 1, 0));
        Tick(world, 3);
        JoltPhysicsBackend.BodyFacts moving = Ghost();
        moving.Motion.Should().Be(MotionType.Kinematic, "the ghost moves in the ship's system");
        ((Double3)moving.LinearVelocity - new Double3(0, -1, 0)).Length.Should().BeLessThan(1e-3, "the floor moves down at 1 m/s in the rising ship");
        IsAwake(faults.Jolt, ship, crate).Should().BeTrue("the floor fell away under the crate");

        pilot.Once = body => body.SetVelocity(Double3.Zero);
        Tick(world, 180);
        Ghost().Motion.Should().Be(MotionType.Static, "the ship hangs at rest again");
        IsAwake(faults.Jolt, ship, crate).Should().BeFalse("the crate stands on the still ghost again");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    // ---- Hulls ----

    /// <summary>
    /// Gravity of 1 g. A static slab of 40 m by 40 m has its top at y = 0. A frame without a body, with the same gravity,
    /// hangs at (0, 5, 0) with a bubble of 20 m, which holds part of the slab. A pod, a frame with a dynamic hull of 1 m and
    /// a bubble of 1.5 m, starts at rest 3 m above the slab in that frame. Its hull lives in the frame's system, where the
    /// slab's ghost has the slab's own layer, which meets hulls (fork F1). So the hull lands on it, as it lands on the slab
    /// in one frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADynamicHullOfAFrameInsideAFrameLandsOnTheGhostOfTheParentsStaticSlab(bool acrossFrames)
    {
        using World world = NewWorld(out _);
        var gravity = GravityField.Uniform(new Vector3(0, -G, 0));
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), gravity));
        var slabLog = new CollisionLog();
        Entity slab = StaticBox(world, ground, At(0, -0.5), new Vector3(20, 0.5f, 20), slabLog);
        FrameHandle home = acrossFrames ? Bubble(world, ground, At(0, 5), 20, gravity) : ground;
        var podLog = new CollisionLog();
        Entity pod = world.Spawn(new SpawnDesc
        {
            Frame = home,
            Pose = acrossFrames ? At(0, -2) : At(0, 3),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 100, Restitution: 0, InteriorShape: new SphereShapeDesc(0.1f)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1.5f), gravity),
            Components = [podLog],
        });

        Tick(world, 180);

        world.PoseIn(pod, ground).Position.Y.Should().BeApproximately(0.5, 0.03, "the pod's hull rests on the slab");
        world.GetFrame(pod).Should().Be(home);
        podLog.NewsOf(slab).Should().Equal(Started);
        slabLog.NewsOf(pod).Should().Equal(Started);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The scene of <see cref="ADynamicHullOfAFrameInsideAFrameLandsOnTheGhostOfTheParentsStaticSlab"/>. Once the pod rests,
    /// the slab leaves Solid by an intent. A hull meets every static body, whatever its groups, so the pod keeps resting on
    /// the slab's ghost, which takes the slab's new groups in the layer of its role, as it keeps resting on the slab in one
    /// frame.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APodKeepsRestingOnTheGhostOfAStaticSlabThatChangesItsGroups(bool acrossFrames)
    {
        var registry = new CollisionGroupRegistry();
        CollisionGroup glass = registry.Register("Glass");
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var gravity = GravityField.Uniform(new Vector3(0, -G, 0));
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), gravity));
        var slabPilot = new Pilot();
        world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(20, 0.5f, 20)), BodyMotion.Static),
            Components = [slabPilot],
        });
        FrameHandle home = acrossFrames ? Bubble(world, ground, At(0, 5), 20, gravity) : ground;
        Entity pod = world.Spawn(new SpawnDesc
        {
            Frame = home,
            Pose = acrossFrames ? At(0, -2) : At(0, 3),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 100, Restitution: 0, InteriorShape: new SphereShapeDesc(0.1f)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1.5f), gravity),
        });
        Tick(world, 120);
        world.PoseIn(pod, ground).Position.Y.Should().BeApproximately(0.5, 0.03, "the pod's hull rests on the slab");

        slabPilot.Once = body => body.SetCollision(CollisionGroups.Of(glass), CollisionMask.Empty.InAny(glass));
        Tick(world, 120);

        world.PoseIn(pod, ground).Position.Y.Should().BeApproximately(0.5, 0.03, "a hull meets every static body");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Space, with no gravity. Two frames without bodies lie side by side with bubbles of 20 m, at x = 0 and x = 43, so
    /// their bubbles are 3 m apart. A pod, a frame with a dynamic hull of radius 1.73 m and a bubble of 6 m, starts in the
    /// first frame at x = 19 and drifts along +X at 1 m/s. It sticks out of the first bubble, so once it reaches the second
    /// bubble it has a stick-out ghost there, in the ghost role. It leaves the first frame into space beyond x = 23.46,
    /// where it reaches the second bubble with its own bubble, so that ghost turns into a child ghost of a hull, which has
    /// the static role of the pod's interior and meets hulls (fork F1). A ghost keeps its role, so the backend makes it
    /// again.
    /// </summary>
    [Fact]
    public void AHullThatLeavesAFrameMakesItsGhostInASiblingAgainInTheRoleOfAChildGhost()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle first = Bubble(world, space, At(0), 20);
        FrameHandle second = Bubble(world, space, At(43), 20);
        Entity pod = world.Spawn(new SpawnDesc
        {
            Frame = first,
            Pose = At(19),
            LinearVelocity = new Vector3(1, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(0.1f)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(6)),
        });
        FrameHandle inside = world.GetOwnedFrame(pod);
        JoltPhysicsBackend.BodyFacts GhostIn(FrameHandle frame) =>
            faults.Jolt.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, pod));

        TickUntil(world, () => faults.Jolt.HasGhostIn(pod, second), 240);
        world.GetFrame(pod).Should().Be(first);
        ObjectLayer interior = faults.Jolt.BodiesIn(inside).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Interior).Layer;
        GhostIn(second).Layer.Should().NotBe(interior, "a stick-out ghost has the ghost role");

        TickUntil(world, () => world.GetFrame(pod) == space, 240);

        world.GetFrame(pod).Should().Be(space);
        GhostIn(second).Layer.Should().Be(interior, "a child ghost of a hull has the layer of its interior");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }
}
