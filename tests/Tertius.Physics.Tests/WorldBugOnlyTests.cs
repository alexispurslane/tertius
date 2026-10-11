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

        world.Errors.Should().ContainSingle().Which.Should().Contain(stranger.ToString()).And.Contain("mass");
        faults.Jolt.TryGetBodyFrame(stranger, out _).Should().BeFalse("the failed creation left nothing behind");
        faults.Jolt.TryGetBodyFrame(ball, out _).Should().BeTrue("the rest of the batch still applied");
        world.GetPose(ball).Position.Y.Should().BeApproximately(0.5, 0.01, "the ball fell to the floor");
#endif
    }
}
