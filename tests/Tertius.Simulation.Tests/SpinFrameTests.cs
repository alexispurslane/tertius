using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Spin frames: a frame that stays at its place in its parent and turns in place, as the ground of a planet does. The
/// world runs 64 ticks per second, so a second is exactly 64 ticks. Every spin here turns a quarter turn per second about
/// +Y, counterclockwise seen from above, so a quarter turn takes +X to -Z.
/// </summary>
public class SpinFrameTests
{
    private const int TicksPerSecond = 64;
    private static readonly FrameSpin QuarterTurnPerSecond = new(DQuaternion.Identity, Math.PI / 2);

    /// <summary>A mesh. Jolt never moves a mesh, so a hull of this shape can only be static.</summary>
    private static readonly MeshShapeDesc Ramp = new([new(-5, 0, -5), new(5, 0, -5), new(5, 2, 5), new(-5, 2, 5)], [0, 2, 1, 0, 3, 2]);

    private static TestWorld NewWorld(double startTime = 0) => new(configure: options =>
    {
        options.TickRate = TicksPerSecond;
        options.StartTime = startTime;
    });

    private static Entity SpawnSpinFrame(TestWorld test, FrameSpin spin, Double3 place = default, params IComponent[] components) =>
        test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = new DPose(place, DQuaternion.FromAxisAngle(Double3.UnitX, 1)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Spin, Spin: spin),
            Components = components,
        });

    // ---- How a spin frame moves ---------------------------------------------------------------------------------

    [Fact]
    public void ASpinFrameIsTurnedToItsSpinAsSoonAsItExistsWhateverRotationItWasSpawnedWith()
    {
        using var test = NewWorld(startTime: 1);
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, QuarterTurnPerSecond));

        PoseChecks.AssertNear(test.World.Frames.LocalPose(ground).Rotation.Rotate(Double3.UnitX), new Double3(0, 0, -1), 1e-12);
        // The spawn rotation turned about +X, which would move the pole: the spin alone keeps it at +Y.
        PoseChecks.AssertNear(test.World.Frames.LocalPose(ground).Rotation.Rotate(Double3.UnitY), Double3.UnitY, 1e-12);
    }

    [Fact]
    public void ASpinFrameTurnsByItsSpinForTheTimeThatPassed()
    {
        using var test = NewWorld();
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, QuarterTurnPerSecond));

        test.Run(2 * TicksPerSecond);

        PoseChecks.AssertNear(test.World.Frames.LocalPose(ground).Rotation.Rotate(Double3.UnitX), new Double3(-1, 0, 0), 1e-12);
    }

    [Fact]
    public void ASpinFrameStaysAtThePlaceInItsParentWhereItWasSpawned()
    {
        using var test = NewWorld();
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, QuarterTurnPerSecond, new Double3(5, 6, 7)));

        test.Run(3 * TicksPerSecond);

        test.World.Frames.LocalPose(ground).Position.Should().Be(new Double3(5, 6, 7));
    }

    [Fact]
    public void AnEntityInASpinFrameTurnsWithTheFrame()
    {
        using var test = NewWorld();
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, QuarterTurnPerSecond));
        Entity tower = test.World.SpawnAt(ground, new Double3(10, 0, 0));

        test.Run(TicksPerSecond);

        test.World.Snapshot.TryGetPoseIn(tower, test.Root, out DPose inRoot).Should().BeTrue();
        PoseChecks.AssertNear(inRoot.Position, new Double3(0, 0, -10), 1e-9);
    }

    [Fact]
    public void ASpinFrameHasTheAngularVelocityOfItsSpinAndNoLinearVelocityOrProperAcceleration()
    {
        using var test = NewWorld();
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, QuarterTurnPerSecond));

        test.Run(10);

        test.World.Frames.AngularVelocity(ground).Should().Be(new Vector3(0, MathF.PI / 2, 0));
        test.World.Frames.LinearVelocity(ground).Should().Be(Double3.Zero);
        test.World.Frames.ProperAcceleration(ground).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void TheOwnerOfASpinFrameMirrorsTheFramesStateAtTheEndOfEveryTick()
    {
        using var test = NewWorld();
        Entity owner = SpawnSpinFrame(test, QuarterTurnPerSecond, new Double3(5, 0, 0));
        FrameHandle frame = test.World.GetOwnedFrame(owner);

        for (int tick = 0; tick < 5; tick++)
        {
            test.Run(1);

            test.World.PoseInOwnFrame(owner).Should().Be(test.World.Frames.LocalPose(frame));
            test.World.VelocityInOwnFrame(owner).Should().Be(Vector3.Zero);
            test.World.AngularVelocityInOwnFrame(owner).Should().Be(test.World.Frames.AngularVelocity(frame));
        }
    }

    [Fact]
    public void ASpinFrameStaysOnItsSpinWhenItsOwnerTriesToMoveIt()
    {
        using var test = NewWorld();
        Entity owner = SpawnSpinFrame(test, QuarterTurnPerSecond,
            components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.SetTransform(TestWorld.At(5))));
        FrameHandle ground = test.World.GetOwnedFrame(owner);

        test.Run(TicksPerSecond);

        test.World.Errors.Should().HaveCount(TicksPerSecond, "each refused write is recorded");
        test.World.Frames.LocalPose(ground).Position.Should().Be(Double3.Zero);
        PoseChecks.AssertNear(test.World.Frames.LocalPose(ground).Rotation.Rotate(Double3.UnitX), new Double3(0, 0, -1), 1e-12);
    }

    [Fact]
    public void ATiltedSpinFrameTurnsAboutItsTiltedPoleAndSaysSoInItsAngularVelocity()
    {
        using var test = NewWorld();
        // The pole leans 90 degrees from +Y to +X, so the frame turns about the parent's +X.
        var tilted = new FrameSpin(DQuaternion.FromAxisAngle(Double3.UnitZ, -Math.PI / 2), Math.PI / 2);
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, tilted));
        Entity tower = test.World.SpawnAt(ground, new Double3(0, 0, 10));

        test.Run(TicksPerSecond);

        test.World.Frames.AngularVelocity(ground).X.Should().BeApproximately(MathF.PI / 2, 1e-6f);
        test.World.Frames.AngularVelocity(ground).Y.Should().BeApproximately(0, 1e-6f);
        // The tower stands 10 m along the frame's +Z, which the lean leaves at the parent's +Z. A quarter turn about +X then
        // takes +Z to -Y.
        test.World.Snapshot.TryGetPoseIn(tower, test.Root, out DPose inRoot).Should().BeTrue();
        PoseChecks.AssertNear(inRoot.Position, new Double3(0, -10, 0), 1e-9);
    }

    // ---- The spin sample matches the World ----------------------------------------------------------------------

    [Fact]
    public void After_a_sync_point_a_spin_frame_has_the_pose_the_linear_velocity_and_the_angular_velocity_of_its_spin_sample()
    {
        using var test = NewWorld(startTime: 1);
        var spin = new FrameSpin(DQuaternion.FromAxisAngle(Double3.UnitZ, -0.4), 0.7, PhaseAtEpoch: 0.3, Epoch: 2);
        var place = new Double3(5, -6, 7);
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, spin, place));

        test.Run(10);

        MotionSample sample = spin.Sample(place, Vector3.Zero, test.World.Time, test.World.DeltaTime);
        test.World.Frames.LocalPose(ground).Should().Be(sample.Pose);
        // The ground hangs in the root, which is no numeric frame, so the stored velocity is the one relative to the parent.
        test.World.Frames.TreeLinearVelocity(ground).Should().Be(sample.Velocity, "a frame that turns in place does not move");
        test.World.Frames.AngularVelocity(ground).Should().Be(sample.AngularVelocity.ToVector3(), "the tree keeps the angular velocity in floats");
    }

    // ---- Changing the driver or the spin ------------------------------------------------------------------------

    [Fact]
    public void AFrameThatGetsTheSpinDriverKeepsItsPlaceAndTurnsToItsSpinAtOnce()
    {
        using var test = NewWorld(startTime: 1);
        FrameHandle dock = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(5),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static, Spin: QuarterTurnPerSecond),
        }));

        test.World.SetFrameDriver(dock, MotionDriver.Spin);

        test.World.Frames.LocalPose(dock).Position.Should().Be(new Double3(5, 0, 0));
        PoseChecks.AssertNear(test.World.Frames.LocalPose(dock).Rotation.Rotate(Double3.UnitX), new Double3(0, 0, -1), 1e-12);
        test.World.PoseInOwnFrame(dock.Entity).Should().Be(test.World.Frames.LocalPose(dock));
    }

    [Fact]
    public void AMovingFrameThatGetsTheSpinDriverStopsButKeepsItsProperAcceleration()
    {
        using var test = NewWorld();
        // A ship that landed under the physics driver reads the ground's push on its accelerometer, so its cargo has weight.
        FrameHandle ship = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            LinearVelocity = new Vector3(10, 0, 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Physics, Spin: QuarterTurnPerSecond),
        }));
        test.World.Frames.ProperAcceleration(ship) = new Vector3(0, 9.81f, 0);

        test.World.SetFrameDriver(ship, MotionDriver.Spin);
        test.World.Frames.LinearVelocity(ship).Should().Be(Double3.Zero);
        test.World.Frames.ProperAcceleration(ship).Should().Be(new Vector3(0, 9.81f, 0));
        test.Run(1);

        test.World.Frames.LinearVelocity(ship).Should().Be(Double3.Zero);
        test.World.Frames.ProperAcceleration(ship).Should().Be(new Vector3(0, 9.81f, 0), "a ship on a turntable keeps its cargo's weight, as a parked one does");
    }

    [Fact]
    public void ASpinFrameThatGetsANewSpinTurnsToItAtOnce()
    {
        using var test = NewWorld(startTime: 1);
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, QuarterTurnPerSecond));

        test.World.SetFrameSpin(ground, new FrameSpin(DQuaternion.Identity, Math.PI));

        test.World.Frames.Spin(ground).Rate.Should().Be(Math.PI);
        PoseChecks.AssertNear(test.World.Frames.LocalPose(ground).Rotation.Rotate(Double3.UnitX), new Double3(-1, 0, 0), 1e-12);
        test.World.Frames.AngularVelocity(ground).Should().Be(new Vector3(0, MathF.PI, 0));
        test.World.PoseInOwnFrame(ground.Entity).Should().Be(test.World.Frames.LocalPose(ground));
    }

    [Fact]
    public void AFrameWithAnotherDriverKeepsANewSpinForLater()
    {
        using var test = NewWorld(startTime: 1);
        FrameHandle dock = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static),
        }));

        test.World.SetFrameSpin(dock, QuarterTurnPerSecond);

        test.World.Frames.Spin(dock).Should().Be(QuarterTurnPerSecond);
        test.World.Frames.LocalPose(dock).Rotation.Should().Be(DQuaternion.Identity);
    }

    // ---- Refusals -----------------------------------------------------------------------------------------------

    [Fact]
    public void AFrameWithTheSpinDriverAndASpinThatIsNotValidIsNotSpawned()
    {
        using var test = NewWorld();
        int before = test.World.Stats.LiveEntities;

        Action spawn = () => SpawnSpinFrame(test, new FrameSpin(DQuaternion.Identity, double.NaN));

        spawn.Should().Throw<ArgumentException>().WithMessage("*Spin driver*spin*");
        test.World.Stats.LiveEntities.Should().Be(before);
    }

    [Fact]
    public void AFrameWithTheSpinDriverAndAMeshHullIsNotSpawned()
    {
        using var test = NewWorld();
        int before = test.World.Stats.LiveEntities;

        Action spawn = () => test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Spin, Spin: QuarterTurnPerSecond),
            Body = new BodyDesc(Ramp, BodyMotion.Static),
        });

        spawn.Should().Throw<ArgumentException>().WithMessage("*mesh*Spin driver*");
        test.World.Stats.LiveEntities.Should().Be(before);
    }

    [Fact]
    public void ASpinThatIsNotValidIsRefusedAndTheFrameKeepsItsSpin()
    {
        using var test = NewWorld();
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, QuarterTurnPerSecond));

        Action set = () => test.World.SetFrameSpin(ground, new FrameSpin(DQuaternion.Identity, double.PositiveInfinity));

        set.Should().Throw<ArgumentException>().WithMessage($"*{ground}*spin*");
        test.World.Frames.Spin(ground).Should().Be(QuarterTurnPerSecond);
    }

    [Fact]
    public void ASpinForAFrameThatIsGoneIsRefused()
    {
        using var test = NewWorld();
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, QuarterTurnPerSecond));
        test.World.Destroy(ground.Entity);

        Action set = () => test.World.SetFrameSpin(ground, QuarterTurnPerSecond);

        set.Should().Throw<ArgumentException>().WithMessage($"*{ground}*");
    }

    [Fact]
    public void ASpinChangeDuringATickIsRefused()
    {
        using var test = NewWorld();
        FrameHandle ground = test.World.GetOwnedFrame(SpawnSpinFrame(test, QuarterTurnPerSecond));
        InvalidOperationException? caught = null;
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            try
            {
                test.World.SetFrameSpin(ground, default);
            }
            catch (InvalidOperationException ex)
            {
                caught = ex;
            }
        }));

        test.Run(1);

        caught.Should().NotBeNull("physics reads the frame tree on its own thread during the tick");
        caught!.Message.Should().Contain("World.SetFrameSpin");
        test.World.Frames.Spin(ground).Should().Be(QuarterTurnPerSecond);
    }

    [Fact]
    public void TheSpinDriverIsRefusedForAFrameWithoutAValidSpin()
    {
        using var test = NewWorld();
        // A frame with another driver keeps the spin that it was spawned with, valid or not, until it gets the Spin driver.
        FrameHandle dock = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static, Spin: new FrameSpin(DQuaternion.Identity, double.NaN)),
        }));

        Action set = () => test.World.SetFrameDriver(dock, MotionDriver.Spin);

        set.Should().Throw<ArgumentException>().WithMessage($"*{dock}*spin*");
        test.World.Frames.Driver(dock).Should().Be(MotionDriver.Static);
    }

    [Fact]
    public void TheSpinDriverIsRefusedForAFrameWhoseHullIsAMesh()
    {
        using var test = NewWorld();
        FrameHandle dock = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static, Spin: QuarterTurnPerSecond),
            Body = new BodyDesc(Ramp, BodyMotion.Static),
        }));

        Action set = () => test.World.SetFrameDriver(dock, MotionDriver.Spin);

        set.Should().Throw<ArgumentException>().WithMessage($"*{dock.Entity}*mesh*");
        test.World.Frames.Driver(dock).Should().Be(MotionDriver.Static);
    }
}
