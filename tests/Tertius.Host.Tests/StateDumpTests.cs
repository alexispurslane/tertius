using System.Numerics;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

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
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        Entity unnamed = live.Single(e => e != scene["space"] && e != scene["ship"] && e != scene["crate"]);

        StateDump dump = Run(world, scene, DumpNow).Dump("now");

        dump.Entities.Select(e => e.Id).Should().Equal("space", "ship", "crate", $"#{unnamed.Index}.{unnamed.Generation}");
    }

    [Fact]
    public void ADumpListsTheFramesWithTheirParentsAndDriversParentsFirst()
    {
        using World world = NewWorld();
        SceneInstance scene = ShipWithCrate(world);

        StateDump dump = Run(world, scene, DumpNow).Dump("now");

        dump.Frames.Should().Equal(new FrameRecord("space", null, MotionDriver.Static), new FrameRecord("ship", "space", MotionDriver.Static));
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
    public void ADumpHoldsTheErrorsThatTheWorldRecorded()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        world.Spawn(new SpawnDesc { Frame = world.GetOwnedFrame(scene["space"]), Components = [new ThrowsOnce()] });

        StateDump dump = Run(world, scene, """{ "do": "run", "ticks": 1 }""", DumpNow).Dump("now");

        dump.Errors.Should().ContainSingle().Which.Should().Contain("the probe broke on purpose");
    }

    [Fact]
    public void ADumpOfAFallingBoxShowsItRestingOnTheFloorAndTheCollisionThatStoppedIt()
    {
        using World world = NewJoltWorld();
        SceneInstance scene = BoxOnFloor(world);

        StateDump dump = Run(world, scene, """{ "do": "run", "ticks": 90 }""", DumpNow).Dump("now");

        dump.Entity("box").Pose.Position.Y.Should().BeApproximately(0.5, 0.02, "the 1 m box rests on the floor whose top is at y = 0");
        dump.Entity("box").Velocity.Length().Should().BeLessThan(0.05f);
        EventRecord hit = dump.Events.First(e => e.Event == "Collision" && e.To == new ToEntity("box"));
        ((Collision)hit.Payload).Other.Should().Be(scene["floor"]);
        dump.Errors.Should().BeEmpty();
    }
}
