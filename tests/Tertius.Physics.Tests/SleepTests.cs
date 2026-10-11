using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// A dynamic body falls asleep only when it really rests. Jolt puts a body to sleep when it moves less than about 3 cm in
/// half a second. A body that gravity has just started to pull moves that little too, when the gravity is weak. So a body
/// that gravity accelerates never falls asleep, while a body that something holds against gravity does. The sleeping
/// bodies of a frame wake when the frame's proper acceleration has moved far enough from its value when they last woke.
/// </summary>
public class SleepTests
{
    /// <summary>True while Jolt simulates the body of <paramref name="entity"/> in the system of <paramref name="frame"/>.</summary>
    private static bool IsAwake(PhysicsHarness h, FrameHandle frame, Entity entity) =>
        h.Backend.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, entity)).Active;

    /// <summary>A frame with uniform gravity of <paramref name="g"/> m/s² along -Y, and a floor whose top is at y = 0.</summary>
    private static FrameHandle Ground(PhysicsHarness h, float g)
    {
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -g, 0)));
        h.AddBody(ground, Shapes.Floor(50), Shapes.At(0, -0.5, 0));
        return ground;
    }

    /// <summary>
    /// The reviewer's probe S1. A box of 1 m is let go at rest with its bottom 1 m above the floor. At 0.05 m/s² the fall
    /// takes sqrt(2 / 0.05) = 6.3 s, and the test steps 15 s.
    /// </summary>
    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.1f)]
    [InlineData(0.2f)]
    [InlineData(0.3f)]
    [InlineData(1.0f)]
    public void ABoxLetGoAtRestInWeakGravityFallsToTheFloor(float g)
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = Ground(h, g);
        Entity box = h.AddBody(ground, Shapes.Box(0.5f, mass: 10), Shapes.At(0, 1.5, 0));

        h.Step(900);

        h[box].Pose.Position.Y.Should().BeApproximately(0.5, 0.01, "the box fell 1 m onto the floor");
    }

    /// <summary>
    /// Two boxes, one on top of the other, are let go at rest 1 m above the floor in weak gravity. They touch each other all
    /// the way down, so a rule that lets a body sleep while it touches something would leave them asleep in mid-air.
    /// </summary>
    [Fact]
    public void AStackOfTwoBoxesLetGoInWeakGravityFallsToTheFloor()
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = Ground(h, 0.1f);
        Entity bottom = h.AddBody(ground, Shapes.Box(0.5f, mass: 10), Shapes.At(0, 1.5, 0));
        Entity top = h.AddBody(ground, Shapes.Box(0.5f, mass: 10), Shapes.At(0, 2.5, 0));

        h.Step(900);

        h[bottom].Pose.Position.Y.Should().BeApproximately(0.5, 0.02, "the stack fell 1 m onto the floor");
        h[top].Pose.Position.Y.Should().BeApproximately(1.5, 0.03, "the top box rides the bottom one down");
    }

    /// <summary>A body that rests on the floor falls asleep there, in weak gravity as in strong gravity.</summary>
    [Theory]
    [InlineData(0.05f)]
    [InlineData(9.81f)]
    public void ABoxThatLandsOnAFloorFallsAsleepThereWithinTwoSeconds(float g)
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = Ground(h, g);
        Entity box = h.AddBody(ground, Shapes.Box(0.5f, mass: 10), Shapes.At(0, 0.6, 0));
        // The fall of 0.1 m takes 2 s at 0.05 m/s².
        h.StepUntil(() => h.Contacts.Count > 0, 300);
        h.Contacts.Should().ContainSingle("the box landed");

        int ticks = h.StepUntil(() => !IsAwake(h, ground, box), 120);

        ticks.Should().BeLessThan(120, "the box rests on the floor, so it falls asleep within 2 s");
        h[box].Pose.Position.Y.Should().BeApproximately(0.5, 0.01, "the box sleeps on the floor");
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
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(10, 0.25f, 30)), new Pose(new Vector3(0, -2.25f, 0), Quaternion.Identity))]);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(10, 3, 30)), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: deck);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(0, 0, 0), FrameBounds.Sphere(100), MotionDriver.Physics, hull,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Entity crate = h.AddBody(ship, Shapes.Box(0.5f, mass: 5), Shapes.At(0, -1.5, 0));
        h.Step();
        h.StepUntil(() => !IsAwake(h, ship, crate), 300);
        IsAwake(h, ship, crate).Should().BeFalse("the crate fell asleep on the deck");

        for (float force = newtonsPerTick; force < 6000 + newtonsPerTick; force += newtonsPerTick)
        {
            h.Intent(ship.Entity).Force = new Vector3(0, 0, MathF.Min(force, 6000));
            h.Step();
        }
        for (int i = 0; i < 60; i++)
        {
            h.Intent(ship.Entity).Force = new Vector3(0, 0, 6000);
            h.Step();
        }

        h[crate].Pose.Position.Z.Should().BeLessThan(-0.3, "6 m/s² toward the stern beats the friction that holds 4.9 m/s²");
    }
}
