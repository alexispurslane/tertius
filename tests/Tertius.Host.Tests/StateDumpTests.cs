using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The state dumps of a scripted run, in memory: poses in two frames, the frame tree, the events since the previous dump
/// and the world's errors. See docs/plans/host-and-scripted-runs.md, "State dumps".
/// </summary>
public class StateDumpTests
{
    private static RunResult Run(World world, SceneInstance scene, params string[] steps) =>
        ScriptRunner.Run(Script.Parse($$"""{ "scene": "test", "steps": [ {{string.Join(", ", steps)}} ] }"""), world, scene);

    private const string DumpNow = """{ "do": "dump", "name": "now" }""";

    [Fact]
    public void ADumpHoldsAnEntitysPoseInItsOwnFrame()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);

        StateDump dump = Run(world, scene, DumpNow).Dump("now");

        FramePose pose = dump.Entity("crate").Pose;
        pose.Frame.Should().Be("ship");
        pose.Position.Should().Be(new Double3(1, 0, 0));
        pose.Rotation.Should().Be(DQuaternion.Identity);
    }

    [Fact]
    public void ADumpShowsOnlySemanticFramesWhileABodyIsInANumericFrame()
    {
        using World world = NewJoltWorld();
        SceneInstance scene = BoxOnFloor(world);
        world.Frames.TryGetFrame(scene["ground"], out FrameHandle ground).Should().BeTrue();
        // Every slow group gets a resting frame, however near the origin: the box and the floor under it.
        world.Frames.NumericOptions = EverySlowGroupRests;
        // The numeric update runs at the start of the first tick, in which the box falls by 3 mm.
        world.Tick();

        StateDump dump = Run(world, scene, DumpNow).Dump("now");

        world.Frames.IsNumeric(world.PhysicsHomeOf(scene["box"])).Should().BeTrue();
        dump.Frames.Select(record => record.Id).Should().Equal("ground");
        dump.Entity("box").Pose.Frame.Should().Be("ground");
        (dump.Entity("box").Pose.Position - new Double3(0, 1.5, 0)).Length.Should().BeLessThan(0.01);
        dump.Entity("box").RootPose.Frame.Should().Be("ground");
    }

    [Fact]
    public void ADumpHoldsAnEntitysPoseInTheRootFrameOfItsTree()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);

        StateDump dump = Run(world, scene, DumpNow).Dump("now");

        // The ship stands at (10, 0, 0), turned 90 degrees about +Y, which takes +X to -Z. So the crate's (1, 0, 0) in
        // the ship is (10, 0, -1) in space, and it is turned with the ship.
        FramePose root = dump.Entity("crate").RootPose;
        root.Frame.Should().Be("space");
        root.Position.X.Should().BeApproximately(10, 1e-12);
        root.Position.Y.Should().BeApproximately(0, 1e-12);
        root.Position.Z.Should().BeApproximately(-1, 1e-12);
        double half = Math.Sqrt(0.5);
        root.Rotation.Y.Should().BeApproximately(half, 1e-12);
        root.Rotation.W.Should().BeApproximately(half, 1e-12);
    }

    [Fact]
    public void TheOwnerOfARootFrameIsInNoFrameAndAtTheOriginOfItsOwnFrame()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);

        StateDump dump = Run(world, scene, DumpNow).Dump("now");

        EntityRecord space = dump.Entity("space");
        space.Pose.Frame.Should().BeNull();
        space.RootPose.Should().Be(new FramePose("space", Double3.Zero, DQuaternion.Identity));
    }

    [Fact]
    public void ADumpListsEveryLiveEntityInSlotOrderAndNamesOneWithoutASceneIdByItsHandle()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);
        string[] named = ["space", "ship", "crate", "shuttle", "pod", "moon"];
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        Entity unnamed = live.Single(e => !named.Any(id => scene[id] == e));

        StateDump dump = Run(world, scene, DumpNow).Dump("now");

        // The scene makes them in this order, so their slots are in this order too.
        dump.Entities.Select(e => e.Id).Should().Equal([.. named, $"#{unnamed.Index}.{unnamed.Generation}"]);
    }

    /// <summary>
    /// The moon's <see cref="KeplerMotion"/> writes a sample in every tick, and the moon follows each sample at the sync point
    /// after the step that applies it. So a dump after two ticks shows that the moon moves on rails. No sample and no dynamic
    /// hull moves the other frames.
    /// </summary>
    [Fact]
    public void ADumpListsTheFramesWithTheirParentsAndWhatTheyFollowParentsFirst()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);

        StateDump dump = Run(world, scene, """{ "do": "run", "ticks": 2 }""", DumpNow).Dump("now");

        dump.Frames.Should().Equal(
            new FrameRecord("space", null, "static"),
            new FrameRecord("ship", "space", "static"),
            new FrameRecord("shuttle", "ship", "static"),
            new FrameRecord("moon", "space", "rails"));
    }

    /// <summary>
    /// A new frame goes live before its rails component first runs, so the step of its first tick has no sample, and the
    /// frame holds still at its spawn pose at that tick's sync point (docs/plans/rails-components.md, "Other rules"). The
    /// sample that the component writes in the first tick moves the frame at the second sync point.
    /// </summary>
    [Fact]
    public void ANewFrameOnRailsFollowsRailsFromTheSecondSyncPointOnBecauseItHoldsStillAtTheFirst()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);

        RunResult run = Run(world, scene, """{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "first" }""",
            """{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "second" }""");

        FollowsOf(run.Dump("first"), "moon").Should().Be("static");
        FollowsOf(run.Dump("second"), "moon").Should().Be("rails");
    }

    /// <summary>
    /// The moon's <see cref="KeplerMotion"/> runs first in its entity, so it writes its last sample in the tick in which a
    /// later component removes it. The moon follows that sample at the sync point of the next tick, and then nothing moves it.
    /// </summary>
    [Fact]
    public void AFrameFollowsTheLastSampleOfARailsComponentThatWentAtTheSyncPointAfterTheStepThatAppliesIt()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);
        WorldTesting.Tick(world, 2);
        world.AddComponent(scene["moon"], new RemovesOnce(world.GetComponent<KeplerMotion>(scene["moon"])!));

        RunResult run = Run(world, scene, """{ "do": "run", "ticks": 2 }""", """{ "do": "dump", "name": "last-sample" }""",
            """{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "after" }""");

        FollowsOf(run.Dump("last-sample"), "moon").Should().Be("rails");
        FollowsOf(run.Dump("after"), "moon").Should().Be("static");
    }

    /// <summary>
    /// The new motion of a hull acts from the step after the tick in which its owner writes it. So physics moves a dynamic
    /// hull that parks in a tick in the step of that tick, and the frame follows that step at the tick's sync point.
    /// </summary>
    [Fact]
    public void AFrameWhoseHullParksInATickFollowsPhysicsAtThatTicksSyncPoint()
    {
        using World world = NewJoltWorld();
        (SceneInstance scene, Entity owner) = ShipInSpace(world, BodyMotion.Dynamic);
        world.SetMotionNextTick(owner, BodyMotion.Static);

        RunResult run = Run(world, scene, """{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "parked" }""",
            """{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "after" }""");

        FollowsOf(run.Dump("parked"), "ship").Should().Be("physics");
        FollowsOf(run.Dump("after"), "ship").Should().Be("static");
    }

    /// <summary>
    /// A Kepler hull that becomes dynamic in a tick is still kinematic in the step of that tick, which applies the sample of
    /// the tick before. So the frame follows that sample at the tick's sync point, and physics moves it from the next step on.
    /// </summary>
    [Fact]
    public void AFrameWhoseKeplerHullBecomesDynamicInATickFollowsRailsAtThatTicksSyncPoint()
    {
        using World world = NewJoltWorld();
        (SceneInstance scene, Entity owner) = ShipInSpace(world, BodyMotion.Kinematic);
        world.SetMotionNextTick(owner, BodyMotion.Dynamic, remove: world.GetComponent<KeplerMotion>(owner));

        RunResult run = Run(world, scene, """{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "changed" }""",
            """{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "after" }""");

        FollowsOf(run.Dump("changed"), "ship").Should().Be("rails");
        FollowsOf(run.Dump("after"), "ship").Should().Be("physics");
    }

    /// <summary>
    /// The frame tree stores how each frame followed its owner beside the record of the step that ended (docs/plans/
    /// rails-components.md, "The follow kind"): a full sample is rails, an end pose kinematic, a dynamic hull physics, and a
    /// frame that held still static, and so is a frame whose owner wrote its transform in the same tick as a motion intent,
    /// because the write wins for one tick. Until every writer takes the rules of the guard (stage 6 of "Order of the work"),
    /// the owner of a root frame with a body is the one owner of a frame that takes an end pose.
    /// </summary>
    [Fact]
    public void AStateDumpTellsHowEachFrameFollowedItsOwner()
    {
        using World world = NewJoltWorld();
        var scene = new SceneInstance();
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        scene.Add("space", space.Entity);
        static DPose At(long tick) => new(new Double3(tick, 0, 0), DQuaternion.Identity);
        static MotionSample Still(long tick) => new(At(tick), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero);
        scene.Add("yard", world.Spawn(new SpawnDesc
        {
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static),
            Components = [new EachTick(self => self.Body.MoveKinematic(At(self.Tick)))],
        }));
        scene.Add("moon", world.Spawn(new SpawnDesc
        {
            Frame = space,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Components = [new EachTick(self => self.MoveOnRails(Still(self.Tick)))],
        }));
        scene.Add("room", world.Spawn(new SpawnDesc
        {
            Frame = space,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Components = [new EachTick(self =>
            {
                self.MoveOnRails(Still(self.Tick));
                self.SetTransform(At(-self.Tick));
            })],
        }));
        scene.Add("ship", world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(new Double3(500, 0, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Dynamic, Mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        }));

        StateDump dump = Run(world, scene, """{ "do": "run", "ticks": 2 }""", DumpNow).Dump("now");

        FollowsOf(dump, "yard").Should().Be("kinematic");
        FollowsOf(dump, "moon").Should().Be("rails");
        FollowsOf(dump, "room").Should().Be("static", "the write of the transform wins over the motion intent for one tick");
        FollowsOf(dump, "ship").Should().Be("physics");
        FollowsOf(dump, "space").Should().Be("static");
        dump.Errors.Should().BeEmpty();
    }

    /// <summary>Runs <paramref name="act"/> on its entity in every tick.</summary>
    private sealed class EachTick(Action<Self> act) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => act(self);
    }

    private static string FollowsOf(StateDump dump, string id) => dump.Frames.Single(frame => frame.Id == id).Follows;

    /// <summary>
    /// The root frame <c>space</c>, with a point gravity of G·M 5e5 m³/s², and the frame <c>ship</c> on a circle of 5 km round
    /// it, at 10 m/s, after five ticks. That is slow and close to the origin, so the engine puts no numeric frame round the
    /// ship. The ship's hull is a box of 1000 kg with <paramref name="motion"/>: a dynamic hull starts with the circle's
    /// velocity and falls round it, and a kinematic hull moves on the circle with a <see cref="KeplerMotion"/>.
    /// </summary>
    private static (SceneInstance Scene, Entity Owner) ShipInSpace(World world, BodyMotion motion)
    {
        const double mu = 5e5;
        var scene = new SceneInstance();
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), GravityField.Point(mu)));
        scene.Add("space", space);
        var orbit = KeplerOrbit.Circular(5000, phase: 1.0);
        (Double3 position, Double3 velocity) = orbit.StateAt(world.Time, mu);
        var desc = new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(position, DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), motion, Mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        };
        Entity owner = world.Spawn(motion == BodyMotion.Kinematic ? desc.OnKeplerOrbit(world, orbit) : desc);
        scene.Add("ship", owner);
        WorldTesting.Tick(world, 5);
        return (scene, owner);
    }

    /// <summary>Removes itself and <paramref name="other"/> from its entity in the first tick in which it runs.</summary>
    private sealed class RemovesOnce(IComponent other) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            self.RemoveComponent(this);
            self.RemoveComponent(other);
        }
    }

    [Fact]
    public void ADumpHoldsAnEntitysVelocityAndAngularVelocityInItsOwnFrame()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);

        EntityRecord crate = Run(world, scene, DumpNow).Dump("now").Entity("crate");

        crate.Velocity.Should().Be(new Double3(1, 2, 3));
        crate.AngularVelocity.Should().Be(new Vector3(0.25f, 0.5f, 0.75f));
    }

    [Fact]
    public void TheRootPoseWalksUpEveryFrameToTheRootOfTheTree()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);

        StateDump dump = Run(world, scene, DumpNow).Dump("now");

        // The pod's (1, 0, 0) in the shuttle is (1, 0, 2) in the ship, and the ship's turn takes (x, 0, z) to (z, 0, -x),
        // so the pod is at (10 + 2, 0, -1) in space.
        FramePose root = dump.Entity("pod").RootPose;
        root.Frame.Should().Be("space");
        root.Position.X.Should().BeApproximately(12, 1e-12);
        root.Position.Y.Should().BeApproximately(0, 1e-12);
        root.Position.Z.Should().BeApproximately(-1, 1e-12);
    }

    [Fact]
    public void ADumpListsEveryEventRoutedSinceThePreviousDump()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        RunResult result = Run(world, scene,
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge", "payload": { "amount": 1 } }""",
            """{ "do": "run", "ticks": 3 }""",
            """{ "do": "dump", "name": "first" }""",
            """{ "do": "post", "to": { "kind": "entity", "id": "crew" }, "event": "Nudge", "payload": { "amount": 2 } }""",
            """{ "do": "run", "ticks": 1 }""",
            """{ "do": "dump", "name": "second" }""");

        result.Dump("first").Events.Select(e => ((Nudge)e.Payload).Amount).Should().Equal(1);
        result.Dump("second").Events.Select(e => ((Nudge)e.Payload).Amount).Should().Equal(2);
    }

    [Fact]
    public void ARecordedEventCarriesTheTickCountAfterTheSyncPointThatRoutedIt()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        StateDump dump = Run(world, scene,
            """{ "do": "run", "ticks": 2 }""",
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge" }""",
            """{ "do": "run", "ticks": 4 }""",
            DumpNow).Dump("now");

        // Posted after 2 ticks, so the sync point of the third tick routes it.
        dump.Events.Should().ContainSingle().Which.Tick.Should().Be(3);
    }

    [Fact]
    public void ARecordedEventNamesItsTypeItsTargetAndItsSender()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        StateDump dump = Run(world, scene,
            """{ "do": "post", "to": { "kind": "topic", "name": "test" }, "event": "Nudge" }""",
            """{ "do": "run", "ticks": 1 }""",
            DumpNow).Dump("now");

        EventRecord record = dump.Events.Should().ContainSingle().Which;
        record.Event.Should().Be("Nudge");
        record.To.Should().Be(new ToTopic("test"));
        record.From.Should().BeNull("a posted event comes from outside the simulation");
    }

    [Fact]
    public void ADumpHoldsTheErrorsThatTheWorldHadRecordedWhenItWasTaken()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        world.Spawn(new SpawnDesc { Frame = world.GetOwnedFrame(scene["space"]), Components = [new ThrowsOnce()] });

        RunResult result = Run(world, scene, """{ "do": "dump", "name": "before" }""", """{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "after" }""");

        result.Dump("before").Errors.Should().BeEmpty();
        result.Dump("after").Errors.Should().ContainSingle().Which.Should().Contain("the probe broke on purpose");
    }

    [Theory]
    [InlineData("""{ "kind": "frame", "id": "ship" }""", "frame")]
    [InlineData("""{ "kind": "broadcast" }""", "broadcast")]
    [InlineData("""{ "kind": "entity", "id": "crew" }""", "entity")]
    public void ARecordedEventNamesItsTargetInTheFormThatAScriptWrites(string target, string kind)
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        StateDump dump = Run(world, scene, $$"""{ "do": "post", "to": {{target}}, "event": "Nudge" }""", """{ "do": "run", "ticks": 1 }""", DumpNow).Dump("now");

        MessageTarget expected = kind switch
        {
            "frame" => new ToFrame("ship"),
            "broadcast" => new ToEveryone(),
            _ => new ToEntity("crew"),
        };
        dump.Events.Should().ContainSingle().Which.To.Should().Be(expected);
    }

    [Fact]
    public void ARecordedMessageNamesTheEntityThatSentIt()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        scene.Add("sender", world.Spawn(new SpawnDesc
        {
            Frame = world.GetOwnedFrame(scene["space"]),
            Components = [SendOnce.Of(scene["probe"], new Nudge { Amount = 4 })],
        }));

        StateDump dump = Run(world, scene, """{ "do": "run", "ticks": 1 }""", DumpNow).Dump("now");

        EventRecord record = dump.Events.Should().ContainSingle().Which;
        record.From.Should().Be("sender");
        record.To.Should().Be(new ToEntity("probe"));
    }

    [Fact]
    public void ADumpOfAFallingBoxShowsItRestingOnTheFloorAndTheCollisionThatStoppedIt()
    {
        using World world = NewJoltWorld();
        SceneInstance scene = BoxOnFloor(world);

        StateDump dump = Run(world, scene, """{ "do": "run", "ticks": 90 }""", DumpNow).Dump("now");

        dump.Entity("box").Pose.Position.Y.Should().BeApproximately(0.5, 0.02, "the 1 m box rests on the floor whose top is at y = 0");
        dump.Entity("box").Velocity.Length.Should().BeLessThan(0.05);
        EventRecord hit = dump.Events.First(e => e.Event == "Collision" && e.To == new ToEntity("box"));
        ((Collision)hit.Payload).Other.Should().Be(scene["floor"]);
        dump.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The scene "box-on-floor" with two characters: <c>person</c> stands on the floor 3 m from the box, and
    /// <c>faller</c> starts 30 m above the floor, 3 m on the other side, so it is still in the air after a second.
    /// </summary>
    internal static SceneInstance PeopleOnFloor(World world)
    {
        SceneInstance scene = BoxOnFloor(world);
        FrameHandle ground = world.GetOwnedFrame(scene["ground"]);
        var person = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);
        scene.Add("person", world.Spawn(new SpawnDesc { Frame = ground, Pose = At(3, 0.92), Body = person }));
        scene.Add("faller", world.Spawn(new SpawnDesc { Frame = ground, Pose = At(-3, 30), Body = person }));
        return scene;
    }

    [Fact]
    public void ADumpHoldsWhetherPhysicsStoodEachCharacterAndNothingOfThatForOtherEntities()
    {
        using World world = NewJoltWorld();
        SceneInstance scene = PeopleOnFloor(world);

        StateDump dump = Run(world, scene, """{ "do": "run", "ticks": 60 }""", DumpNow).Dump("now");

        dump.Entity("person").Standing.Should().BeTrue();
        dump.Entity("faller").Standing.Should().BeFalse("it is still in the air");
        dump.Entity("box").Standing.Should().BeNull("the box is not a character");
        dump.Entity("floor").Standing.Should().BeNull();
        dump.Entity("ground").Standing.Should().BeNull();
    }
}
