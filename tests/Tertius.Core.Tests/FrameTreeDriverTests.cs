using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="FrameTree.SetDriver"/> changes what moves a frame, for example when a game parks a ship. Physics follows
/// the change at its next step, the same way that it follows a reparent: the change moves the tree's version on.
/// </summary>
public class FrameTreeDriverTests
{
    private static readonly Double3 ShipVelocity = new(10, 0, 0);
    private static readonly Vector3 ShipSpin = new(0, 0.5f, 0);
    private static readonly Vector3 ShipAcceleration = new(0, 9.81f, 0);
    private static readonly DPose ShipPose = new(new Double3(100, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.3));

    /// <summary>
    /// A root frame and a ship frame inside it, with the physics driver, that moves with <see cref="ShipVelocity"/>, spins
    /// with <see cref="ShipSpin"/>, and reads <see cref="ShipAcceleration"/> on its accelerometer, like a ship on the ground.
    /// </summary>
    private sealed class Ship
    {
        public readonly FrameTree Frames = new();
        public readonly FrameHandle Root;
        public readonly FrameHandle Hull;

        public Ship()
        {
            Root = Frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static);
            Hull = Frames.Create(new Entity(2, 1), Root, ShipPose, FrameBounds.Sphere(50), MotionDriver.Physics);
            Frames.TreeLinearVelocity(Hull) = ShipVelocity;
            Frames.AngularVelocity(Hull) = ShipSpin;
            Frames.ProperAcceleration(Hull) = ShipAcceleration;
            Frames.Update();
        }
    }

    [Theory]
    [InlineData(MotionDriver.Static)]
    [InlineData(MotionDriver.Kepler)]
    public void AFrameHasTheDriverThatItWasLastGiven(MotionDriver driver)
    {
        var ship = new Ship();

        ship.Frames.SetDriver(ship.Hull, driver);

        ship.Frames.Driver(ship.Hull).Should().Be(driver);
    }

    [Theory]
    [InlineData(MotionDriver.Static)]
    [InlineData(MotionDriver.Kepler)]
    public void ANewDriverMovesTheVersionOnSoThatPhysicsFollowsIt(MotionDriver driver)
    {
        var ship = new Ship();
        int before = ship.Frames.Version;

        ship.Frames.SetDriver(ship.Hull, driver);

        ship.Frames.Version.Should().NotBe(before);
    }

    /// <summary>
    /// Physics puts the hull at the frame's pose and velocity in the tree after every driver call. So a call that gives a
    /// frame the driver that it has moves the version on too: after a park and an unpark in one gap between ticks, the hull
    /// must stop, although its driver ends where it started.
    /// </summary>
    [Fact]
    public void TheDriverThatAFrameHasAlreadyMovesTheVersionOnToo()
    {
        var ship = new Ship();
        int before = ship.Frames.Version;

        ship.Frames.SetDriver(ship.Hull, MotionDriver.Physics);

        ship.Frames.Version.Should().NotBe(before);
    }

    [Theory]
    [InlineData(MotionDriver.Static)]
    [InlineData(MotionDriver.Physics)]
    public void EveryDriverCallMovesTheFramesDriverVersionOn(MotionDriver driver)
    {
        var ship = new Ship();
        int before = ship.Frames.DriverVersion(ship.Hull);

        ship.Frames.SetDriver(ship.Hull, driver);

        ship.Frames.DriverVersion(ship.Hull).Should().NotBe(before);
    }

    [Fact]
    public void ADriverCallLeavesTheDriverVersionOfEveryOtherFrameAsItWas()
    {
        var ship = new Ship();
        int before = ship.Frames.DriverVersion(ship.Root);

        ship.Frames.SetDriver(ship.Hull, MotionDriver.Static);

        ship.Frames.DriverVersion(ship.Root).Should().Be(before);
    }

    [Fact]
    public void AFrameThatGetsTheStaticDriverStopsWhereItIs()
    {
        var ship = new Ship();

        ship.Frames.SetDriver(ship.Hull, MotionDriver.Static);

        ship.Frames.LocalPose(ship.Hull).Should().Be(ShipPose);
        ship.Frames.LinearVelocity(ship.Hull).Should().Be(Double3.Zero);
        ship.Frames.AngularVelocity(ship.Hull).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void AFrameThatGetsTheStaticDriverKeepsItsProperAccelerationSoThatAParkedShipKeepsTheWeightOfItsCargo()
    {
        var ship = new Ship();

        ship.Frames.SetDriver(ship.Hull, MotionDriver.Static);

        ship.Frames.ProperAcceleration(ship.Hull).Should().Be(ShipAcceleration);
    }

    [Fact]
    public void AFrameThatGetsAMovingDriverKeepsItsMotionUntilThatDriverMovesIt()
    {
        var ship = new Ship();

        ship.Frames.SetDriver(ship.Hull, MotionDriver.Kepler);

        ship.Frames.LocalPose(ship.Hull).Should().Be(ShipPose);
        ship.Frames.LinearVelocity(ship.Hull).Should().Be(ShipVelocity);
        ship.Frames.AngularVelocity(ship.Hull).Should().Be(ShipSpin);
    }

    [Fact]
    public void ADriverForAStaleFrameIsRefused()
    {
        var ship = new Ship();
        ship.Frames.Destroy(ship.Hull);

        Action set = () => ship.Frames.SetDriver(ship.Hull, MotionDriver.Static);

        set.Should().Throw<InvalidOperationException>();
    }
}
