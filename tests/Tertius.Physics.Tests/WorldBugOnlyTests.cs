using System.Runtime.CompilerServices;
using JoltPhysicsSharp;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// States that only a bug produces, on the World and the real backend. A Debug build breaks a contract. A Release build
/// skips the state, records it in <see cref="World.Errors"/>, and goes on with everything else.
/// </summary>
public sealed class WorldBugOnlyTests
{
    /// <summary>
    /// The World refuses at spawn every body that physics cannot create, so a creation that fails in the backend comes only
    /// from a bug. A test stands in for that bug: it puts a dynamic body without mass for an entity that the World does not
    /// know first in the batch that also creates a ball.
    /// </summary>
    [Fact]
    public void ACreationThatFailsInTheBackendIsABugAndTheRestOfTheBatchStillApplies()
    {
        var faults = new FaultInjectingBackend(new(new() { WorkerThreads = 2 }));
        using var world = new World(
            new()
            {
                MaxDegreeOfParallelism = 1,
                DebugChecks = true,
                DisposePhysics = true,
            },
            faults
        );
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        world.Tick();
        var stranger = new Entity(999, 1);
        faults.EditNextChanges = changes =>
            changes.BodiesCreated.Insert(
                0,
                new(
                    Entity: stranger,
                    Frame: ground,
                    Desc: new(Shape: new SphereShapeDesc(1), Mass: 0),
                    Pose: At(5, 5),
                    LinearVelocity: default,
                    AngularVelocity: default
                )
            );
        Entity ball = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, 5),
                Body = new(Shape: new SphereShapeDesc(0.5f), Mass: 1),
            }
        );

#if TERTIUS_CONTRACTS
        Action tick = world.Tick;

        tick.Should().Throw<ContractException>().WithMessage($"*{stranger}*");
#else
        Tick(world, 120);

        world
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(stranger.ToString())
            .And.Contain("mass")
            .And.Contain("Only a bug")
            .And.NotContain(
                "failed to apply changes",
                "the backend applied the rest of the batch, and it named the change that it skipped"
            );
        faults.Jolt.TryGetBodyFrame(stranger, out _).Should().BeFalse("the failed creation left nothing behind");
        faults.Jolt.TryGetBodyFrame(ball, out _).Should().BeTrue("the rest of the batch still applied");
        world.PoseInOwnFrame(ball).Position.Y.Should().BeApproximately(0.5, 0.01, "the ball fell to the floor");
#endif
    }

    /// <summary>Casts one ray along +X from its own position on tick <paramref name="onTick"/>, and keeps the answers.</summary>
    private sealed class RayOnTick(long onTick) : IComponent
    {
        public List<RayHit> Answers { get; } = [];

        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>().And<RayHit>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref RayHit hit in events.Of<RayHit>())
                Answers.Add(hit);
            if (self.Tick == onTick)
                self.Physics.CastRay(from: Double3.Zero, direction: Double3.UnitX, length: 50, query: default, tag: 1);
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
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 0);
        var asker = new RayOnTick(onTick: 1);
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0),
                Components = [asker],
            }
        );
        Entity dead = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(10),
                Body = new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static),
            }
        );
        Entity beyond = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(20),
                Body = new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Static),
            }
        );
        world.Tick();
        faults.EditNextChanges = changes => changes.BodiesDestroyed.Remove(dead);
        world.Destroy(dead);

#if TERTIUS_CONTRACTS
        Action tick = world.Tick;

        tick.Should().Throw<ContractException>().WithMessage($"*{dead}*which is not live*");
#else
        Tick(world, 2);

        world
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("physics reported 1 bodies of entities that are not live")
            .And.Contain("The ray cast skipped them");
        asker
            .Answers.Should()
            .ContainSingle()
            .Which.Entity.Should()
            .Be(beyond, "the ray skipped the body of the entity that is not live");
        faults
            .Jolt.TryGetBodyFrame(dead, out _)
            .Should()
            .BeTrue("the edit kept the body in Jolt, which is the bug that this test stands in for");
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
    public void AMotionIntentWhoseCaseTheBodysMotionRefusesIsABugAndAReleaseBuildIgnoresIt(
        string write,
        BodyMotion motion
    )
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Entity body = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0),
                Body = Ball() with { Motion = motion },
            }
        );
        Tick(world, 2);
        DPose end = At(1);
        MotionIntent move = write switch
        {
            "end pose" => new(new EndPose(end)),
            "full sample" => new(
                new MotionSample(
                    Pose: end,
                    ChordVelocity: new(60, 0, 0),
                    Velocity: new(60, 0, 0),
                    Acceleration: Double3.Zero,
                    AngularVelocity: Double3.Zero,
                    AngularAcceleration: Double3.Zero
                )
            ),
            _ => new(new SteerTarget(end)),
        };
        faults.EditNextIntents = intents => intents.Add((body, new() { Frame = space, Move = move }));

#if TERTIUS_CONTRACTS
        Action tick = world.Tick;

        tick.Should().Throw<ContractException>().WithMessage($"*{body}*");
#else
        Tick(world, 2);

        world.Errors.Should().ContainSingle().Which.Should().Contain(body.ToString()).And.Contain("Only a bug");
        world.PoseIn(body, space).Position.Length.Should().BeLessThan(1e-9, "physics ignored the motion intent");
