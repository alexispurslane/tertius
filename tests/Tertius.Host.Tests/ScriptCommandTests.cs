using System.Text.Json;
using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// ScriptCommand: the command line that the Demo runs, <c>--script &lt;file&gt; --headless --out &lt;folder&gt;</c>, and its exit
/// codes.
/// </summary>
public sealed class ScriptCommandTests : IDisposable
{
    private static readonly Scene[] Scenes = [ProbesScene, BoxOnFloorScene, LoggerScene];
    private readonly string folder = NewFolder();
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    private string Out => Path.Combine(folder, "out");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string ScriptFile(string json)
    {
        string path = Path.Combine(folder, "script.json");
        File.WriteAllText(path, json);
        return path;
    }

    private int Run(params string[] args) =>
        ScriptCommand.Run(args: args, scenes: Scenes, output: output, error: error);

    [Fact]
    public void TheCommandRunsTheScriptOnTheSceneThatItNamesAndWritesTheDumps()
    {
        string script = ScriptFile(
            """{ "scene": "box-on-floor", "steps": [ { "do": "dump", "name": "start" }, { "do": "run", "ticks": 60 }, { "do": "dump", "name": "landed" } ] }"""
        );

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        File.ReadAllText(Path.Combine(Out, "landed.json")).Should().Contain("\"box\"");
        output
            .ToString()
            .Should()
            .Be(
                Path.Combine(Out, "start.json")
                    + Environment.NewLine
                    + Path.Combine(Out, "landed.json")
                    + Environment.NewLine
            );
    }

    [Fact]
    public void AScriptThatIsNotValidExitsWithTheScriptErrorCodeAndNamesTheFile()
    {
        string script = ScriptFile("""{ "scene": "probes", "steps": [] }""");

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain(script);
        Directory.Exists(Out).Should().BeFalse("nothing ran");
    }

    [Fact]
    public void AnOutputFolderThatCannotBeMadeExitsWithTheFileErrorCode()
    {
        string script = ScriptFile("""{ "scene": "probes", "steps": [ { "do": "dump", "name": "now" } ] }""");
        string taken = Path.Combine(folder, "taken");
        File.WriteAllText(taken, "a file, not a folder");

        int code = Run("--script", script, "--headless", "--out", taken);

        code.Should().Be(ScriptCommand.FileError);
        error
            .ToString()
            .Should()
            .StartWith(
                $"Cannot write into {taken}: ",
                "the window reports a dump that cannot be written in the same words"
            );
    }

    [Fact]
    public void AScriptThatNamesAnUnknownSceneExitsWithTheScriptErrorCode()
    {
        string script = ScriptFile("""{ "scene": "moon-base", "steps": [ { "do": "run", "ticks": 1 } ] }""");

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain("moon-base");
    }

    [Fact]
    public void AScriptThatCannotRunExitsWithTheScriptErrorCode()
    {
        string script = ScriptFile(
            """{ "scene": "probes", "steps": [ { "do": "post", "to": { "kind": "entity", "id": "ghost" }, "event": "Nudge" } ] }"""
        );

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain("ghost");
    }

