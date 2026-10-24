using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The owner of a frame changes its hull's motion in a tick with <see cref="BodyHandle.SetMotion"/>, for example to park a
/// ship (docs/plans/rails-components.md, "Changes of motion"). Step k is the physics step that runs beside tick k. A change
/// written in tick 1 is in the intents that step 2 applies, and physics gives the Jolt body the new motion at the start of
/// step 2. The stored motion, which the frame tree keeps (<see cref="FrameTree.HullMotion"/>), describes the last finished
/// step, like the collision rows: it changes at step 4 of the sync point after step 2. The motion of a step that has not
/// finished is derived from the pending intent. <c>SetMotion</c> refuses, with an always-on guard, a body that is no hull
/// and a motion that the hull cannot take.
/// </summary>
public sealed class SetMotionTests
{
    /// <summary>A triangle mesh. Jolt never moves a mesh, so a hull of this shape can only be static.</summary>
    private static readonly MeshShapeDesc Ramp = new([new(-5, 0, -5), new(5, 0, -5), new(5, 2, 5), new(-5, 2, 5)],
        [0, 2, 1, 0, 3, 2]);

    /// <summary>A box of 1000 kg with sides of 4 m, which can take every motion of a hull.</summary>
    private static readonly BodyDesc ShipBody = new(Shape: new BoxShapeDesc(new(2)), Mass: 1000);

