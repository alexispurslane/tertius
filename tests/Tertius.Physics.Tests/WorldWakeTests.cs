using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A sleeping body wakes when what holds it goes away without Jolt simulating the move: its support is destroyed or
/// teleported, or the frame tree or its owner's teleport moves a static hull. Jolt wakes a sleeping body only when a
/// simulated body touches it, and nothing touches a body whose support vanished.
/// </summary>
public class WorldWakeTests
{
    /// <summary>
    /// The reviewer's probes W10 and W10w. A box falls asleep on a support whose top is 2 m above the floor. Then the
    /// support goes: World.Destroy removes it, or a teleport puts it 20 m away. The box falls to the floor.
    /// </summary>
    [Theory]
    [InlineData("destroy a static table")]
    [InlineData("destroy a dynamic crate")]
    [InlineData("teleport a dynamic crate away")]
    [InlineData("teleport a kinematic platform away")]
    public void ASleepingBoxFallsWhenItsSupportGoesAway(string how)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        BodyDesc supportDesc = how switch
        {
            "destroy a static table" => new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static),
            "teleport a kinematic platform away" => new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic),
            _ => new BodyDesc(new BoxShapeDesc(new Vector3(1)), Mass: 100),
        };
        var pilot = new Pilot();
        Entity support = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 1), Body = supportDesc, Components = [pilot] });
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 2.5), Body = Cube() });
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, ground, box), 600);
        Tick(world, 30);
        IsAwake(backend, ground, box).Should().BeFalse("the box fell asleep on its support");

        if (how.StartsWith("destroy"))
            world.Destroy(support);
        else
            pilot.Once = body => body.Teleport(At(20, 1));
        Tick(world, 180);

        world.PoseInOwnFrame(box).Position.Y.Should().BeApproximately(0.5, 0.01, "nothing holds the box up any more, so it falls to the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A lander falls asleep on the roof of a parked platform. Both are frames with hulls in the ground frame, and neither
    /// bubble fits into the other, so the two hulls touch in the ground frame. The platform has the static driver, so its
    /// hull is static. Between two ticks, the game moves the platform 20 m away through the frame tree. Physics puts the
    /// static hull there without simulating the move, and the lander falls to the floor.
    /// </summary>
    [Fact]
    public void ALanderAsleepOnAParkedPlatformFallsWhenTheGameMovesThePlatformAway()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity platformOwner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 1),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static, Mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(3), MotionDriver.Static),
        });
        FrameHandle platform = world.GetOwnedFrame(platformOwner);
        Entity lander = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 2.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 100),
            AsFrame = new FrameSpec(FrameBounds.Sphere(3), MotionDriver.Physics),
        });
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, ground, lander), 600);
        Tick(world, 30);
        IsAwake(backend, ground, lander).Should().BeFalse("the lander fell asleep on the platform");

        world.Frames.SetLocalPose(platform, At(20, 1));
        Tick(world, 180);

        world.PoseInOwnFrame(lander).Position.Y.Should().BeApproximately(0.5, 0.01, "the platform went away, so the lander falls to the floor");
        world.Errors.Should().BeEmpty();
    }

    // ---- A static hull follows its owner's teleport (docs/plans/rails-components.md, "Frames without a body, and static frames") ----

    /// <summary>
    /// The owner of a parked platform teleports its static hull 20 m away and turns it a quarter turn. The teleport acts in
    /// the step after the tick that wrote it, so the frame stays where it was at that tick's sync point, and stands exactly
    /// at the teleport's pose at the next one, at rest. The hull stands there in Jolt too.
    /// </summary>
    [Fact]
    public void AStaticHullFollowsItsOwnersTeleportAtTheNextSyncPoint()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var pilot = new Pilot();
        Entity owner = Platform(world, ground, pilot);
        FrameHandle platform = world.GetOwnedFrame(owner);
        Tick(world, 3);
        var target = new DPose(new Double3(20, 1, 0.25), DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2));

        pilot.Once = body => body.Teleport(target);
        world.Tick();

        world.Frames.LocalPose(platform).Should().Be(At(0, 1), "the teleport acts in the next step");
        world.Tick();

        world.Frames.LocalPose(platform).Should().Be(target, "the frame takes the teleport's pose, in doubles, at the next sync point");
        world.Frames.LinearVelocity(platform).Should().Be(Double3.Zero);
        world.Frames.AngularVelocity(platform).Should().Be(Vector3.Zero);
        world.PoseInOwnFrame(owner).Should().Be(target, "the owner's row is the float view of its frame");
        HullPosition(backend, ground, owner).Should().Be(target.Position);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A parked platform's frame reads a proper acceleration of 9.81 m/s² up, as the ground holds it. Its owner teleports
    /// the static hull. The teleport moves the frame and changes nothing else, so the frame keeps its proper acceleration, as
    /// a frame that holds still does, and the bodies in it keep their weight.
    /// </summary>
    [Fact]
    public void AStaticHullThatTeleportsKeepsItsFramesProperAcceleration()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        var pilot = new Pilot();
        FrameHandle platform = world.GetOwnedFrame(Platform(world, ground, pilot));
        var weight = new Vector3(0, 9.81f, 0);
        world.Frames.ProperAcceleration(platform) = weight;
        Tick(world, 3);

        pilot.Once = body => body.Teleport(At(20, 1));
        Tick(world, 2);

        world.Frames.LocalPose(platform).Should().Be(At(20, 1));
        world.Frames.ProperAcceleration(platform).Should().Be(weight);
    }

    /// <summary>
    /// Lander A sleeps on the roof of a parked platform, and lander B sleeps on the floor 20 m away. Both are frames with
    /// hulls, which meet the platform's hull. The platform's owner teleports the static hull to just above B. Jolt does not
    /// simulate the jump, so physics wakes the bodies around the hull's old place, A, and around its new place, B, and A
    /// falls to the floor.
    /// </summary>
    [Fact]
    public void ATeleportOfAStaticHullWakesTheSleepingBodiesAroundItsOldAndNewPlaces()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        var pilot = new Pilot();
        Platform(world, ground, pilot);
        Entity landerA = Lander(world, ground, At(0, 2.5));
        Entity landerB = Lander(world, ground, At(20, 0.5));
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, ground, landerA) && !IsAwake(backend, ground, landerB), 600);
        Tick(world, 30);
        IsAwake(backend, ground, landerA).Should().BeFalse("lander A fell asleep on the platform");
        IsAwake(backend, ground, landerB).Should().BeFalse("lander B fell asleep on the floor");

        // The platform's bottom ends 2 cm above B's roof.
        pilot.Once = body => body.Teleport(At(20, 2.02));
        Tick(world, 2);

        IsAwake(backend, ground, landerA).Should().BeTrue("the platform left lander A");
        IsAwake(backend, ground, landerB).Should().BeTrue("the platform came down next to lander B");
        Tick(world, 180);
        world.PoseInOwnFrame(landerA).Position.Y.Should().BeApproximately(0.5, 0.01, "nothing holds lander A up any more, so it falls to the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Between two ticks, the game puts a parked platform's frame 20 m away through the frame tree, which no physics batch
    /// reports. The platform's static hull stands there in Jolt after the next step.
    /// </summary>
    [Fact]
    public void AnEditOfAStaticFramesPoseOutsideATickStillMovesItsHull()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity owner = Platform(world, ground, new Pilot());
        FrameHandle platform = world.GetOwnedFrame(owner);
        Tick(world, 3);

        world.Frames.SetLocalPose(platform, At(20, 1));
        world.Tick();

        HullPosition(backend, ground, owner).Should().Be(new Double3(20, 1, 0));
        world.Frames.LocalPose(platform).Should().Be(At(20, 1));
        world.Errors.Should().BeEmpty();
    }

    /// <summary>The owner of a parked platform at (0, 1, 0) in <paramref name="ground"/>: a static frame whose hull is a box 2 m wide.</summary>
    private static Entity Platform(World world, FrameHandle ground, params IComponent[] components) => world.Spawn(new SpawnDesc
    {
        Frame = ground,
        Pose = At(0, 1),
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static),
        AsFrame = new FrameSpec(FrameBounds.Sphere(3), MotionDriver.Static),
        Components = components,
    });

    /// <summary>The owner of a lander at <paramref name="pose"/> in <paramref name="ground"/>: a frame whose dynamic hull is a box 1 m wide.</summary>
    private static Entity Lander(World world, FrameHandle ground, DPose pose) => world.Spawn(new SpawnDesc
    {
        Frame = ground,
        Pose = pose,
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 100),
        AsFrame = new FrameSpec(FrameBounds.Sphere(3), MotionDriver.Physics),
    });

    /// <summary>Where Jolt holds the hull of <paramref name="owner"/> in the system of <paramref name="frame"/>.</summary>
    private static Double3 HullPosition(JoltPhysicsBackend backend, FrameHandle frame, Entity owner) =>
        backend.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner)).Position;

    /// <summary>
    /// A box falls asleep on the floor next to a sleeping crate, which does not touch it. The crate goes away. The box
    /// stays where it was: a removal wakes only what is near the removed body, and the box then falls asleep again.
    /// </summary>
    [Fact]
    public void ABoxAsleepFarFromARemovedBodySleepsOn()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = Cube() });
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(5, 0.5), Body = Cube() });
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, ground, box) && !IsAwake(backend, ground, crate), 600);

        world.Destroy(crate);
        world.Tick();

        IsAwake(backend, ground, box).Should().BeFalse("the crate never touched the box");
    }
}