    [Fact]
    public void AScriptFileThatDoesNotExistExitsWithTheFileErrorCode()
    {
        int code = Run("--script", Path.Combine(folder, "missing.json"), "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.FileError);
        error.ToString().Should().Contain("missing.json");
    }

    [Theory]
    [InlineData("--headless", "--out", "out")]
    [InlineData("--script", "script.json", "--headless")]
    [InlineData("--script", "script.json", "--headless", "--out")]
    [InlineData("--script", "script.json", "--headless", "--out", "out", "--fast")]
    [InlineData("--script", "", "--headless", "--out", "out")]
    [InlineData("--script", "script.json", "--headless", "--out", " ")]
    public void AMissingOrUnknownOptionExitsWithTheCommandLineErrorCode(params string[] args)
    {
        int code = Run(args);

        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().NotBeEmpty();
    }

    [Fact]
    public void TheCommandWritesTheWorldsLogToStandardError()
    {
        string script = ScriptFile("""{ "scene": "logger", "steps": [ { "do": "run", "ticks": 2 } ] }""");

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        error.ToString().Should().Contain("hello from the logger");
    }

    [Fact]
    public void AScriptWithoutCapturesRunsWithNoRendererAndTheDemoSceneStillMakesItsEntities()
    {
        string script = ScriptFile(
            """{ "scene": "drop-box", "steps": [ { "do": "run", "ticks": 120 }, { "do": "dump", "name": "landed" } ] }"""
        );

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out],
            scenes: DemoScenes.All,
            output: output,
            error: error,
            createRenderer: _ => throw new InvalidOperationException("a script without captures makes no renderer")
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        using var landed = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, "landed.json")));
        JsonElement box = landed
            .RootElement.GetProperty("entities")
            .EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == "box");
        box.GetProperty("pose").GetProperty("position")[1].GetDouble().Should().BeApproximately(0.5, 0.02);
    }

    [Theory]
    [InlineData("--script or --scene", "--out", "out")]
    [InlineData("not both", "--script", "script.json", "--scene", "no-such-scene", "--out", "out")]
    [InlineData("free play", "--scene", "no-such-scene", "--headless")]
    [InlineData("--out applies to a script", "--scene", "no-such-scene", "--out", "out")]
    [InlineData(
        "--speed applies to the window",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--speed",
        "2"
    )]
    [InlineData("positive", "--scene", "no-such-scene", "--speed", "0")]
    [InlineData("positive", "--scene", "no-such-scene", "--speed", "-1")]
    [InlineData("positive", "--scene", "no-such-scene", "--speed", "fast")]
    [InlineData("positive", "--scene", "no-such-scene", "--speed", "NaN")]
    [InlineData("positive", "--scene", "no-such-scene", "--speed", "Infinity")]
    [InlineData("--speed needs a value", "--scene", "no-such-scene", "--speed")]
    [InlineData("--scene needs a value", "--scene")]
    [InlineData("--record needs a value", "--scene", "no-such-scene", "--record")]
    [InlineData("--record applies to free play", "--script", "script.json", "--out", "out", "--record", "record.json")]
    [InlineData(
        "--record applies to free play",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--record",
        "record.json"
    )]
    [InlineData("--video applies to a headless run", "--script", "script.json", "--out", "out", "--video", "v.mkv")]
    [InlineData("--video applies to a headless run", "--scene", "no-such-scene", "--video", "v.mkv")]
    [InlineData("--video needs a value", "--script", "script.json", "--headless", "--out", "out", "--video")]
    [InlineData("ends in .mkv", "--script", "script.json", "--headless", "--out", "out", "--video", "v.mp4")]
    [InlineData("ends in .mkv", "--script", "script.json", "--headless", "--out", "out", "--video", "mkv")]
    [InlineData("--fps applies to a video", "--script", "script.json", "--headless", "--out", "out", "--fps", "30")]
    [InlineData(
        "--fps needs a value",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--video",
        "v.mkv",
        "--fps"
    )]
    [InlineData(
        "whole number from 1 to 240",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--video",
        "v.mkv",
        "--fps",
        "0"
    )]
    [InlineData(
        "whole number from 1 to 240",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--video",
        "v.mkv",
        "--fps",
        "241"
    )]
    [InlineData(
        "whole number from 1 to 240",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--video",
        "v.mkv",
        "--fps",
        "29.97"
    )]
    [InlineData(
        "whole number from 1 to 240",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--video",
        "v.mkv",
        "--fps",
        "fast"
    )]
    [InlineData("--size needs a value", "--script", "script.json", "--headless", "--out", "out", "--size")]
    [InlineData("--size applies to a script", "--scene", "no-such-scene", "--size", "1920x1080")]
    [InlineData("width and a height", "--script", "script.json", "--headless", "--out", "out", "--size", "big")]
    [InlineData("width and a height", "--script", "script.json", "--headless", "--out", "out", "--size", "1920x")]
    [InlineData("width and a height", "--script", "script.json", "--headless", "--out", "out", "--size", "0x1080")]
    [InlineData("width and a height", "--script", "script.json", "--headless", "--out", "out", "--size", "1920x16385")]
    [InlineData("width and a height", "--script", "script.json", "--headless", "--out", "out", "--size", "-5x10")]
    [InlineData("--tone-curve needs a value", "--script", "script.json", "--headless", "--out", "out", "--tone-curve")]
    [InlineData(
        "--tone-curve applies to a headless run",
        "--script",
        "script.json",
        "--out",
        "out",
        "--tone-curve",
        "hable"
    )]
    [InlineData("--tone-curve applies to a headless run", "--scene", "no-such-scene", "--tone-curve", "hable")]
    [InlineData("--bloom needs a value", "--script", "script.json", "--headless", "--out", "out", "--bloom")]
    [InlineData("--bloom applies to a headless run", "--script", "script.json", "--out", "out", "--bloom", "off")]
    [InlineData("--bloom applies to a headless run", "--scene", "no-such-scene", "--bloom", "off")]
    [InlineData("--bloom needs on or off", "--script", "script.json", "--headless", "--out", "out", "--bloom", "dim")]
    [InlineData(
        "--tone-curve needs aces or hable",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--tone-curve",
        "filmic"
    )]
    [InlineData(
        "--ambient-occlusion applies to a headless run",
        "--script",
        "script.json",
        "--out",
        "out",
        "--ambient-occlusion",
        "off"
    )]
    [InlineData(
        "--ambient-occlusion applies to a headless run",
        "--scene",
        "no-such-scene",
        "--ambient-occlusion",
        "off"
    )]
    [InlineData(
        "--anti-aliasing needs a value",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--anti-aliasing"
    )]
    [InlineData(
        "--anti-aliasing applies to a headless run",
        "--script",
        "script.json",
        "--out",
        "out",
        "--anti-aliasing",
        "off"
    )]
    [InlineData("--anti-aliasing applies to a headless run", "--scene", "no-such-scene", "--anti-aliasing", "off")]
    [InlineData(
        "--anti-aliasing needs off or smaa",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--anti-aliasing",
        "fxaa"
    )]
    public void AnOptionThatTheWindowRulesRefuseExitsWithTheCommandLineErrorCodeAndNamesTheRule(
        string rule,
        params string[] args
    )
    {
        int code = Run(args);

        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain(rule);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void AnAmbientOcclusionOptionThatIsNeitherOffNorAPositiveNumberIsAProblem(string value)
    {
        int code = Run("--script", "script.json", "--headless", "--out", "out", "--ambient-occlusion", value);

        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("--ambient-occlusion needs off or a radius").And.Contain($"'{value}'");
    }

    // The refused rows above name a scene that no table has, or a script file that does not exist. If a rule breaks, the
    // run then ends in ScriptError or FileError before any window code runs, instead of opening a window on a test thread.

    [Fact]
    public void TheWindowOptionsCarryTheirValues()
    {
        ScriptCommand.Options? free = ScriptCommand.ReadOptions(["--scene", "drop-box", "--speed", "0.5"], out _);
        ScriptCommand.Options? played = ScriptCommand.ReadOptions(["--script", "s.json", "--out", "o"], out _);

        free.Should()
            .Be(new ScriptCommand.Options(Script: null, Scene: "drop-box", Out: null, Headless: false, Speed: 0.5));
        played
            .Should()
            .Be(new ScriptCommand.Options(Script: "s.json", Scene: null, Out: "o", Headless: false, Speed: 1));
    }

    [Fact]
    public void TheSizeOptionCarriesTheWidthAndTheHeight()
    {
        ScriptCommand.Options? sized = ScriptCommand.ReadOptions(
            ["--script", "s.json", "--headless", "--out", "o", "--size", "1920x1080"],
            out string? problem
        );

        sized.Should().NotBeNull(problem);
        sized!.Size.Should().Be(new ScriptWindow(1920, 1080));
    }

    [Theory]
    [InlineData("aces", ToneCurve.AcesFitted)]
    [InlineData("hable", ToneCurve.Hable)]
    public void TheToneCurveOptionCarriesTheCurveThatItNames(string name, ToneCurve curve)
    {
        ScriptCommand.Options? toned = ScriptCommand.ReadOptions(
            ["--script", "s.json", "--headless", "--out", "o", "--tone-curve", name],
            out string? problem
        );

        toned.Should().NotBeNull(problem);
        toned!.ToneCurve.Should().Be(curve);
    }

    [Theory]
    [InlineData("off", AntiAliasing.None)]
    [InlineData("smaa", AntiAliasing.Smaa)]
    public void TheAntiAliasingOptionCarriesTheChoiceThatItNames(string name, AntiAliasing antiAliasing)
    {
        ScriptCommand.Options? chosen = ScriptCommand.ReadOptions(
            ["--script", "s.json", "--headless", "--out", "o", "--anti-aliasing", name],
            out string? problem
        );

        chosen.Should().NotBeNull(problem);
        chosen!.AntiAliasing.Should().Be(antiAliasing);
    }

    [Fact]
    public void TheVideoOptionsCarryTheFileAndTheFrameRateWhichIs30WithoutFps()
    {
        ScriptCommand.Options? rated = ScriptCommand.ReadOptions(
            ["--script", "s.json", "--headless", "--out", "o", "--video", "o/v.mkv", "--fps", "24"],
            out _
        );
        ScriptCommand.Options? plain = ScriptCommand.ReadOptions(
            ["--script", "s.json", "--headless", "--out", "o", "--video", "o/v.mkv"],
            out _
        );

        rated
            .Should()
            .Be(
                new ScriptCommand.Options(
                    Script: "s.json",
                    Scene: null,
                    Out: "o",
                    Headless: true,
                    Speed: 1,
                    Video: new("o/v.mkv", 24)
                )
            );
        plain
            .Should()
            .Be(
                new ScriptCommand.Options(
                    Script: "s.json",
                    Scene: null,
                    Out: "o",
                    Headless: true,
                    Speed: 1,
                    Video: new("o/v.mkv", 30)
                )
            );
    }

    [Theory]
    [InlineData("1")]
    [InlineData("240")]
    public void TheFrameRateMayBeAnyWholeNumberFrom1To240(string rate) =>
        ScriptCommand
            .ReadOptions(
                ["--script", "s.json", "--headless", "--out", "o", "--video", "v.mkv", "--fps", rate],
                out string? problem
            )
            .Should()
            .NotBeNull(problem);

    [Fact]
    public void AVideoForAScriptWithNoCaptureOrFilmExitsWithTheCommandLineErrorCodeAndStartsNoFfmpeg()
    {
        string script = ScriptFile(
            """{ "scene": "probes", "steps": [ { "do": "run", "ticks": 1 }, { "do": "dump", "name": "end" } ] }"""
        );
        string video = Path.Combine(folder, "v.mkv");
        int started = 0;

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, "--video", video],
            scenes: Scenes,
            output: output,
            error: error,
            createRenderer: VulkanRenderer.CreateHeadless,
            startVideo: (_, _) =>
            {
                started++;
                throw new InvalidOperationException("no ffmpeg in this test");
            }
        );

        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("--video").And.Contain("no capture or film step");
        started.Should().Be(0);
        File.Exists(Path.Combine(Out, "end.json"))
            .Should()
            .BeFalse("the command refuses the run before the first tick");
    }

    [Fact]
    public void TheRecordOptionOfFreePlayCarriesItsPath()
    {
        ScriptCommand.Options? recorded = ScriptCommand.ReadOptions(
            ["--scene", "drop-box", "--record", "sessions/today.json", "--speed", "2"],
            out _
        );

        recorded
            .Should()
            .Be(
                new ScriptCommand.Options(
                    Script: null,
                    Scene: "drop-box",
                    Out: null,
                    Headless: false,
                    Speed: 2,
                    Record: "sessions/today.json"
                )
            );
    }

    [Fact]
    public void AnUnknownSceneForFreePlayWritesNoRecord()
    {
        string record = Path.Combine(folder, "record.json");

        int code = Run("--scene", "moon-base", "--record", record);

        code.Should().Be(ScriptCommand.ScriptError);
        File.Exists(record).Should().BeFalse();
    }

    [Fact]
    public void AnUnknownSceneForFreePlayExitsWithTheScriptErrorCodeBeforeAWindowOpens()
    {
        int code = Run("--scene", "moon-base");

        code.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain("moon-base");
    }
}
