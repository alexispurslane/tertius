using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;
using Tertius.Testing;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The owner of a frame changes its hull's motion in a tick with <see cref="BodyHandle.SetMotion"/>, for example to park a
/// ship (docs/plans/rails-components.md, "Changes of motion"). The hull motion that the frame tree keeps changes at the sync
/// point of the tick that writes the change, and physics gives the Jolt body the new motion at the start of the next step.
/// <c>SetMotion</c> refuses, with an always-on guard, a body that is no hull and a motion that the hull cannot take.
/// </summary>
public class SetMotionTests
{
    /// <summary>A triangle mesh. Jolt never moves a mesh, so a hull of this shape can only be static.</summary>
    private static readonly MeshShapeDesc Ramp = new([new(-5, 0, -5), new(5, 0, -5), new(5, 2, 5), new(-5, 2, 5)], [0, 2, 1, 0, 3, 2]);

    /// <summary>A box of 1000 kg with sides of 4 m, which can take every motion of a hull.</summary>
    private static readonly BodyDesc ShipBody = new(new BoxShapeDesc(new Vector3(2)), Mass: 1000);

    /// <summary>A frame 100 m along +X in the root, whose owner has the hull <paramref name="body"/> with <paramref name="motion"/>.</summary>
    private static FrameHandle SpawnHull(TestWorld test, BodyMotion motion, BodyDesc body, params IComponent[] components) =>
        test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(100),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Body = body with { Motion = motion },
            Components = components,
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

    private static BodyMotion FactsMotion(TestWorld test, Entity entity)
    {
        test.World.Snapshot.TryGetBody(entity, out BodyFacts facts).Should().BeTrue();
        return facts.Motion;
    }

    // ---- When a change acts ---------------------------------------------------------------------------------------

