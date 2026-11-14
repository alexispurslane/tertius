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
    [InlineData("--video applies to a headless run", "--script", "script.json", "--out", "out", "--video", "none")]
    [InlineData(
        "--video none writes no video",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--video",
        "none",
        "--fps",
        "30"
    )]
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
        "--screen-trace needs a value",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--screen-trace"
    )]
    [InlineData(
        "--screen-trace applies to a headless run",
        "--script",
        "script.json",
        "--out",
        "out",
        "--screen-trace",
        "off"
    )]
    [InlineData("--screen-trace applies to a headless run", "--scene", "no-such-scene", "--screen-trace", "off")]
    [InlineData(
        "--screen-trace needs on or off",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--screen-trace",
        "mirror"
    )]
    [InlineData(
        "--environment-map needs a value",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--environment-map"
    )]
    [InlineData(
        "--environment-map applies to a headless run",
        "--script",
        "script.json",
        "--out",
        "out",
        "--environment-map",
        "off"
    )]
    [InlineData("--environment-map applies to a headless run", "--scene", "no-such-scene", "--environment-map", "off")]
    [InlineData(
        "--environment-map needs on or off, not 'dim'",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--environment-map",
        "dim"
    )]
    [InlineData("--subsurface needs a value", "--script", "script.json", "--headless", "--out", "out", "--subsurface")]
    [InlineData(
        "--subsurface applies to a headless run",
        "--script",
        "script.json",
        "--out",
        "out",
        "--subsurface",
        "off"
    )]
    [InlineData("--subsurface applies to a headless run", "--scene", "no-such-scene", "--subsurface", "off")]
    [InlineData(
        "--in-flight needs 1 or 2, not '3'",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--in-flight",
        "3"
    )]
    [InlineData("--in-flight applies to a headless run", "--script", "script.json", "--out", "out", "--in-flight", "2")]
    [InlineData(
        "--subsurface needs on or off, not 'waxy'",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--subsurface",
        "waxy"
    )]
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
    [InlineData("--upscaling needs a value", "--script", "script.json", "--headless", "--out", "out", "--upscaling")]
    [InlineData(
        "--upscaling applies to a headless run",
        "--script",
        "script.json",
        "--out",
        "out",
        "--upscaling",
        "off"
    )]
    [InlineData("--upscaling applies to a headless run", "--scene", "no-such-scene", "--upscaling", "off")]
    [InlineData(
        "--upscaling needs off or fsr3",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--upscaling",
        "fxaa"
    )]
    [InlineData(
        "--local-shadows needs a value",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--local-shadows"
    )]
    [InlineData(
        "--local-shadows applies to a headless run",
        "--script",
        "script.json",
        "--out",
        "out",
        "--local-shadows",
        "off"
    )]
    [InlineData("--local-shadows applies to a headless run", "--scene", "no-such-scene", "--local-shadows", "off")]
    [InlineData(
        "--local-shadows needs off or on",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--local-shadows",
        "soft"
    )]
    [InlineData(
        "--local-shadow-tiles applies to a headless run",
        "--script",
        "script.json",
        "--out",
        "out",
        "--local-shadow-tiles",
        "16"
    )]
    [InlineData(
        "--local-shadow-tiles sets the budget of local shadows, so leave out --local-shadows off",
        "--script",
        "script.json",
        "--headless",
        "--out",
        "out",
        "--local-shadows",
        "off",
        "--local-shadow-tiles",
        "16"
    )]
    [InlineData(
        "--cascaded-lights applies to a headless run",
        "--script",
        "script.json",
        "--out",
        "out",
        "--cascaded-lights",
        "2"
    )]
    [InlineData("--cascaded-lights applies to a headless run", "--scene", "no-such-scene", "--cascaded-lights", "2")]
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
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("2.5")]
    [InlineData("many")]
    public void ALocalShadowTilesOptionThatIsNotAWholeNumberOfOneOrMoreIsAProblem(string value)
    {
        int code = Run("--script", "script.json", "--headless", "--out", "out", "--local-shadow-tiles", value);

        code.Should().Be(ScriptCommand.CommandLineError);
        error
            .ToString()
            .Should()
            .Contain("--local-shadow-tiles needs a whole number of 1 or more")
            .And.Contain($"'{value}'");
    }

    [Theory]
    [InlineData("5")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("one")]
    public void ACascadedLightsOptionThatIsNotAWholeNumberFromZeroToFourIsAProblem(string value)
    {
        int code = Run("--script", "script.json", "--headless", "--out", "out", "--cascaded-lights", value);

        code.Should().Be(ScriptCommand.CommandLineError);
        error
            .ToString()
            .Should()
            .Contain("--cascaded-lights needs a whole number from 0 to 4")
            .And.Contain($"'{value}'");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("on")]
    [InlineData("2.5")]
    public void AKeyLightsOptionThatIsNeitherOffNorAWholeNumberFromOneToFourIsAProblem(string value)
    {
        int code = Run("--script", "script.json", "--headless", "--out", "out", "--key-lights", value);

        code.Should().Be(ScriptCommand.CommandLineError);
        error
            .ToString()
            .Should()
            .Contain("--key-lights needs off or a whole number from 1 to 4")
            .And.Contain($"'{value}'");
    }

    [Theory]
    [InlineData("65")]
    [InlineData("-64.5")]
    [InlineData("NaN")]
    [InlineData("bright")]
    public void AnExposureOptionThatIsNotANumberOfStopsFromMinusSixtyFourToSixtyFourIsAProblem(string value)
    {
        int code = Run("--script", "script.json", "--headless", "--out", "out", "--exposure", value);

        code.Should().Be(ScriptCommand.CommandLineError);
        error
            .ToString()
            .Should()
            .Contain("--exposure needs a number of stops from -64 to 64")
            .And.Contain($"'{value}'");
    }

    // The refused rows above name a scene that no table has, or a script file that does not exist. If a rule breaks, the
    // run then ends in ScriptError or FileError before any window code runs, instead of opening a window on a test thread.

    [Fact]
    public void FreePlayCarriesItsSpeedWithoutARecord()
    {
        ScriptCommand.RunOptions? free = ScriptCommand.ReadOptions(["--scene", "drop-box", "--speed", "0.5"], out _);

        free.Should()
            .Be(new ScriptCommand.RunOptions(new ScriptCommand.FreePlayRun("drop-box", Speed: 0.5, Record: null)));
    }

    /// <summary>The script run of a command line that ReadOptions takes.</summary>
    private static ScriptCommand.ScriptRun ScriptRunOf(string[] args)
    {
        ScriptCommand.RunOptions? options = ScriptCommand.ReadOptions(args, out string? problem);
        options.Should().NotBeNull(problem);
        return options.Value.Value.Should().BeOfType<ScriptCommand.ScriptRun>().Subject;
    }

    /// <summary>The headless display of a script run's command line that ReadOptions takes.</summary>
    private static ScriptCommand.Headless HeadlessOf(string[] args) =>
        ScriptRunOf(args).Display.Value.Should().BeOfType<ScriptCommand.Headless>().Subject;

    [Fact]
    public void TheSizeOptionCarriesTheWidthAndTheHeight()
    {
        ScriptCommand.ScriptRun sized = ScriptRunOf([
            "--script",
            "s.json",
            "--headless",
            "--out",
            "o",
            "--size",
            "1920x1080",
        ]);

        sized.Size.Should().Be(new ScriptWindow(1920, 1080));
    }

    [Theory]
    [InlineData("aces", ToneCurve.AcesFitted)]
    [InlineData("hable", ToneCurve.Hable)]
    public void TheToneCurveOptionCarriesTheCurveThatItNames(string name, ToneCurve curve)
    {
        ScriptCommand.Headless toned = HeadlessOf([
            "--script",
            "s.json",
            "--headless",
            "--out",
            "o",
            "--tone-curve",
            name,
        ]);

        toned.Renderer.ToneCurve.Should().Be(curve);
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--environment-map", "on" }, false)]
    [InlineData(new[] { "--environment-map", "off" }, true)]
    public void TheEnvironmentMapOptionTurnsTheHeadlessRenderersMapOffAndOnKeepsTheRenderersDefault(
        string[] environmentMap,
        bool off
    )
    {
        string script = ScriptFile("""{ "scene": "probes", "steps": [ { "do": "capture", "name": "front" } ] }""");
        var settings = new List<EnvironmentMapSetting>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, .. environmentMap],
            scenes: Scenes,
            output: output,
            error: error,
            createRenderer: options =>
            {
                settings.Add(options.EnvironmentMap);
                throw new InvalidOperationException("the test stops at the renderer's options");
            }
        );

        code.Should().Be(ScriptCommand.ToolError, "the test's renderer cannot start");
        EnvironmentMapSetting expected = off ? new NoEnvironmentMap() : new RendererOptions().EnvironmentMap;
        settings.Should().Equal(expected);
    }

    [Fact]
    public void TheDirectLightOptionIsUnknownBecauseTheRendererTakesEveryLightExactly()
    {
        int code = Run("--script", "script.json", "--headless", "--out", "out", "--direct-light", "every");

        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("Unknown option '--direct-light'");
    }

    [Theory]
    [InlineData("off")]
    [InlineData("fsr3")]
    public void TheUpscalingOptionCarriesTheChoiceThatItNames(string name)
    {
        ScriptCommand.Headless chosen = HeadlessOf([
            "--script",
            "s.json",
            "--headless",
            "--out",
            "o",
            "--upscaling",
            name,
        ]);

        UpscalingSetting expected = name == "off" ? new NoUpscaling() : Fsr3Upscaling.Default;
        chosen.Renderer.Upscaling.Should().Be(expected);
    }

    [Theory]
    [InlineData("0.5", 0.5f)]
    [InlineData("0.6666667", 2f / 3)]
    [InlineData("1", 1f)]
    public void AHeadlessRenderScaleOverridesTheFixedFsrScale(string text, float scale)
    {
        ScriptCommand.Headless chosen = HeadlessOf([
            "--script",
            "s.json",
            "--headless",
            "--out",
            "o",
            "--render-scale",
            text,
        ]);
        var options = new RendererOptions();

        chosen.Renderer.ApplyTo(options);

        options.Upscaling.Should().Be((UpscalingSetting)new Fsr3Upscaling(new FixedRenderScale(scale), 0));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0.49")]
    [InlineData("1.01")]
    public void AHeadlessRenderScaleOutsideTheFiniteRangeIsRefused(string text)
    {
        ScriptCommand.RunOptions? chosen = ScriptCommand.ReadOptions(
            ["--script", "s.json", "--headless", "--out", "o", "--render-scale", text],
            out string? problem
        );

        chosen.Should().BeNull();
        problem.Should().Contain("--render-scale");
    }

    [Fact]
    public void AHeadlessRunWithoutARenderScaleUsesTwoThirdsRatherThanTheWindowController()
    {
        ScriptCommand.Headless chosen = HeadlessOf(["--script", "s.json", "--headless", "--out", "o"]);
        var options = new RendererOptions();

        chosen.Renderer.ApplyTo(options);

        options.Upscaling.Should().Be((UpscalingSetting)new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0));
    }

    public static TheoryData<string[], LocalShadowSetting?> LocalShadowOptions =>
        new()
        {
            { [], null },
            { ["--local-shadows", "off"], new NoLocalShadows() },
            { ["--local-shadows", "on"], LocalShadowAtlas.Default },
            { ["--local-shadow-tiles", "384"], LocalShadowAtlas.Default with { Tiles = 384 } },
            { ["--local-shadows", "on", "--local-shadow-tiles", "1"], LocalShadowAtlas.Default with { Tiles = 1 } },
        };

    [Theory]
    [MemberData(nameof(LocalShadowOptions))]
    public void TheLocalShadowOptionsTurnTheAtlasOffOrOnAndSetItsBudget(
        string[] localShadows,
        LocalShadowSetting? expected
    )
    {
        ScriptCommand.Headless chosen = HeadlessOf(["--script", "s.json", "--headless", "--out", "o", .. localShadows]);

        chosen.Renderer.LocalShadows.Should().Be(expected);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("4", 4)]
    public void TheCascadedLightsOptionCarriesItsCount(string value, int count)
    {
        ScriptCommand.Headless chosen = HeadlessOf([
            "--script",
            "s.json",
            "--headless",
            "--out",
            "o",
            "--cascaded-lights",
            value,
        ]);

        chosen.Renderer.CascadedLights.Should().Be(count);
    }

    [Fact]
    public void TheVideoOptionsCarryTheFileAndTheFrameRateWhichIs30WithoutFps()
    {
        ScriptCommand.Headless rated = HeadlessOf([
            "--script",
            "s.json",
            "--headless",
            "--out",
            "o",
            "--video",
            "o/v.mkv",
            "--fps",
            "24",
        ]);
        ScriptCommand.Headless plain = HeadlessOf([
            "--script",
            "s.json",
            "--headless",
            "--out",
            "o",
            "--video",
            "o/v.mkv",
        ]);

        rated.Video.Should().Be(new ScriptCommand.HeadlessVideo(new VideoFile("o/v.mkv", 24)));
        plain.Video.Should().Be(new ScriptCommand.HeadlessVideo(new VideoFile("o/v.mkv", 30)));
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
    public void VideoNoneDiscardsTheCapturesOfAHeadlessRun()
    {
        ScriptCommand.Headless discarded = HeadlessOf([
            "--script",
            "s.json",
            "--headless",
            "--out",
            "o",
            "--video",
            "none",
        ]);

        discarded.Video.Should().Be(new ScriptCommand.HeadlessVideo(new Discard()));
    }

    [Fact]
    public void TheUsageLineNamesVideoNoneBesideTheVideoFile()
    {
        int code = Run("--script", "script.json", "--headless", "--out", "out", "--video");

        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("[--video none|<file.mkv> [--fps <rate>]]");
    }

    [Theory]
    [InlineData("v.mkv")]
    [InlineData("none")]
    public void AVideoForAScriptWithNoCaptureOrFilmExitsWithTheCommandLineErrorCodeAndStartsNoFfmpeg(string name)
    {
        string script = ScriptFile(
            """{ "scene": "probes", "steps": [ { "do": "run", "ticks": 1 }, { "do": "dump", "name": "end" } ] }"""
        );
        string video = name == "none" ? name : Path.Combine(folder, name);
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