#endif
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Find")]
    private static extern BodyRecord? RecordOf(JoltPhysicsBackend backend, Entity entity);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "WantGhost")]
    private static extern void WantGhost(
        JoltPhysicsBackend backend,
        BodyRecord record,
        FrameHandle frame,
        in DPose pose,
        Double3 linearVelocity,
        Double3 angularVelocity,
        BodyLayer role
    );

    /// <summary>
    /// No frame gets two ghosts of one body, so a ghost pass wants each ghost once, and a ghost changes its role only before
    /// the pass wants it (see <c>GhostBody.Role</c>). A ghost that a pass kept counts as wanted until the next pass. A test
    /// stands in for a bug that wants a ghost again in another role: a parked shuttle beside a dock has a child ghost in
    /// the dock, with the static role of the shuttle's interior, and the test wants that ghost again in the frame hull role
    /// of an anchor ghost. A Debug build breaks a contract. A Release build skips the second want, so the ghost keeps its
    /// role and its Jolt body.
    /// </summary>
    [Fact]
    public void AGhostThatIsWantedAgainInAnotherRoleComesOnlyFromABugAndKeepsItsRole()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        FrameHandle dock = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = At(43),
                    AsFrame = new(FrameBounds.Sphere(20)),
                }
            )
        );
        Entity shuttle = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(19),
                Body = new(
                    Shape: new BoxShapeDesc(new(2.5f)),
                    Motion: BodyMotion.Static,
                    InteriorShape: new SphereShapeDesc(0.1f)
                ),
                AsFrame = new(FrameBounds.Sphere(11)),
            }
        );
        world.Tick();

        JoltPhysicsBackend.BodyFacts GhostInDock() =>
            faults
                .Jolt.BodiesIn(dock)
                .Single(facts =>
                    BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, shuttle)
                );

        JoltPhysicsBackend.BodyFacts kept = GhostInDock();
        BodyRecord record = RecordOf(faults.Jolt, shuttle)!;

        Action want = () =>
            WantGhost(
                backend: faults.Jolt,
                record: record,
                frame: dock,
                pose: new(kept.Position, DQuaternion.Identity),
                linearVelocity: default,
                angularVelocity: default,
                role: BodyLayer.FrameHull
            );

#if TERTIUS_CONTRACTS
        want.Should().Throw<ContractException>().WithMessage($"*{shuttle}*{dock}*Only a bug*");
#else
        want.Should().NotThrow();
#endif
        GhostInDock().Id.Should().Be(kept.Id, "the second want is skipped");
        GhostInDock().Layer.Should().Be(kept.Layer);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ExistingWorld")]
    private static extern FrameWorld? SystemOf(JoltPhysicsBackend backend, FrameHandle frame);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ghosts")]
    private static extern ref Dictionary<BodyID, MotionType> ContactMotionsOf(FrameWorld world);

    /// <summary>
    /// The backend records the contact motion of every ghost when it adds the ghost to a system, so a ghost without one
    /// comes only from a bug. A test stands in for that bug: a pod, a frame with a dynamic hull in a frame above a static
    /// slab, rests on the slab's ghost, and the test forgets the contact motion of that ghost. When the pod parks, the
    /// check after the change of motion asks the pair test about the pod's hull and the slab's ghost, and the pair test
    /// finds the ghost without a contact motion. A Debug build breaks a contract. A Release build judges the ghost by its
    /// Jolt motion, which is static, as its contact motion is, so the pod's touch of the slab ends as when a pod parks on
    /// the slab in one frame, and the state goes to <see cref="World.Errors"/>.
    /// </summary>
    [Fact]
    public void AGhostWithoutAContactMotionThatThePairTestFindsComesOnlyFromABug()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults, debugChecks: false);
        var gravity = GravityField.Uniform(new(0, -9.81f, 0));
        FrameHandle ground = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), gravity));
        Entity slab = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, -0.5),
                Body = new(Shape: new BoxShapeDesc(new(20, 0.5f, 20)), Motion: BodyMotion.Static),
            }
        );
        FrameHandle home = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = At(0, 5),
                    AsFrame = new(FrameBounds.Sphere(20), gravity),
                }
            )
        );
        var podLog = new CollisionLog();
        Entity pod = world.Spawn(
            new()
            {
                Frame = home,
                Pose = At(0, -2),
                Body = new(
                    Shape: new BoxShapeDesc(new(0.5f)),
                    Mass: 100,
                    Restitution: 0,
                    InteriorShape: new SphereShapeDesc(0.1f)
                ),
                AsFrame = new(FrameBounds.Sphere(1.5f), gravity),
                Components = [podLog],
            }
        );
        Tick(world, 180);
        podLog.NewsOf(slab).Should().Equal(Started);
        BodyID ghost = faults
            .Jolt.BodiesIn(home)
            .Single(facts =>
                BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, slab)
            )
            .Id;
        ContactMotionsOf(SystemOf(faults.Jolt, home)!).Remove(ghost).Should().BeTrue();
        world.SetMotionNextTick(owner: pod, motion: BodyMotion.Static);

        Action ticks = () => Tick(world, 3);

#if TERTIUS_CONTRACTS
        ticks.Should().Throw<ContractException>().WithMessage("*no contact motion*Only a bug*");
#else
        ticks.Should().NotThrow();
        world.Errors.Should().ContainSingle().Which.Should().Contain("no contact motion").And.Contain("Only a bug");
        podLog.NewsOf(slab).Should().Equal(Started, Ended);
#endif
    }
}
