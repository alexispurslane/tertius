using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// States that only a bug produces, on the World and the real backend. A Debug build breaks a contract. A Release build
/// skips the state, records it in <see cref="World.Errors"/>, and goes on with everything else.
/// </summary>
public class WorldBugOnlyTests
{
    /// <summary>
    /// The World refuses at spawn every body that physics cannot create, so a creation that fails in the backend comes only
    /// from a bug. A test stands in for that bug: it puts a dynamic body without mass for an entity that the World does not
    /// know first in the batch that also creates a ball.
    /// </summary>
    [Fact]
    public void ACreationThatFailsInTheBackendIsABugAndTheRestOfTheBatchStillApplies()
    {
        var faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 }));
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, faults);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        world.Tick();
        var stranger = new Entity(999, 1);
        faults.EditNextChanges = changes => changes.BodiesCreated.Insert(0,
            new BodyCreate(stranger, ground, new BodyDesc(new SphereShapeDesc(1), Mass: 0), At(5, 5), default, default));
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 5), Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 1) });

#if TERTIUS_CONTRACTS
        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{stranger}*");
#else
        Tick(world, 120);

        world.Errors.Should().ContainSingle().Which.Should().Contain(stranger.ToString()).And.Contain("mass").And.Contain("Only a bug")
            .And.NotContain("failed to apply changes", "the backend applied the rest of the batch, and it named the change that it skipped");
        faults.Jolt.TryGetBodyFrame(stranger, out _).Should().BeFalse("the failed creation left nothing behind");
        faults.Jolt.TryGetBodyFrame(ball, out _).Should().BeTrue("the rest of the batch still applied");
        world.GetPose(ball).Position.Y.Should().BeApproximately(0.5, 0.01, "the ball fell to the floor");
#endif
    }

    /// <summary>
    /// The World refuses a driver that the hull of a frame cannot follow, so a frame tree with such a driver comes only
    /// from a bug. A test stands in for that bug: it gives a dock whose hull is a mesh the physics driver through the
    /// frame tree, past the World. A Release build keeps the hull static and records the state in World.Errors, not as a
    /// warning.
    /// </summary>
    [Fact]
    public void ADriverThatAHullCannotFollowIsABugThatAReleaseBuildRecordsAsAnError()
    {
        var warnings = new List<string>();
        using World world = NewWorld(out JoltPhysicsBackend backend, warnings);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        var ramp = new MeshShapeDesc([new(-5, 0, -5), new(5, 0, -5), new(5, 2, 5), new(-5, 2, 5)], [0, 2, 1, 0, 3, 2]);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(50),
            Body = new BodyDesc(ramp, BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
        });
        FrameHandle dock = world.GetOwnedFrame(owner);
        world.Tick();

        world.Frames.SetDriver(dock, MotionDriver.Physics);

#if TERTIUS_CONTRACTS
        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{owner}*");
#else
        Tick(world, 3);

        world.Errors.Should().ContainSingle().Which.Should().Contain(owner.ToString()).And.Contain("Only a bug");
        warnings.Should().BeEmpty("a state that only a bug produces is an error, not a warning");
        world.Frames.LocalPose(dock).Should().Be(At(50), "the hull kept its static motion");
#endif
    }
}
