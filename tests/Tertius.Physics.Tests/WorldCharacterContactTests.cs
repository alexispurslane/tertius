using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Contacts of a character with a box that falls asleep. Jolt never simulates the inner body of a character, and it does
/// not simulate a sleeping box. So Jolt cannot tell whether such a pair still touches. Also, a character keeps a gap of
/// 2 cm from what it touches, which is Jolt's speculative contact distance, so Jolt loses the contact and finds it again
/// while the character pushes an awake box. The character's own contact list knows what the character touches.
/// </summary>
public class WorldCharacterContactTests
{
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    /// <summary>A heavy box, which the character cannot push: friction holds 0.5 · 500 kg · g, and a character pushes with 100 N.</summary>
    private static readonly BodyDesc HeavyBox = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 500);

    /// <summary>
    /// The reviewer's probe C11. A character walks against a heavy box and stands there until the box falls asleep. It
    /// walks 3 m away and comes back against the box. The two touched twice, so each hears the other twice. A body that
    /// moves far away keeps the ground's system stepping, or nothing does.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACharacterThatLeavesASleepingBoxAndComesBackHearsItAgain(bool keepStepping)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        if (keepStepping)
            world.Spawn(new SpawnDesc { Frame = ground, Pose = At(-40, 20), LinearVelocity = new Vector3(0, 0, 0.1f), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Kinematic) });
        var boxLog = new CollisionLog();
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1, 0.5), Body = HeavyBox, Components = [boxLog] });
        var pilot = new Pilot();
        var personLog = new CollisionLog();
        Entity person = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.95), Body = Person, Components = [pilot, personLog] });

        pilot.EveryTick = body => body.MoveCharacter(new Vector3(0.5f, 0, 0));
        Tick(world, 120);
        pilot.EveryTick = body => body.MoveCharacter(Vector3.Zero);
        Tick(world, 60);
        IsAwake(backend, ground, box).Should().BeFalse("the box fell asleep while the character stood against it");
        personLog.CountWith(box).Should().Be(1, "the character walked into the box once");

        pilot.EveryTick = body => body.MoveCharacter(new Vector3(-1.5f, 0, 0));
        Tick(world, 120);
        world.GetPose(person).Position.X.Should().BeLessThan(-2, "the character walked 3 m away");
        IsAwake(backend, ground, box).Should().BeFalse("nothing touched the box while the character was away");
        pilot.EveryTick = body => body.MoveCharacter(new Vector3(1.5f, 0, 0));
        Tick(world, 150);

        personLog.CountWith(box).Should().Be(2, "the character left the box and touched it again");
        boxLog.CountWith(person).Should().Be(2);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A character walks against a heavy box and stands there until the box falls asleep, then pushes against it again
    /// without ever stepping away. The two never separated, so each hears the other once.
    /// </summary>
    [Fact]
    public void ACharacterThatStaysAgainstASleepingBoxAndPushesAgainHearsItOnce()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(1, 0.5), Body = HeavyBox });
        var pilot = new Pilot();
        var personLog = new CollisionLog();
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.95), Body = Person, Components = [pilot, personLog] });

        pilot.EveryTick = body => body.MoveCharacter(new Vector3(0.5f, 0, 0));
        Tick(world, 120);
        pilot.EveryTick = body => body.MoveCharacter(Vector3.Zero);
        Tick(world, 60);
        IsAwake(backend, ground, box).Should().BeFalse("the box fell asleep while the character stood against it");
        pilot.EveryTick = body => body.MoveCharacter(new Vector3(0.5f, 0, 0));
        Tick(world, 120);

        personLog.CountWith(box).Should().Be(1, "the character never left the box");
        world.Errors.Should().BeEmpty();
    }
}
