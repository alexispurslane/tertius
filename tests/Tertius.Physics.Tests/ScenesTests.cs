using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>The scene builders of the shared test project build what their names and comments say, on the real physics.</summary>
public class ScenesTests
{
    [Fact]
    public void ABallReleasedAboveTheFloorFallsAndLiesOnIt()
    {
        using World world = NewWorld(out _);
        Scenes.FloorAndBall scene = Scenes.FloorWithBall(world, height: 1);
        world.PoseInOwnFrame(scene.Ball).Position.Y.Should().Be(1.5, "the height is measured to the bottom of the ball, which has a radius of 0.5 m");

        Tick(world, 180);

        world.GetFrame(scene.Ball).Should().Be(scene.Ground);
        world.PoseInOwnFrame(scene.Ball).Position.Y.Should().BeApproximately(0.5, 0.02, "the ball lies on the floor, whose top is at height 0");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABallReleasedInAShipWithGravityLiesOnItsDeck()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, space, g: 9.81f);
        Entity ball = world.Spawn(new SpawnDesc { Frame = ship.Interior, Pose = At(0, Scenes.DeckTop + 1), Body = Ball() });

        Tick(world, 180);

        world.GetFrame(ball).Should().Be(ship.Interior);
        world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(Scenes.DeckTop + 0.5, 0.02, "the lowest point of the ball rests on the top of the deck");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShipWithoutGravityStaysWhereItIsInSpace()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, space, position: new Double3(100, 200, 300), bubbleRadius: 30);

        Tick(world, 60);

        world.GetFrame(ship.Hull).Should().Be(space);
        world.PoseInOwnFrame(ship.Hull).Position.Should().Be(new Double3(100, 200, 300));
        world.Frames.Bounds(ship.Interior).Should().Be(FrameBounds.Sphere(30));
    }

    [Fact]
    public void AMoonOnACircularOrbitKeepsItsDistanceFromThePlanet()
    {
        using World world = NewWorld(out _);
        Scenes.MoonScene scene = Scenes.MoonOnKeplerOrbit(world, orbitRadius: 7e6);

        Tick(world, 120);

        world.Frames.Parent(scene.Moon).Should().Be(scene.Planet);
        world.Frames.LocalPose(scene.Moon).Position.Length.Should().BeApproximately(7e6, 1e-3);
        world.Frames.LinearVelocity(scene.Moon).Length.Should().BeApproximately(Math.Sqrt(Scenes.EarthMu / 7e6), 1e-3, "a circular orbit has the speed of √(G·M / r)");
    }
}
