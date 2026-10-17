using System.Numerics;
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

    [Fact]
    public void ADumpListsTheFramesWithTheirParentsAndDriversParentsFirst()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);

        StateDump dump = Run(world, scene, DumpNow).Dump("now");

        dump.Frames.Should().Equal(
            new FrameRecord("space", null, MotionDriver.Static),
            new FrameRecord("ship", "space", MotionDriver.Static),
            new FrameRecord("shuttle", "ship", MotionDriver.Static),
            new FrameRecord("moon", "space", MotionDriver.Kepler));
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
