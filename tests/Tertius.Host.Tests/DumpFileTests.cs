using System.Text.Json;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The dump files that a scripted run writes to its output folder: their JSON form, which follows the content formats
/// conventions, and their stability from run to run.
/// </summary>
public sealed class DumpFileTests : IDisposable
{
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static Script Parse(params string[] steps) =>
        Script.Parse($$"""{ "scene": "test", "steps": [ {{string.Join(", ", steps)}} ] }""");

    private JsonElement RunAndRead(Func<World> newWorld, Func<World, SceneInstance> build, Script script, string dump)
    {
        using World world = newWorld();
        ScriptRunner.Run(script: script, world: world, scene: build(world), outputFolder: folder);
        return JsonElement.Parse(File.ReadAllText(Path.Combine(folder, dump + ".json")));
    }

    private static JsonElement EntityNamed(JsonElement dump, string id) =>
        dump.GetProperty("entities").EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

    private static double[] Numbers(JsonElement array) => [.. array.EnumerateArray().Select(n => n.GetDouble())];

    [Fact]
    public void EachDumpIsWrittenToTheOutputFolderUnderItsName()
    {
        using World world = NewWorld();

        ScriptRunner.Run(
            script: Parse(
                """{ "do": "dump", "name": "start" }""",
                """{ "do": "run", "ticks": 1 }""",
                """{ "do": "dump", "name": "end" }"""
            ),
            world: world,
            scene: Probes(world),
            outputFolder: folder
        );

        Directory.GetFiles(folder).Select(Path.GetFileName).Should().BeEquivalentTo("start.json", "end.json");
    }

