using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A sleeping body wakes when what holds it goes away without Jolt simulating the move: its support is destroyed or
/// teleported, or the frame tree moves a static hull. Jolt wakes a sleeping body only when a simulated body touches it,
/// and nothing touches a body whose support vanished.
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

        world.GetPose(box).Position.Y.Should().BeApproximately(0.5, 0.01, "nothing holds the box up any more, so it falls to the floor");
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
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), Mass: 1000),
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

        world.GetPose(lander).Position.Y.Should().BeApproximately(0.5, 0.01, "the platform went away, so the lander falls to the floor");
        world.Errors.Should().BeEmpty();
    }

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