    /// <summary>A frame 100 m along +X in the root, whose owner has the hull <paramref name="body"/> with
    /// <paramref name="motion"/>.</summary>
    private static FrameHandle SpawnHull(TestWorld test, BodyMotion motion, BodyDesc body,
        params IComponent[] components) =>
        test.World.GetOwnedFrame(test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = TestWorld.At(100),
            AsFrame = new(FrameBounds.Sphere(10)),
            Body = body with { Motion = motion },
            Components = components
        }));

    /// <summary>
    /// Runs one tick in which a component of <paramref name="entity"/> calls <c>SetMotion(motion)</c>. Returns what the call
    /// threw, or null.
    /// </summary>
    private static Exception? SetMotionInATick(TestWorld test, Entity entity, BodyMotion motion)
    {
        Exception? caught = null;
        bool done = false;
        test.World.AddComponent(entity, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (done)
                return;
            done = true;
            try
            {
                self.Body.SetMotion(motion);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        }));
        test.Run(1);
        return caught;
    }

    /// <summary>
    /// Asserts, after the tick of a refused <c>SetMotion</c>, that the step that applies that tick's writes runs the hull of
    /// <paramref name="frame"/> with <paramref name="motion"/>, the motion that it had, and that the owner's intent for that
    /// step holds no change of motion. A change that the tick wrote shows here at once. The stored motion would show it only
    /// at the sync point after that step, so it cannot tell a refused call from a call that wrote its change.
    /// </summary>
    private static void NoChangeOfMotionIsPending(TestWorld test, FrameHandle frame, BodyMotion motion)
    {
        test.World.NextStepHullMotion(frame).Should().Be(motion, "a refused change of motion writes nothing");
        (test.World.Entities.NextStepIntent(frame.Entity).Flags & IntentFlags.SetMotion).Should().Be(IntentFlags.None);
    }

    private static BodyMotion FactsMotion(TestWorld test, Entity entity)
    {
        test.World.Snapshot.TryGetBody(entity, out BodyFacts facts).Should().BeTrue();
        return facts.Motion;
    }

    // ---- When a change acts ---------------------------------------------------------------------------------------

    /// <summary>
    /// Tick 1 writes the change, and step 2, beside tick 2, applies it. The stored motion and the body facts describe the
    /// last finished step, so they show the old motion through tick 2, and the new one from step 4 of the sync point after
    /// step 2, where the collision rows change too.
    /// </summary>
    [Fact]
    public void AChangeOfMotionShowsInTheTreeAndTheBodyFactsAtTheSyncPointAfterTheStepThatAppliesIt()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        FrameHandle ship = SpawnHull(test, BodyMotion.Dynamic, ShipBody);
        test.Run(1);
        var seen = new List<(BodyMotion? Tree, BodyMotion Facts)>();
        test.World.AddComponent(ship.Entity, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (seen.Count == 0)
                self.Body.SetMotion(BodyMotion.Static);
            self.Snapshot.TryGetBody(self.Entity, out BodyFacts facts);
            seen.Add((self.Snapshot.Frames.HullMotion(ship), facts.Motion));
        }));

        test.Run(1);

        seen.Should().Equal([(BodyMotion.Dynamic, BodyMotion.Dynamic)],
            "tick 1 runs beside step 1, which has the old motion");
        test.World.Frames.HullMotion(ship).Should().Be(BodyMotion.Dynamic, "step 1 finished with the old motion");
        FactsMotion(test, ship.Entity).Should().Be(BodyMotion.Dynamic);

        test.Run(1);

        seen[^1].Should().Be((BodyMotion.Dynamic, BodyMotion.Dynamic),
            "tick 2 runs beside step 2, which has not finished");
        test.World.Frames.HullMotion(ship).Should().Be(BodyMotion.Static,
            "step 4 of the sync point after step 2 took the change that step 2 applied");
        FactsMotion(test, ship.Entity).Should().Be(BodyMotion.Static);

        test.Run(1);

        seen[^1].Should().Be((BodyMotion.Static, BodyMotion.Static),
            "tick 3 reads the state of step 2, the last finished step");
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheLastChangeOfMotionInATickWins()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        FrameHandle ship = SpawnHull(test, BodyMotion.Dynamic, ShipBody);
        test.Run(1);
        bool done = false;
        test.World.AddComponent(ship.Entity, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (done)
                return;
            done = true;
            self.Body.SetMotion(BodyMotion.Static);
            self.Body.SetMotion(BodyMotion.Kinematic);
        }));

        test.Run(2);

        test.World.Frames.HullMotion(ship).Should().Be(BodyMotion.Kinematic);
        FactsMotion(test, ship.Entity).Should().Be(BodyMotion.Kinematic);
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Without a physics backend the World still counts its steps: the change goes on to the intents of the next step, and
    /// the stored motion takes it one sync point after the tick that wrote it, as with a backend.
    /// </summary>
    [Fact]
    public void AChangeOfMotionWorksWithoutAPhysicsBackend()
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnHull(test, BodyMotion.Dynamic, ShipBody);
        test.Run(1);

        test.World.SetMotionNextTick(owner: ship.Entity, motion: BodyMotion.Static);
        test.Run(1);

        test.World.Frames.HullMotion(ship).Should()
            .Be(BodyMotion.Dynamic, "the step that applies the change has not finished");
        test.World.NextStepHullMotion(ship).Should().Be(BodyMotion.Static);

        test.Run(1);

        test.World.Frames.HullMotion(ship).Should().Be(BodyMotion.Static);
        FactsMotion(test, ship.Entity).Should().Be(BodyMotion.Static);
        test.World.Errors.Should().BeEmpty();
    }

    // ---- The motion of a step that has not finished -----------------------------------------------------------------

    /// <summary>
    /// <see cref="BodyHandle.NextStepMotion"/> answers for the step that applies the tick's writes: the change that the tick
    /// wrote so far, else the change of the tick before, which the running step applies, else the stored motion. So the tick
    /// after a change sees the new motion there, while the body facts still describe the last finished step.
    /// </summary>
    [Fact]
    public void TheNextStepMotionCountsTheChangesThatNoStepHasFinishedWith()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        FrameHandle ship = SpawnHull(test, BodyMotion.Dynamic, ShipBody);
        test.Run(1);
        var seen = new List<(BodyMotion? Before, BodyMotion? After, BodyMotion Facts)>();
        test.World.AddComponent(ship.Entity, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            BodyMotion? before = self.Body.NextStepMotion;
            if (seen.Count == 0)
                self.Body.SetMotion(BodyMotion.Static);
            self.Snapshot.TryGetBody(self.Entity, out BodyFacts facts);
            seen.Add((before, self.Body.NextStepMotion, facts.Motion));
        }));

        test.Run(3);

        seen.Should().Equal(
            [
                (BodyMotion.Dynamic, BodyMotion.Static, BodyMotion.Dynamic),
                (BodyMotion.Static, BodyMotion.Static, BodyMotion.Dynamic),
                (BodyMotion.Static, BodyMotion.Static, BodyMotion.Static)
            ],
            "the tick's own change counts at once, the facts follow one sync point after the step that applies it");
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheNextStepMotionOfABodyThatIsNoHullIsTheMotionOfItsBody()
    {
        using var test = new TestWorld();
        Entity crate = test.World.Spawn(new()
            { Frame = test.Root, Body = ShipBody with { Motion = BodyMotion.Kinematic } });
        Entity dockOwner = test.World.Spawn(new() { AsFrame = new(FrameBounds.Sphere(10)), Body = ShipBody });
        Entity marker = test.World.Spawn(new() { Frame = test.Root });
        test.Run(1);
        var seen = new Dictionary<Entity, BodyMotion?>();
        foreach (Entity entity in new[] { crate, dockOwner, marker })
            test.World.AddComponent(entity,
                Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                    seen[self.Entity] = self.Body.NextStepMotion));

        test.Run(1);

        seen[crate].Should().Be(BodyMotion.Kinematic, "a body that owns no frame has the motion of its description");
        seen[dockOwner].Should()
            .Be(BodyMotion.Static, "the owner of a root frame has only the frame's static interior");
        seen[marker].Should().BeNull("an entity without a body has no motion");
    }

    /// <summary>
    /// The guards of a tick judge each write by the motion of the step that applies it, which counts the tick's own change
    /// of motion: a kinematic hull takes no velocity, and only a kinematic hull takes a motion sample.
    /// </summary>
    [Theory]
    [InlineData(BodyMotion.Dynamic, BodyMotion.Kinematic, "SetVelocity")]
    [InlineData(BodyMotion.Kinematic, BodyMotion.Dynamic, "MoveOnRails")]
    public void AWriteAfterTheTicksOwnChangeOfMotionIsJudgedByTheNewMotion(BodyMotion from, BodyMotion to, string write)
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnHull(test, from, ShipBody);
        test.Run(1);

        Exception? refused = WriteInATick(test, ship.Entity, self =>
        {
            self.Body.SetMotion(to);
            Write(self, write);
        });

        refused.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain(ship.ToString()).And
            .Contain("kinematic");
    }

    /// <summary>
    /// The tick after a change runs beside the step that applies it, and its writes reach the step after that, which has
    /// the new motion too. So its guards judge by the new motion, although the stored motion is still the old one.
    /// </summary>
    [Theory]
    [InlineData(BodyMotion.Dynamic, BodyMotion.Kinematic, "SetVelocity", "MoveOnRails")]
    [InlineData(BodyMotion.Kinematic, BodyMotion.Dynamic, "MoveOnRails", "SetVelocity")]
    public void TheGuardsOfTheTickAfterAChangeJudgeWritesByTheNewMotion(BodyMotion from, BodyMotion to,
        string refusedWrite, string takenWrite)
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnHull(test, from, ShipBody);
        test.Run(1);
        test.World.SetMotionNextTick(owner: ship.Entity, motion: to);
        test.Run(1);
        test.World.Frames.HullMotion(ship).Should().Be(from, "the step that applies the change has not finished");

        Exception? taken = null;
        Exception? refused = WriteInATick(test, ship.Entity, self =>
        {
            try
            {
                Write(self, takenWrite);
            }
            catch (Exception ex)
            {
                taken = ex;
            }

            Write(self, refusedWrite);
        });

        refused.Should().BeOfType<InvalidOperationException>(refusedWrite);
        taken.Should().BeNull(takenWrite);
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>What a test does with the running entity in a tick. A delegate of its own, because <see cref="Self"/> is a ref struct.</summary>
    private delegate void SelfAction(Self self);

    /// <summary>Runs one tick in which a component of <paramref name="entity"/> runs <paramref name="write"/> once. Returns what it threw, or null.</summary>
    private static Exception? WriteInATick(TestWorld test, Entity entity, SelfAction write)
    {
        Exception? caught = null;
        bool done = false;
        test.World.AddComponent(entity, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (done)
                return;
            done = true;
            try
            {
                write(self);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        }));
        test.Run(1);
        return caught;
    }

    /// <summary>Writes a velocity of 1 m/s along +X, or the sample of <see cref="HoldsOnRails"/>.</summary>
    private static void Write(Self self, string write)
    {
        if (write == "SetVelocity")
            self.Body.SetVelocity(new(1, 0, 0));
        else
            self.MoveOnRails(HoldsOnRails.Sample);
    }

    // ---- What a change drops --------------------------------------------------------------------------------------

    /// <summary>A component that writes the same sample in every tick: the frame stands still 100 m along +X.</summary>
    private sealed class HoldsOnRails : IComponent
    {
        public static readonly MotionSample Sample =
            new(Pose: TestWorld.At(100), ChordVelocity: Double3.Zero, Velocity: Double3.Zero,
                Acceleration: Double3.Zero, AngularVelocity: Double3.Zero, AngularAcceleration: Double3.Zero);

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.MoveOnRails(Sample);
    }

    /// <summary>A component that writes the same end pose in every tick: the frame stands still 100 m along +X.</summary>
    private sealed class HoldsByEndPoses : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.MoveKinematic(HoldsOnRails.Sample.Pose);
    }

    /// <summary>
    /// The component runs before the change in the same tick, so it writes a motion intent that no step may apply: a dynamic
    /// hull takes no end pose and no full sample. The motion intent that the last step applied stays the record that the
    /// frame took.
    /// </summary>
    [Theory]
    [InlineData("full sample")]
    [InlineData("end pose")]
    public void AHullThatIsNotKinematicInTheNextStepDropsTheMotionIntentOfTheTick(string write)
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        IComponent writer = write == "full sample" ? new HoldsOnRails() : new HoldsByEndPoses();
        FrameHandle station = SpawnHull(test, BodyMotion.Kinematic, ShipBody, writer);
        test.Run(2);
        test.World.Entities.NextStepIntent(station.Entity).Move.HasValue.Should()
            .BeTrue("the component writes a motion intent in every tick");

        test.World.SetMotionNextTick(owner: station.Entity, motion: BodyMotion.Dynamic, remove: writer);
        test.Run(1);

        test.World.NextStepHullMotion(station).Should().Be(BodyMotion.Dynamic);
        test.World.Entities.NextStepIntent(station.Entity).Move.HasValue.Should()
            .BeFalse("the next step applies no motion intent to a dynamic hull");
        test.World.Frames.LocalPose(station).Should().Be(HoldsOnRails.Sample.Pose,
            "the motion intent that the step of the tick applied moved the frame");
        test.World.Frames.Followed(station).Should()
            .Be(write == "full sample" ? FollowKind.Rails : FollowKind.Kinematic);
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A dynamic hull steers in a tick, and the same tick makes it kinematic or static. Only a dynamic body takes a steer
    /// target, so step 4 drops it by the guard's rule, with the hull's motion in the next step.
    /// </summary>
    [Theory]
    [InlineData(BodyMotion.Kinematic)]
    [InlineData(BodyMotion.Static)]
    public void AHullThatTurnsKinematicOrStaticInTheTickDropsItsSteerTarget(BodyMotion to)
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        FrameHandle ship = SpawnHull(test, BodyMotion.Dynamic, ShipBody);
        test.Run(1);

        Exception? refused = WriteInATick(test, ship.Entity, self =>
        {
            self.Body.SteerTo(TestWorld.At(103));
            self.Body.SetMotion(to);
        });

        refused.Should().BeNull("the steer target came while the next step was to be dynamic");
        test.World.NextStepHullMotion(ship).Should().Be(to);
        test.World.Entities.NextStepIntent(ship.Entity).Move.HasValue.Should()
            .BeFalse("the next step applies no steer target to a hull that is not dynamic");
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>True when the next step applies a full sample to the owner of <paramref name="frame"/>, between two ticks.</summary>
    private static bool HasNextSample(TestWorld test, FrameHandle frame) =>
        test.World.Entities.NextStepIntent(frame.Entity).Move.TryGetValue(out MotionSample _);

    /// <summary>
    /// A sample written while the tick's first change made the hull kinematic goes when the last change of the tick makes it
    /// dynamic again: the drops follow the motion of the next step, not whether it differs from the motion of the running
    /// step.
    /// </summary>
    [Fact]
    public void ASampleWrittenBetweenTwoChangesOfOneTickGoesWhenTheLastChangeIsNotKinematic()
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnHull(test, BodyMotion.Dynamic, ShipBody);
        test.Run(1);

        Exception? refused = WriteInATick(test, ship.Entity, self =>
        {
            self.Body.SetMotion(BodyMotion.Kinematic);
            self.MoveOnRails(HoldsOnRails.Sample);
            self.Body.SetMotion(BodyMotion.Dynamic);
        });

        refused.Should().BeNull("the sample came while the next step was to be kinematic");
        HasNextSample(test, ship).Should().BeFalse("the next step applies no sample to a dynamic hull");
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A dynamic hull may set its velocities and a steer target in a tick. When the same tick makes the hull kinematic,
    /// they go: a kinematic hull moves only by its samples and teleports, which tell its frame how to follow. A force of the
    /// same tick stays, because physics applies it only to a dynamic body.
    /// </summary>
    [Fact]
    public void AHullThatBecomesKinematicDropsTheVelocitySetInTheSameTick()
    {
        using TestWorld test = TestWorld.OnJolt(faults: out _);
        FrameHandle ship = SpawnHull(test, BodyMotion.Dynamic, ShipBody);
        test.Run(1);
        bool done = false;
        test.World.AddComponent(ship.Entity, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (done)
                return;
            done = true;
            self.Body.SetVelocity(new(5, 0, 0));
            self.Body.SetAngularVelocity(new(0, 1, 0));
            self.Body.SteerTo(TestWorld.At(3));
            self.Body.AddForce(new(10, 0, 0));
            self.Body.SetMotion(BodyMotion.Kinematic);
        }));

        test.Run(1);

        PhysicsIntent next = test.World.Entities.NextStepIntent(ship.Entity);
        next.Flags.Should().Be(IntentFlags.SetMotion, "the velocities went, and the change of motion stays");
        next.Move.HasValue.Should().BeFalse("the steer target went");
        next.Motion.Should().Be(BodyMotion.Kinematic);
        next.Force.Should().Be(new Vector3(10, 0, 0),
            "a force stays, because physics applies it only to a dynamic body");
        test.World.Errors.Should().BeEmpty();
    }

    // ---- Refusals -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("a plain body", "owns no frame")]
    [InlineData("a root frame's owner", "root frame")]
    [InlineData("an entity without a body", "no physics body")]
    public void SetMotionRefusesABodyThatIsNoHull(string which, string reason)
    {
        using var test = new TestWorld();
        Entity entity = which switch
        {
            "a plain body" => test.World.Spawn(new() { Frame = test.Root, Body = ShipBody }),
            "a root frame's owner" => test.World.Spawn(new()
            {
                AsFrame = new(FrameBounds.Sphere(10)),
                Body = ShipBody
            }),
            _ => test.World.GetOwnedFrame(test.World.Spawn(new()
            {
                Frame = test.Root,
                AsFrame = new(FrameBounds.Sphere(10))
            })).Entity
        };
        test.Run(1);

        Exception? refused = SetMotionInATick(test, entity, BodyMotion.Static);

        refused.Should().BeOfType<InvalidOperationException>(which).Which.Message.Should().Contain(entity.ToString())
            .And.Contain("hull").And.Contain(reason);
    }

    [Fact]
    public void SetMotionRefusesADynamicMotionForAHullWithoutMass()
    {
        using var test = new TestWorld();
        FrameHandle dock = SpawnHull(test, BodyMotion.Static, ShipBody with { Mass = 0 });
        test.Run(1);

        Exception? refused = SetMotionInATick(test, dock.Entity, BodyMotion.Dynamic);

        refused.Should().BeOfType<ArgumentException>().Which.Message.Should().Contain(dock.Entity.ToString()).And
            .Contain(dock.ToString()).And.Contain("mass");
        ((ArgumentException)refused!).ParamName.Should().Be("motion");
        NoChangeOfMotionIsPending(test, dock, BodyMotion.Static);
    }

    [Theory]
    [InlineData(BodyMotion.Kinematic, false)]
    [InlineData(BodyMotion.Kinematic, true)]
    [InlineData(BodyMotion.Dynamic, false)]
    [InlineData(BodyMotion.Dynamic, true)]
    public void SetMotionRefusesAMotionOtherThanStaticForAMeshHull(BodyMotion motion, bool inCompound)
    {
        using var test = new TestWorld();
        ShapeDesc shape = inCompound ? new CompoundShapeDesc([new(Ramp, Pose.Identity)]) : Ramp;
        FrameHandle dock = SpawnHull(test, BodyMotion.Static, new(Shape: shape, Mass: 1000));
        test.Run(1);

        Exception? refused = SetMotionInATick(test, dock.Entity, motion);

        refused.Should().BeOfType<ArgumentException>().Which.Message.Should().Contain(dock.Entity.ToString()).And
            .Contain(dock.ToString()).And.Contain("mesh");
        NoChangeOfMotionIsPending(test, dock, BodyMotion.Static);
    }

    [Theory]
    [InlineData(BodyMotion.Character)]
    [InlineData((BodyMotion)7)]
    public void SetMotionRefusesTheCharacterMotionAndAMotionOutsideTheEnum(BodyMotion motion)
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnHull(test, BodyMotion.Dynamic, ShipBody);
        test.Run(1);

        Exception? refused = SetMotionInATick(test, ship.Entity, motion);

        refused.Should().BeOfType<ArgumentException>().Which.Message.Should().Contain(ship.Entity.ToString()).And
            .Contain(ship.ToString());
        ((ArgumentException)refused!).ParamName.Should().Be("motion");
        NoChangeOfMotionIsPending(test, ship, BodyMotion.Dynamic);
    }
}
