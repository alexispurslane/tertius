using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;

namespace Tertius.Physics.Tests;

/// <summary>
/// Every read of a position or a velocity names the frame that it wants the answer in. A velocity read gives a
/// <see cref="Double3"/>. Writes stay relative to the entity's own frame. These tests run the real World on real Jolt.
/// </summary>
public sealed class WorldFrameReadTests
{
    /// <summary>A ball without damping, so that it keeps its velocity in a weightless frame.</summary>
    private static readonly BodyDesc Ball = new(
        Shape: new SphereShapeDesc(0.5f),
        Mass: 1,
        LinearDamping: 0,
        AngularDamping: 0
    );

    /// <summary>A weightless root frame, so that a body keeps the velocity that it was given.</summary>
    private static FrameHandle Space(World world) => world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));

    /// <summary>A frame 10 m along X in <paramref name="parent"/>, tilted, that turns at 0.5 rad/s.</summary>
    private static FrameHandle SpinningChild(World world, FrameHandle parent)
    {
        var spin = new FrameSpin(Tilt: DQuaternion.FromAxisAngle(Double3.UnitX, 0.3), Rate: 0.5);
        Entity owner = world.Spawn(
            new SpawnDesc
            {
                Frame = parent,
                Pose = WorldTesting.At(10, 0, 0),
                AsFrame = new(FrameBounds.Sphere(100)),
            }.Spinning(world, spin)
        );
        return world.GetOwnedFrame(owner);
    }

    /// <summary>A frame that was live and is now gone.</summary>
    private static FrameHandle StaleFrame(World world, FrameHandle parent)
    {
        FrameHandle frame = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = parent,
                    Pose = WorldTesting.At(-20),
                    AsFrame = new(FrameBounds.Sphere(10)),
                }
            )
        );
        world.Destroy(frame.Entity);
        world.Tick();
        world.Frames.IsAlive(frame).Should().BeFalse();
        return frame;
    }

    private delegate void SelfRead(Self self);

    /// <summary>Runs <see cref="Read"/> on every tick while it is set.</summary>
    private sealed class Reader : IComponent
    {
        public SelfRead? Read { get; set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => Read?.Invoke(self);
    }

    [Fact]
    public void AVelocitySetAsADouble3ReadsBackInTheEntitysOwnFrame()
    {
        using World world = WorldTesting.NewWorld(backend: out _);
        FrameHandle space = Space(world);
        var pilot = new Pilot { Once = body => body.SetVelocity(new(3, -1, 0.5)) };
        Entity ball = world.Spawn(
            new()
            {
                Frame = space,
                Body = Ball,
                Components = [pilot],
            }
        );

        WorldTesting.Tick(world, 3);

        Double3 velocity = world.VelocityIn(ball, space);
        TestPrecision.RoughlyEqual(velocity, new(3, -1, 0.5), 1e-5).Should().BeTrue($"the read gave {velocity}");
    }

    [Fact]
    public void AVelocityGivenAtSpawnReadsBackInTheEntitysOwnFrame()
    {
        using World world = WorldTesting.NewWorld(backend: out _);
        FrameHandle space = Space(world);
        Entity ball = world.Spawn(
            new()
            {
                Frame = space,
                Body = Ball,
                LinearVelocity = new(0, 0, -2),
            }
        );

        WorldTesting.Tick(world, 2);

        Double3 velocity = world.VelocityIn(ball, space);
        TestPrecision.RoughlyEqual(velocity, new(0, 0, -2), 1e-5).Should().BeTrue($"the read gave {velocity}");
    }

    [Fact]
    public void APoseReadInAnotherFrameMatchesTheSnapshotsPoseInThatFrame()
    {
        using World world = WorldTesting.NewWorld(backend: out _);
        FrameHandle space = Space(world);
        FrameHandle turning = SpinningChild(world, space);
        Entity ball = world.Spawn(
            new()
            {
                Frame = space,
                Body = Ball,
                Pose = WorldTesting.At(4, 2, 1),
                LinearVelocity = new(1, 0, 0),
            }
        );
        WorldTesting.Tick(world, 3);

        DPose pose = world.PoseIn(ball, turning);

        DPose expected = world.Snapshot.PoseIn(ball, turning).Found();
        pose.Should().Be(expected);
        TestPrecision
            .RoughlyEqual(
                pose.Position,
                world.Frames.Relative(space, turning).TransformPoint(world.PoseIn(ball, space).Position)
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void AVelocityReadInAnotherFrameIsTheFrameTreesConversionOfTheVelocityInTheOwnFrame()
    {
        using World world = WorldTesting.NewWorld(backend: out _);
        FrameHandle space = Space(world);
        FrameHandle turning = SpinningChild(world, space);
        Entity ball = world.Spawn(
            new()
            {
                Frame = space,
                Body = Ball,
                Pose = WorldTesting.At(4, 2, 1),
                LinearVelocity = new(1, 2, 0),
            }
        );
        WorldTesting.Tick(world, 3);

        Double3 velocity = world.VelocityIn(ball, turning);

        Double3 expected = world.Frames.ConvertVelocity(
            from: space,
            to: turning,
            position: world.PoseIn(ball, space).Position,
            v: world.VelocityIn(ball, space)
        );
        // The same conversion, up to the last bit of rounding.
        TestPrecision
            .RoughlyEqual(velocity, expected, 1e-12)
            .Should()
            .BeTrue($"the read gave {velocity}, not {expected}");
        Double3 fromSnapshot = world.Snapshot.VelocityIn(ball, turning).Found();
        fromSnapshot.Should().Be(velocity);
    }

    [Fact]
    public void AnAngularVelocityReadInAnotherFrameTurnsIntoItsAxesAndAddsTheFramesTurn()
    {
        using World world = WorldTesting.NewWorld(backend: out _);
        FrameHandle space = Space(world);
        FrameHandle turning = SpinningChild(world, space);
        Entity ball = world.Spawn(
            new()
            {
                Frame = space,
                Body = Ball,
                AngularVelocity = new(0, 0, 2),
            }
        );
        WorldTesting.Tick(world, 3);

        Vector3 omega = world.AngularVelocityIn(ball, turning);

        DPose relative = world.Frames.Relative(space, turning);
        Double3 expected =
            relative.TransformDirection(world.AngularVelocityIn(ball, space))
            + world.Frames.AngularVelocityRelative(space, turning);
        TestPrecision
            .RoughlyEqual(omega, expected.ToVector3(), 1e-5f)
            .Should()
            .BeTrue($"the read gave {omega}, not {expected}");
        Vector3 fromSnapshot = world.Snapshot.AngularVelocityIn(ball, turning).Found();
        fromSnapshot.Should().Be(omega);
    }

    [Fact]
    public void AReadInAStaleFrameThrowsInTheWorld()
    {
        using World world = WorldTesting.NewWorld(backend: out _);
        FrameHandle space = Space(world);
        Entity ball = world.Spawn(new() { Frame = space, Body = Ball });
        FrameHandle stale = StaleFrame(world, space);

        world.Invoking(w => w.PoseIn(ball, stale)).Should().Throw<ArgumentException>();
        world.Invoking(w => w.VelocityIn(ball, stale)).Should().Throw<ArgumentException>();
        world.Invoking(w => w.AngularVelocityIn(ball, stale)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AReadInAStaleFrameThrowsInSelf()
    {
        using World world = WorldTesting.NewWorld(backend: out _);
        FrameHandle space = Space(world);
        FrameHandle stale = StaleFrame(world, space);
        var thrown = new List<Type>();
        var reader = new Reader();
        reader.Read = self =>
        {
            reader.Read = null;
            foreach (
                SelfRead read in new SelfRead[]
                {
                    s => s.PoseIn(stale),
                    s => s.VelocityIn(stale),
                    s => s.AngularVelocityIn(stale),
                }
            )
                try
                {
                    read(self);
                }
                catch (Exception ex)
                {
                    thrown.Add(ex.GetType());
                }
        };
        world.Spawn(
            new()
            {
                Frame = space,
                Body = Ball,
                Components = [reader],
            }
        );

        WorldTesting.Tick(world, 2);

        thrown.Should().Equal(typeof(ArgumentException), typeof(ArgumentException), typeof(ArgumentException));
    }

    [Fact]
    public void AReadInAStaleFrameIsFalseInTheSnapshot()
    {
        using World world = WorldTesting.NewWorld(backend: out _);
        FrameHandle space = Space(world);
        Entity ball = world.Spawn(new() { Frame = space, Body = Ball });
        FrameHandle stale = StaleFrame(world, space);
        Snapshot snapshot = world.Snapshot;

        snapshot.PoseIn(ball, stale).Should().BeNull();
        snapshot.VelocityIn(ball, stale).Should().BeNull();
        snapshot.AngularVelocityIn(ball, stale).Should().BeNull();
        snapshot.VelocityIn(ball, space).Should().NotBeNull();
    }

    [Fact]
    public void ATeleportAndAVelocityRequestLandInTheEntitysOwnFrame()
    {
        using World world = WorldTesting.NewWorld(backend: out _);
        FrameHandle space = Space(world);
        var pilot = new Pilot
        {
            Once = body =>
            {
                body.Teleport(WorldTesting.At(7, 8, 9));
                body.SetVelocity(new(0, 0, 0));
            },
        };
        Entity ball = world.Spawn(
            new()
            {
                Frame = space,
                Body = Ball,
                LinearVelocity = new(1, 0, 0),
                Components = [pilot],
            }
        );

        WorldTesting.Tick(world, 2);

        Double3 position = world.PoseIn(ball, space).Position;
        TestPrecision.RoughlyEqual(position, new(7, 8, 9), 1e-4).Should().BeTrue($"the read gave {position}");
    }
}

internal static class TestPrecision
{
    public static bool RoughlyEqual(Double3 a, Double3 b, double tolerance = 1e-9) => (a - b).Length <= tolerance;

    public static bool RoughlyEqual(Vector3 a, Vector3 b, float tolerance) => (a - b).Length() <= tolerance;
}
