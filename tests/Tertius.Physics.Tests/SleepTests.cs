using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A dynamic body falls asleep only when it really rests. Jolt puts a body to sleep when it moves less than about 3 cm in
/// half a second. A body that gravity has just started to pull moves that little too, when the gravity is weak. So the
/// sleep threshold of a frame's system scales with the frame's gravity, and a body that gravity accelerates stays awake,
/// while a body that something holds against gravity falls asleep. The sleeping bodies of a frame wake when the frame's
/// proper acceleration has moved far enough from its value when they last woke. <see cref="WorldSleepTests"/> has the
/// cases where only a small share of a weak gravity moves the body. These tests run the World on the real backend.
/// </summary>
/// <remarks>
/// The tests that tick 900 times leave the world's checks of its invariants at the end of every tick off, as
/// <see cref="WorldSleepTests"/> does for its long runs. With them on, these tests take 3 s longer in a Debug build.
/// </remarks>
public sealed class SleepTests
{
    /// <summary>A cube of 1 m with no damping and no bounce, so that only gravity and the floor move it.</summary>
    private static BodyDesc Box(float mass) =>
        new(Shape: new BoxShapeDesc(new(0.5f)), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A frame with uniform gravity of <paramref name="g"/> m/s² along -Y, and a floor whose top is at y = 0.</summary>
    private static FrameHandle GroundWithFloor(World world, float g)
    {
        FrameHandle ground = Ground(world, g);
        Floor(world, ground);
        return ground;
    }

    /// <summary>
    /// The reviewer's probe S1. A box of 1 m is let go at rest with its bottom 1 m above the floor. At 0.05 m/s² the fall
    /// takes sqrt(2 / 0.05) = 6.3 s, and the test ticks 15 s.
    /// </summary>
    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.1f)]
    [InlineData(0.2f)]
    [InlineData(0.3f)]
    [InlineData(1.0f)]
    public void ABoxLetGoAtRestInWeakGravityFallsToTheFloor(float g)
    {
        using World world = NewWorld(backend: out _, debugChecks: false);
        FrameHandle ground = GroundWithFloor(world, g);
        Entity box = world.Spawn(new() { Frame = ground, Pose = At(0, 1.5), Body = Box(mass: 10) });

        Tick(world, 900);

        world.PoseInOwnFrame(box).Position.Y.Should().BeApproximately(0.5, 0.01, "the box fell 1 m onto the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Two boxes, one on top of the other, are let go at rest 1 m above the floor in weak gravity. They touch each other all
    /// the way down, so a rule that lets a body sleep while it touches something would leave them asleep in mid-air.
    /// </summary>
    [Fact]
    public void AStackOfTwoBoxesLetGoInWeakGravityFallsToTheFloor()
    {
        using World world = NewWorld(backend: out _, debugChecks: false);
        FrameHandle ground = GroundWithFloor(world, 0.1f);
        Entity bottom = world.Spawn(new() { Frame = ground, Pose = At(0, 1.5), Body = Box(mass: 10) });
        Entity top = world.Spawn(new() { Frame = ground, Pose = At(0, 2.5), Body = Box(mass: 10) });

        Tick(world, 900);

        world.PoseInOwnFrame(bottom).Position.Y.Should()
            .BeApproximately(0.5, 0.02, "the stack fell 1 m onto the floor");
        world.PoseInOwnFrame(top).Position.Y.Should()
            .BeApproximately(1.5, 0.03, "the top box rides the bottom one down");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A body that rests on the floor falls asleep there, in weak gravity as in strong gravity.</summary>
    [Theory]
    [InlineData(0.05f)]
    [InlineData(9.81f)]
    public void ABoxThatLandsOnAFloorFallsAsleepThereWithinTwoSeconds(float g)
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle ground = GroundWithFloor(world, g);
        Entity box = world.Spawn(new() { Frame = ground, Pose = At(0, 0.6), Body = Box(mass: 10) });

        List<ContactStarted> Contacts() => [.. faults.Outputs.SelectMany(output => output.Contacts)];
        // The fall of 0.1 m takes 2 s at 0.05 m/s².
        TickUntil(world, () => Contacts().Count > 0, 300);
        Contacts().Should().ContainSingle("the box landed");

        int ticks = TickUntil(world, () => !IsAwake(faults.Jolt, ground, box), 120);

        ticks.Should().BeLessThan(120, "the box rests on the floor, so it falls asleep within 2 s");
        world.PoseInOwnFrame(box).Position.Y.Should().BeApproximately(0.5, 0.01, "the box sleeps on the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The reviewer's probe S2. A ship in space has artificial gravity of 9.81 m/s² along its -Y, and a crate falls asleep
    /// on its deck. Then the ship's thrust along +Z grows by <paramref name="newtonsPerTick"/> each tick, on 1000 kg, up to
    /// 6 m/s², and holds for 1 s. The crate then feels 6 m/s² toward the stern, more than the 0.5 · 9.81 = 4.9 m/s² that
    /// friction holds, so it slides toward the stern. At 10 N per tick the proper acceleration changes by 0.01 m/s² per
    /// step, half of the wake threshold.
    /// </summary>
    [Theory]
    [InlineData(10f)]
    [InlineData(6000f)]
    public void CargoAsleepOnADeckSlidesTowardTheSternWhenTheShipsThrustGrows(float newtonsPerTick)
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        var deck = new CompoundShapeDesc([
            new(new BoxShapeDesc(new(10, 0.25f, 30)), new(new(0, -2.25f, 0), Quaternion.Identity))
        ]);
        var hull = new BodyDesc(Shape: new BoxShapeDesc(new(10, 3, 30)), Mass: 1000, LinearDamping: 0,
            AngularDamping: 0,
            InteriorShape: deck);
        // The ship's engine pushes with this force along +Z in every tick. A pilot writes it in one tick and Jolt applies
        // it in the step of the next one.
        float thrust = 0;
        var pilot = new Pilot
        {
            EveryTick = body =>
            {
                if (thrust > 0) body.AddForce(new(0, 0, thrust));
            }
        };
        Entity owner = world.Spawn(new()
        {
            Frame = space,
            Pose = At(0),
            Body = hull,
            AsFrame = new(FrameBounds.Sphere(100), GravityField.Uniform(new(0, -9.81f, 0))),
            Components = [pilot]
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        Entity crate = world.Spawn(new() { Frame = ship, Pose = At(0, -1.5), Body = Box(mass: 5) });
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, ship, crate), 300);
        IsAwake(backend, ship, crate).Should().BeFalse("the crate fell asleep on the deck");

        for (float force = newtonsPerTick; force < 6000 + newtonsPerTick; force += newtonsPerTick)
        {
            thrust = MathF.Min(force, 6000);
            world.Tick();
        }

        thrust = 6000;
        Tick(world, 60);

        world.PoseInOwnFrame(crate).Position.Z.Should()
            .BeLessThan(-0.3, "6 m/s² toward the stern beats the friction that holds 4.9 m/s²");
        world.Errors.Should().BeEmpty();
    }
}