    [Fact]
    public void ADumpFileWritesPosesAsPositionAndQuaternionArraysWithTheirFrames()
    {
        JsonElement dump = RunAndRead(
            newWorld: NewWorld,
            build: ShipWithCrate,
            script: Parse("""{ "do": "dump", "name": "now" }"""),
            dump: "now"
        );

        JsonElement crate = EntityNamed(dump, "crate");
        crate.GetProperty("pose").GetProperty("frame").GetString().Should().Be("ship");
        Numbers(crate.GetProperty("pose").GetProperty("position")).Should().Equal(1, 0, 0);
        Numbers(crate.GetProperty("pose").GetProperty("rotation")).Should().Equal(0, 0, 0, 1);
        // In space the crate is at (10, 0, -1), turned 90 degrees about +Y with the ship.
        crate.GetProperty("rootPose").GetProperty("frame").GetString().Should().Be("space");
        Numbers(crate.GetProperty("rootPose").GetProperty("position"))
            .Should()
            .Equal([10, 0, -1], (a, b) => Math.Abs(a - b) < 1e-12);
        Numbers(crate.GetProperty("rootPose").GetProperty("rotation"))
            .Should()
            .Equal([0, Math.Sqrt(0.5), 0, Math.Sqrt(0.5)], (a, b) => Math.Abs(a - b) < 1e-12);
        EntityNamed(dump, "space").GetProperty("pose").GetProperty("frame").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ADumpFileWritesVelocitiesAsArrays()
    {
        // The crate takes its velocities from its first motion intent, at the first sync point.
        JsonElement dump = RunAndRead(
            newWorld: NewWorld,
            build: ShipWithCrate,
            script: Parse("""{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "now" }"""),
            dump: "now"
        );

        Numbers(EntityNamed(dump, "crate").GetProperty("velocity")).Should().Equal(1, 2, 3);
        Numbers(EntityNamed(dump, "crate").GetProperty("angularVelocity")).Should().Equal(0.25, 0.5, 0.75);
    }

    [Fact]
    public void ADumpFileWritesTheTickTheTimeAndTheFrameTree()
    {
        JsonElement dump = RunAndRead(
            newWorld: NewWorld,
            build: ShipWithCrate,
            script: Parse("""{ "do": "run", "ticks": 30 }""", """{ "do": "dump", "name": "now" }"""),
            dump: "now"
        );

        dump.GetProperty("name").GetString().Should().Be("now");
        dump.GetProperty("tick").GetInt64().Should().Be(30);
        // The tick length is the float 1/60 s, so 30 ticks last 0.5 s to about 3e-8 s.
        dump.GetProperty("time").GetDouble().Should().BeApproximately(0.5, 1e-6);
        JsonElement ship = dump.GetProperty("frames")[1];
        ship.GetProperty("id").GetString().Should().Be("ship");
        ship.GetProperty("parent").GetString().Should().Be("space");
        ship.GetProperty("follows").GetString().Should().Be("static");
        JsonElement moon = dump.GetProperty("frames")[3];
        moon.GetProperty("id").GetString().Should().Be("moon");
        moon.GetProperty("follows").GetString().Should().Be("rails");
    }

    [Fact]
    public void ADumpFileWritesAnEventWithItsTargetAndItsPayloadInScriptForm()
    {
        JsonElement dump = RunAndRead(
            newWorld: NewWorld,
            build: Probes,
            script: Parse(
                """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Tag", "payload": { "other": "crew" } }""",
                """{ "do": "run", "ticks": 1 }""",
                """{ "do": "dump", "name": "now" }"""
            ),
            dump: "now"
        );

        JsonElement e = dump.GetProperty("events").EnumerateArray().Single();
        e.GetProperty("tick").GetInt64().Should().Be(1);
        e.GetProperty("event").GetString().Should().Be("Tag");
        e.GetProperty("to").GetProperty("kind").GetString().Should().Be("entity");
        e.GetProperty("to").GetProperty("id").GetString().Should().Be("probe");
        e.GetProperty("from").ValueKind.Should().Be(JsonValueKind.Null);
        e.GetProperty("payload").GetProperty("other").GetString().Should().Be("crew");
    }

    [Fact]
    public void ADumpFileWritesAFrameTargetAsTheIdOfTheFramesOwner()
    {
        JsonElement dump = RunAndRead(
            newWorld: NewWorld,
            build: Probes,
            script: Parse(
                """{ "do": "post", "to": { "kind": "frame", "id": "ship" }, "event": "Nudge" }""",
                """{ "do": "run", "ticks": 1 }""",
                """{ "do": "dump", "name": "now" }"""
            ),
            dump: "now"
        );

        JsonElement to = dump.GetProperty("events")[0].GetProperty("to");
        to.GetProperty("kind").GetString().Should().Be("frame");
        to.GetProperty("id").GetString().Should().Be("ship");
    }

    [Fact]
    public void ADumpFileWritesTheCollisionsOfAFallingBoxWithSceneIds()
    {
        JsonElement dump = RunAndRead(
            newWorld: NewJoltWorld,
            build: BoxOnFloor,
            script: Parse("""{ "do": "run", "ticks": 60 }""", """{ "do": "dump", "name": "now" }"""),
            dump: "now"
        );

        JsonElement hit = dump.GetProperty("events")
            .EnumerateArray()
            .First(e =>
                e.GetProperty("event").GetString() == "Collision"
                && e.GetProperty("to").GetProperty("id").GetString() == "box"
            );
        hit.GetProperty("payload").GetProperty("other").GetString().Should().Be("floor");
        Numbers(hit.GetProperty("payload").GetProperty("normal")).Should().HaveCount(3);
    }

    [Fact]
    public void ADumpFileWritesStandingForACharacterOnlyAndLeavesItOutForEveryOtherEntity()
    {
        JsonElement dump = RunAndRead(
            newWorld: NewJoltWorld,
            build: StateDumpTests.PeopleOnFloor,
            script: Parse("""{ "do": "run", "ticks": 60 }""", """{ "do": "dump", "name": "now" }"""),
            dump: "now"
        );

        EntityNamed(dump, "person").GetProperty("standing").GetBoolean().Should().BeTrue();
        EntityNamed(dump, "faller").GetProperty("standing").GetBoolean().Should().BeFalse();
        EntityNamed(dump, "box").TryGetProperty("standing", out _).Should().BeFalse("the box is not a character");
        EntityNamed(dump, "floor").TryGetProperty("standing", out _).Should().BeFalse();
    }

    [Fact]
    public void TheSameScriptWritesByteIdenticalDumpsInTwoFreshWorlds()
    {
        Script script = Parse(
            """{ "do": "run", "ticks": 45 }""",
            """{ "do": "dump", "name": "landing" }""",
            """{ "do": "run", "ticks": 45 }""",
            """{ "do": "dump", "name": "rest" }"""
        );
        string second = Path.Combine(folder, "second");

        using (World world = NewJoltWorld())
        {
            ScriptRunner.Run(script: script, world: world, scene: BoxOnFloor(world), outputFolder: folder);
        }

        using (World world = NewJoltWorld())
        {
            ScriptRunner.Run(script: script, world: world, scene: BoxOnFloor(world), outputFolder: second);
        }

        foreach (string name in new[] { "landing.json", "rest.json" })
            File.ReadAllBytes(Path.Combine(second, name))
                .Should()
                .Equal(File.ReadAllBytes(Path.Combine(folder, name)), name);
    }

    [Fact]
    public void ANumberThatJsonCannotHoldIsWrittenAsAStringSoTheDumpStillWrites()
    {
        JsonElement dump = RunAndRead(
            newWorld: NewWorld,
            build: world =>
            {
                SceneInstance scene = Probes(world);
                Entity probe = scene["probe"];
                world.Spawn(
                    new()
                    {
                        Frame = world.GetOwnedFrame(scene["space"]),
                        Components =
                        [
                            SendOnce.Of(probe, new Nudge { Amount = float.NaN }),
                            SendOnce.Of(probe, new Aim { Direction = new(float.PositiveInfinity, 0, 0) }),
                        ],
                    }
                );
                return scene;
            },
            script: Parse("""{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "now" }"""),
            dump: "now"
        );

        JsonElement[] payloads = [.. dump.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("payload"))];
        payloads[0].GetProperty("amount").GetString().Should().Be("NaN");
        payloads[1].GetProperty("direction")[0].GetString().Should().Be("Infinity");
    }

