using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

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
        world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(0.5, 0.01, "the ball fell to the floor");
#endif
    }

    /// <summary>Casts one ray along +X from its own position on tick <paramref name="onTick"/>, and keeps the answers.</summary>
    private sealed class RayOnTick(long onTick) : IComponent
    {
        public List<RayHit> Answers { get; } = new();

        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tertius.Tick>().And<RayHit>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref RayHit hit in events.Of<RayHit>())
                Answers.Add(hit);
            if (self.Tick == onTick)
                self.Physics.CastRay(Double3.Zero, Double3.UnitX, 50, default, 1);
        }
    }

    /// <summary>
    /// The World destroys the body of an entity in the same sync point that destroys the entity, so Jolt never holds the
    /// body of an entity that is not live when a query runs. A body that stays comes only from a bug. A test stands in for
    /// that bug: it removes the entity from the change batch that would destroy its body. A Debug build breaks a contract
    /// when the ray meets the body. A Release build skips the body, so that the ray goes on to the next one, and records
    /// the state in <see cref="World.Errors"/>.
    /// </summary>
    [Fact]
    public void ARayThatMeetsTheBodyOfAnEntityThatIsNotLiveIsABugAndAReleaseBuildSkipsTheBodyAndRecordsAnError()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 0);
        var asker = new RayOnTick(onTick: 1);
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0), Components = [asker] });
        Entity dead = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(10), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        Entity beyond = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(20), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        world.Tick();
        faults.EditNextChanges = changes => changes.BodiesDestroyed.Remove(dead);
        world.Destroy(dead);

#if TERTIUS_CONTRACTS
        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{dead}*which is not live*");
#else
        Tick(world, 2);

        world.Errors.Should().ContainSingle().Which.Should().Contain("physics reported 1 bodies of entities that are not live").And.Contain("The ray cast skipped them");
        asker.Answers.Should().ContainSingle().Which.Entity.Should().Be(beyond, "the ray skipped the body of the entity that is not live");
        faults.Jolt.TryGetBodyFrame(dead, out _).Should().BeTrue("the edit kept the body in Jolt, which is the bug that this test stands in for");
#endif
    }

    /// <summary>
    /// The guard of every writer, the drops of step 4 and the check of a spawn let a body take only a motion intent whose
    /// case its motion in the step takes (docs/plans/rails-components.md, "The flow per kind of entity"). So physics meets
    /// any other only through a bug. A test stands in for that bug: it gives the step a motion intent that the body's motion
    /// refuses. A Release build ignores the motion intent, so the body stays where it rests, and records the bug.
    /// </summary>
    [Theory]
    [InlineData("end pose", BodyMotion.Dynamic)]
    [InlineData("full sample", BodyMotion.Dynamic)]
    [InlineData("steer target", BodyMotion.Kinematic)]
    [InlineData("end pose", BodyMotion.Static)]
    public void AMotionIntentWhoseCaseTheBodysMotionRefusesIsABugAndAReleaseBuildIgnoresIt(string write, BodyMotion motion)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity body = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0), Body = Ball() with { Motion = motion } });
        Tick(world, 2);
        DPose end = At(1);
        MotionIntent move = write switch
        {
            "end pose" => new MotionIntent(new EndPose(end)),
            "full sample" => new MotionIntent(new MotionSample(end, new Double3(60, 0, 0), new Double3(60, 0, 0), Double3.Zero, Double3.Zero, Double3.Zero)),
            _ => new MotionIntent(new SteerTarget(end)),
        };
        faults.EditNextIntents = intents => intents.Add((body, new PhysicsIntent { Frame = space, Move = move }));

#if TERTIUS_CONTRACTS
        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{body}*");
#else
        Tick(world, 2);

        world.Errors.Should().ContainSingle().Which.Should().Contain(body.ToString()).And.Contain("Only a bug");
        world.PoseIn(body, space).Position.Length.Should().BeLessThan(1e-9, "physics ignored the motion intent");
#endif
    }
}