    [Fact]
    public void AChangeOfMotionShowsAtTheSyncPointOfTheTickThatWritesItNotBefore()
    {
        using var test = TestWorld.OnJolt(out _);
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

        seen.Should().Equal([(BodyMotion.Dynamic, BodyMotion.Dynamic)], "the tick that writes the change still sees the motion of its step");
        test.World.Frames.HullMotion(ship).Should().Be(BodyMotion.Static, "the sync point of that tick changed the hull motion");
        FactsMotion(test, ship.Entity).Should().Be(BodyMotion.Static);

        test.Run(1);

        seen[^1].Should().Be((BodyMotion.Static, BodyMotion.Static), "the next tick runs beside the step that has the new motion");
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheLastChangeOfMotionInATickWins()
    {
        using var test = TestWorld.OnJolt(out _);
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

    [Fact]
    public void AChangeOfMotionWorksWithoutAPhysicsBackend()
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnHull(test, BodyMotion.Dynamic, ShipBody);
        test.Run(1);

        test.World.SetMotionNextTick(ship.Entity, BodyMotion.Static);
        test.Run(1);

        test.World.Frames.HullMotion(ship).Should().Be(BodyMotion.Static);
        FactsMotion(test, ship.Entity).Should().Be(BodyMotion.Static);
        test.World.Errors.Should().BeEmpty();
    }

    // ---- What a change drops --------------------------------------------------------------------------------------

    /// <summary>A component that writes the same sample in every tick: the frame stands still 100 m along +X.</summary>
    private sealed class HoldsOnRails : IComponent
    {
        public static readonly MotionSample Sample =
            new(TestWorld.At(100), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Vector3.Zero);

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => self.MoveOnRails(Sample);
    }

    /// <summary>
    /// The rails component runs before the change in the same tick, so it writes a sample that no step may apply: a dynamic
    /// hull follows no sample. The sample that the last step applied stays, as the record that the frame follows.
    /// </summary>
    [Fact]
    public void AHullThatStopsBeingKinematicDropsTheSampleWrittenInTheSameTick()
    {
        using var test = new TestWorld();
        var rails = new HoldsOnRails();
        FrameHandle station = SpawnHull(test, BodyMotion.Kinematic, ShipBody, rails);
        test.Run(2);
        test.World.Frames.Samples.Applied.TryGet(station, out _).Should().BeTrue("the rails component writes a sample in every tick");

        test.World.SetMotionNextTick(station.Entity, BodyMotion.Dynamic, remove: rails);
        test.Run(1);

        test.World.Frames.HullMotion(station).Should().Be(BodyMotion.Dynamic);
        test.World.Frames.TryGetAppliedSample(station, out _).Should().BeFalse("the next step applies no sample to a dynamic hull");
        test.World.Frames.Samples.Followed.TryGet(station, out _).Should().BeTrue("the sample that the step of the tick applied stays");
        test.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A dynamic hull may set its velocities and a kinematic target in a tick. When the same tick makes the hull kinematic,
    /// they go: a kinematic hull moves only by its samples and teleports, which tell its frame how to follow. A force of the
    /// same tick stays, because physics applies it only to a dynamic body.
    /// </summary>
    [Fact]
    public void AHullThatBecomesKinematicDropsTheVelocitySetInTheSameTick()
    {
        using var test = TestWorld.OnJolt(out _);
        FrameHandle ship = SpawnHull(test, BodyMotion.Dynamic, ShipBody);
        test.Run(1);
        bool done = false;
        test.World.AddComponent(ship.Entity, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (done)
                return;
            done = true;
            self.Body.SetVelocity(new Double3(5, 0, 0));
            self.Body.SetAngularVelocity(new Vector3(0, 1, 0));
            self.Body.MoveKinematic(TestWorld.At(3));
            self.Body.AddForce(new Vector3(10, 0, 0));
            self.Body.SetMotion(BodyMotion.Kinematic);
        }));

        test.Run(1);

        PhysicsIntent next = test.World.Entities.NextStepIntent(ship.Entity);
        next.Flags.Should().Be(IntentFlags.SetMotion, "the velocities and the target went, and the change of motion stays");
        next.Motion.Should().Be(BodyMotion.Kinematic);
        next.Force.Should().Be(new Vector3(10, 0, 0), "a force stays, because physics applies it only to a dynamic body");
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
            "a plain body" => test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = ShipBody }),
            "a root frame's owner" => test.World.Spawn(new SpawnDesc
            {
                AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
                Body = ShipBody,
            }),
            _ => test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
            {
                Frame = test.Root,
                AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            })).Entity,
        };
        test.Run(1);

        Exception? refused = SetMotionInATick(test, entity, BodyMotion.Static);

        refused.Should().BeOfType<InvalidOperationException>(which).Which.Message.Should().Contain(entity.ToString()).And.Contain("hull").And.Contain(reason);
    }

    [Fact]
    public void SetMotionRefusesADynamicMotionForAHullWithoutMass()
    {
        using var test = new TestWorld();
        FrameHandle dock = SpawnHull(test, BodyMotion.Static, ShipBody with { Mass = 0 });
        test.Run(1);

        Exception? refused = SetMotionInATick(test, dock.Entity, BodyMotion.Dynamic);

        refused.Should().BeOfType<ArgumentException>().Which.Message.Should().Contain(dock.Entity.ToString()).And.Contain(dock.ToString()).And.Contain("mass");
        ((ArgumentException)refused!).ParamName.Should().Be("motion");
        test.World.Frames.HullMotion(dock).Should().Be(BodyMotion.Static);
    }

    [Theory]
    [InlineData(BodyMotion.Kinematic, false)]
    [InlineData(BodyMotion.Kinematic, true)]
    [InlineData(BodyMotion.Dynamic, false)]
    [InlineData(BodyMotion.Dynamic, true)]
    public void SetMotionRefusesAMotionOtherThanStaticForAMeshHull(BodyMotion motion, bool inCompound)
    {
        using var test = new TestWorld();
        ShapeDesc shape = inCompound ? new CompoundShapeDesc([new CompoundChild(Ramp, Pose.Identity)]) : Ramp;
        FrameHandle dock = SpawnHull(test, BodyMotion.Static, new BodyDesc(shape, Mass: 1000));
        test.Run(1);

        Exception? refused = SetMotionInATick(test, dock.Entity, motion);

        refused.Should().BeOfType<ArgumentException>().Which.Message.Should().Contain(dock.Entity.ToString()).And.Contain(dock.ToString()).And.Contain("mesh");
        test.World.Frames.HullMotion(dock).Should().Be(BodyMotion.Static);
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

        refused.Should().BeOfType<ArgumentException>().Which.Message.Should().Contain(ship.Entity.ToString()).And.Contain(ship.ToString());
        ((ArgumentException)refused!).ParamName.Should().Be("motion");
        test.World.Frames.HullMotion(ship).Should().Be(BodyMotion.Dynamic);
    }
}
