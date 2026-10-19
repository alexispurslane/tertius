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
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
            Components = components,
        }.Spinning(test.World, spin));

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
    public void WhenItsOwnerWritesItsTransformABodylessSpinFrameTurnsAwayForOneTickAndItsNextSamplePutsItBackOnItsSpin()
    {
        using var test = NewWorld();
        var place = new Double3(5, 0, 0);
        DQuaternion leaning = DQuaternion.FromAxisAngle(Double3.UnitZ, 1);
        bool write = false;
        Entity owner = SpawnSpinFrame(test, QuarterTurnPerSecond, place,
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                if (write)
                    self.SetTransform(new DPose(place, leaning));
                write = false;
            }));
        FrameHandle ground = test.World.GetOwnedFrame(owner);
        test.Run(3);

        write = true;
        test.Run(1);

        test.World.Errors.Should().BeEmpty();
        test.World.Frames.LocalPose(ground).Position.Should().Be(place);
        PoseChecks.AssertNear(test.World.Frames.LocalPose(ground).Rotation.Rotate(Double3.UnitX), leaning.Rotate(Double3.UnitX), 1e-12);
        test.Run(1);

        test.World.Frames.LocalPose(ground).Position.Should().Be(place);
        PoseChecks.AssertNear(test.World.Frames.LocalPose(ground).Rotation.Rotate(Double3.UnitX),
            QuarterTurnPerSecond.RotationAt(test.World.Time).Rotate(Double3.UnitX), 1e-12);
    }

    /// <summary>
    /// The spin's sample of the tick of the write starts where the sample before it ends, so the write moves the frame for one
    /// tick only, and the frame then turns at its old place for good (docs/plans/rails-components.md, "Tests").
    /// </summary>
    [Fact]
    public void WhenItsOwnerWritesANewPlaceABodylessSpinFrameStandsThereForOneTickAndThenTurnsAtItsOldPlaceForGood()
    {
        using var test = NewWorld();
        var place = new Double3(5, 0, 0);
        bool write = false;
        Entity owner = SpawnSpinFrame(test, QuarterTurnPerSecond, place,
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                if (write)
                    self.SetTransform(TestWorld.At(8));
                write = false;
            }));
        FrameHandle ground = test.World.GetOwnedFrame(owner);
        test.Run(3);

        write = true;
        test.Run(1);
        test.World.Frames.LocalPose(ground).Position.Should().Be(new Double3(8, 0, 0), "the write moves the frame at the sync point of its tick");

        for (int tick = 0; tick < 4; tick++)
        {
            test.Run(1);

            test.World.Frames.LocalPose(ground).Position.Should().Be(place, "the spin's samples keep the place where the frame stood before the write");
        }
    }

    [Fact]
    public void WhenItsOwnerWritesItsTransformABodylessSpinFrameKeepsItsProperAcceleration()
    {
        using var test = NewWorld();
        var weight = new Vector3(0, 9.81f, 0);
        bool write = false;
        Entity owner = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                if (write)
                    self.SetTransform(TestWorld.At(5));
                write = false;
            })],
        }.Spinning(test.World, QuarterTurnPerSecond, weight));
        FrameHandle ground = test.World.GetOwnedFrame(owner);
        test.Run(3);
        test.World.Frames.ProperAcceleration(ground).Should().Be(weight, "the spin's sample gives the frame its proper acceleration");

        write = true;
        test.Run(1);

        test.World.Frames.LocalPose(ground).Position.Should().Be(new Double3(5, 0, 0));
        test.World.Frames.ProperAcceleration(ground).Should().Be(weight, "a write moves the frame and changes nothing else, as a frame that holds still keeps its proper acceleration");
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

    // ---- Changing the motion ------------------------------------------------------------------------------
    // A change of motion is a swap of components, and for a hull also a change of the hull's motion with SetMotion
    // (docs/plans/rails-components.md, "Changes of motion"). These frames have no body, so the components alone decide how
    // the frame moves.

    /// <summary>
    /// A component that is new on an entity runs first in the next tick, and its first sample acts in the step after that
    /// tick. So the frame holds still at its pose for the first step and stands on its spin from the second sync point on
    /// (docs/plans/rails-components.md, "Other rules" in "The rails components"). The world starts two ticks before 1 s, so
    /// the second sync point ends at 1 s, when the spin has turned a quarter turn.
    /// </summary>
    [Fact]
    public void AFrameThatGetsASpinMotionKeepsItsPlaceAndTurnsByItsSpinFromTheSecondSyncPoint()
    {
        using var test = NewWorld(startTime: 1 - 2.0 / TicksPerSecond);
        Entity owner = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(5),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
        });
        FrameHandle dock = test.World.GetOwnedFrame(owner);

        test.World.AddComponent(owner, new SpinMotion { Spin = QuarterTurnPerSecond });
        test.Run(1);
        test.World.Frames.LocalPose(dock).Should().Be(TestWorld.At(5), "the component's first sample acts in the step after its first tick");
        test.Run(1);

        test.World.Time.Should().Be(1);
        test.World.Frames.LocalPose(dock).Position.Should().Be(new Double3(5, 0, 0));
        PoseChecks.AssertNear(test.World.Frames.LocalPose(dock).Rotation.Rotate(Double3.UnitX), new Double3(0, 0, -1), 1e-12);
        test.World.Frames.AngularVelocity(dock).Should().Be(new Vector3(0, MathF.PI / 2, 0));
        test.World.PoseInOwnFrame(owner).Should().Be(test.World.Frames.LocalPose(dock));
    }

    /// <summary>
    /// A ship without a body flies along +X at 10 m/s, on samples that a scripted flight writes, and its engines push it up
    /// at 9.81 m/s², so its cargo has weight. The game parks it on a turntable: a <see cref="SpinMotion"/> in place of the
    /// flight, into which the game copies the frame's proper acceleration (docs/plans/rails-components.md,
    /// "The rails components"). The flight's last sample moves the ship in the step of the swap, so the swap has no gap. From
    /// the second sync point after the swap, the ship stands still and turns at the place where that last sample left it: the
    /// spin's first sample starts where the flight's last sample ends.
    /// </summary>
    [Fact]
    public void AMovingFrameThatGetsASpinMotionInPlaceOfItsFlightStopsButKeepsItsProperAcceleration()
    {
        using var test = NewWorld();
        var velocity = new Double3(10, 0, 0);
        var weight = new Vector3(0, 9.81f, 0);
        Lambda flight = Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            double stepEnd = self.TimeAt(self.Tick + 2);
            self.MoveOnRails(new MotionSample(new DPose(velocity * stepEnd, DQuaternion.Identity), velocity, velocity, Double3.Zero,
                Double3.Zero, Double3.Zero, weight));
        });
        Entity owner = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
            Components = [flight],
        });
        FrameHandle ship = test.World.GetOwnedFrame(owner);
        test.Run(TicksPerSecond);
        test.World.Frames.LinearVelocity(ship).Should().Be(velocity, "the ship flies before the swap");
        test.World.Frames.ProperAcceleration(ship).Should().Be(weight);

        test.World.RemoveComponent(owner, flight);
        test.World.AddComponent(owner, new SpinMotion { Spin = QuarterTurnPerSecond, ProperAcceleration = test.World.Frames.ProperAcceleration(ship) });
        test.Run(1);
        Double3 parked = test.World.Frames.LocalPose(ship).Position;
        parked.Should().Be(velocity * test.World.Time, "the flight's last sample moves the ship in the step of the swap");

        for (int tick = 0; tick < 4; tick++)
        {
            test.Run(1);

            test.World.Frames.LinearVelocity(ship).Should().Be(Double3.Zero);
            test.World.Frames.AngularVelocity(ship).Should().Be(new Vector3(0, MathF.PI / 2, 0), "the ship turns by its spin");
            test.World.Frames.ProperAcceleration(ship).Should().Be(weight, "a ship on a turntable keeps its cargo's weight, as a parked one does");
            test.World.Frames.LocalPose(ship).Position.Should().Be(parked, "a ship on a turntable turns in place");
        }
    }

    // ---- Refusals -----------------------------------------------------------------------------------------------

    [Fact]
    public void AFrameWithASpinThatIsNotValidIsNotSpawned()
    {
        using var test = NewWorld();
        int before = test.World.Stats.LiveEntities;

        // The SpinMotion of the spawn refuses the spin in its setter, so nothing spawns.
        Action spawn = () => SpawnSpinFrame(test, new FrameSpin(DQuaternion.Identity, double.NaN));

        spawn.Should().Throw<ArgumentException>().WithMessage("*SpinMotion*spin*not valid*");
        test.World.Stats.LiveEntities.Should().Be(before);
    }

    [Fact]
    public void AFrameWithASpinMotionAndAKinematicMeshHullIsNotSpawned()
    {
        using var test = NewWorld();
        int before = test.World.Stats.LiveEntities;

        Action spawn = () => test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
            Body = new BodyDesc(Ramp, BodyMotion.Kinematic),
        }.Spinning(test.World, QuarterTurnPerSecond));

        spawn.Should().Throw<ArgumentException>().WithMessage("*kinematic*mesh*");
        test.World.Stats.LiveEntities.Should().Be(before);
    }

    [Fact]
    public void ASpinMotionWithASpinThatIsNotValidIsRefusedBeforeItReachesTheFrame()
    {
        using var test = NewWorld();
        FrameHandle dock = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
        }));

        // The component checks its spin, not the World: its setter refuses it.
        Action add = () => test.World.AddComponent(dock.Entity, new SpinMotion { Spin = new FrameSpin(DQuaternion.Identity, double.NaN) });

        add.Should().Throw<ArgumentException>().WithMessage("*SpinMotion*spin*not valid*");
        test.World.GetComponent<SpinMotion>(dock.Entity).Should().BeNull();
    }
}
