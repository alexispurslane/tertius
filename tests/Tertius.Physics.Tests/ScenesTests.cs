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
        world.PoseInOwnFrame(scene.Ball).Position.Y.Should().Be(1.5,
            "the height is measured to the bottom of the ball, which has a radius of 0.5 m");

        Tick(world, 180);

        world.GetFrame(scene.Ball).Should().Be(scene.Ground);
        world.PoseInOwnFrame(scene.Ball).Position.Y.Should()
            .BeApproximately(0.5, 0.02, "the ball lies on the floor, whose top is at height 0");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABallReleasedInAShipWithGravityLiesOnItsDeck()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, space, g: 9.81f);
        Entity ball = world.Spawn(new() { Frame = ship.Interior, Pose = At(0, Scenes.DeckTop + 1), Body = Ball() });

        Tick(world, 180);

        world.GetFrame(ball).Should().Be(ship.Interior);
        world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(Scenes.DeckTop + 0.5, 0.02,
            "the lowest point of the ball rests on the top of the deck");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShipWithoutGravityStaysWhereItIsInSpace()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Scenes.ShipScene ship = Scenes.ShipWithInterior(world, space, position: new(100, 200, 300), bubbleRadius: 30);

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
        world.Frames.LinearVelocity(scene.Moon).Length.Should().BeApproximately(Math.Sqrt(Scenes.EarthMu / 7e6), 1e-3,
            "a circular orbit has the speed of √(G·M / r)");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(4, 0)]
    [InlineData(1, 20)]
    public void ABallApproachingABubbleIsHandedOffInTheStepOfTheTickThatWasAskedFor(int tick, double lateral)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = space,
            Pose = At(500, 3, -2),
            AsFrame = new(FrameBounds.Sphere(100))
        }));
        Scenes.Approach approach = Scenes.ApproachToBubble(world, station, tick, lateral: lateral);
        Entity ball = world.Spawn(new()
            { Frame = space, Pose = approach.Pose, LinearVelocity = approach.Velocity, Body = Ball() });

        Tick(world, tick + 3);

        int stepsWithAHandOff = faults.Outputs.FindIndex(output => output.HandOffs.Count > 0);
        stepsWithAHandOff.Should().Be(tick);
        HandOff handOff = faults.Outputs[tick].HandOffs.Should().ContainSingle().Subject;
        handOff.Entity.Should().Be(ball);
        handOff.From.Should().Be(space);
        handOff.To.Should().Be(station);
        world.GetFrame(ball).Should().Be(station);
        faults.Outputs.SelectMany(output => output.HandOffs).Should()
            .ContainSingle("the ball enters the bubble once and stays in it");
        world.Errors.Should().BeEmpty();
    }
}