    [Fact]
    public void NegativeZeroIsWrittenAsZeroSoThatDumpsCompareAsText()
    {
        JsonElement dump = RunAndRead(
            newWorld: NewWorld,
            build: world =>
            {
                SceneInstance scene = Probes(world);
                Entity probe = scene["probe"];
                world.Spawn(
                    new()
                    {
                        Frame = world.GetOwnedFrame(scene["space"]),
                        Components = [SendOnce.Of(probe, new Aim { Direction = new(-0f, 1, -0f) })],
                    }
                );
                return scene;
            },
            script: Parse("""{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "now" }"""),
            dump: "now"
        );

        dump.GetProperty("events")[0]
            .GetProperty("payload")
            .GetProperty("direction")
            .GetRawText()
            .Should()
            .Be("[0, 1, 0]");
    }

    [Fact]
    public void ADumpFileWritesAnInputEventInTheFormThatAScriptReads()
    {
        JsonElement dump = RunAndRead(
            newWorld: NewWorld,
            build: Probes,
            script: Parse(
                """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "InputEvent", "payload": { "kind": "keyDown", "code": 87 } }""",
                """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "InputEvent", "payload": { "kind": "mouseMove", "delta": [1.5, -2] } }""",
                """{ "do": "run", "ticks": 1 }""",
                """{ "do": "dump", "name": "now" }"""
            ),
            dump: "now"
        );

        JsonElement[] payloads = [.. dump.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("payload"))];
        payloads[0].EnumerateObject().Select(member => member.Name).Should().Equal("kind", "code");
        payloads[0].GetProperty("kind").GetString().Should().Be("keyDown");
        payloads[0].GetProperty("code").GetInt32().Should().Be(87);
        payloads[1].EnumerateObject().Select(member => member.Name).Should().Equal("kind", "delta");
        payloads[1].GetProperty("kind").GetString().Should().Be("mouseMove");
        payloads[1].GetProperty("delta").GetRawText().Should().Be("[1.5, -2]");
    }

    [Fact]
    public void ADumpOfAnInputEventThatHoldsNoCaseOrANaNStillWrites()
    {
        JsonElement dump = RunAndRead(
            newWorld: NewWorld,
            build: world =>
            {
                SceneInstance scene = Probes(world);
                Entity probe = scene["probe"];
                world.Spawn(
                    new()
                    {
                        Frame = world.GetOwnedFrame(scene["space"]),
                        Components =
                        [
                            SendOnce.Of(probe, default(InputEvent)),
                            SendOnce.Of(probe, InputEvent.Scroll(new(float.NaN, 1))),
                        ],
                    }
                );
                return scene;
            },
            script: Parse("""{ "do": "run", "ticks": 1 }""", """{ "do": "dump", "name": "now" }"""),
            dump: "now"
        );

        JsonElement[] payloads = [.. dump.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("payload"))];
        payloads[0].ValueKind.Should().Be(JsonValueKind.Null);
        payloads[1].GetProperty("delta")[0].GetString().Should().Be("NaN");
    }
}
