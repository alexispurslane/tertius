using System.Text.Json;
using Tertius.Demo;
using Tertius.Rendering;
using static Tertius.Host.Tests.GpuTesting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>The command line with scripts that capture images, which makes it create a headless renderer.</summary>
public sealed class ScriptCommandCaptureTests : IDisposable
{
    /// <summary>
    /// The timed passes of a headless frame with the renderer's defaults, in frame order: the deformation first, the local
    /// shadows after the cascades, the environment map's faces and prefilter after them, the surface test after the
    /// G-buffer, the depth pyramid and key and fill before the direct pass, the screen tracer's passes and its two accumulations between the
    /// direct pass and the composite, the skin scatter's two passes after the composite, the atmosphere's haze before the
    /// transparent pass, FSR before the output-sized bloom and tone map. A pass that records nothing, such as the haze without an atmosphere, still
    /// takes its timestamps.
    /// </summary>
    private static readonly string[] FramePasses =
    [
        "deformation",
        "shadow maps",
        "local shadows",
        "environment faces",
        "environment prefilter",
        "G-buffer",
        "surface test",
        "depth pyramid",
        "key and fill",
        "direct light",
        "light pyramid",
        "ray plan",
        "reflection rays",
        "diffuse rays",
        "screen trace resolve",
        "accumulate indirect diffuse",
        "filter indirect diffuse",
        "accumulate indirect specular",
        "composite",
        "scatter rows",
        "scatter columns",
        "haze",
        "transparent",
        "exposure",
        "FSR 3.1",
        "exposure adapt",
        "bloom",
        "tone map",
        "outlines",
        "capture",
    ];

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

    [Fact]
    public void TheCommandCapturesWithAHeadlessRendererAndWritesThePathOfEveryFile()
    {
        string script = ScriptFile(
            """
            { "scene": "lit-box", "window": { "width": 160, "height": 90 },
              "steps": [ { "do": "capture", "name": "front" }, { "do": "dump", "name": "state" } ] }
            """
        );

        // The lit box shows its color by the hemisphere's light, which the environment map replaces.
        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, "--environment-map", "off"],
            scenes: [LitBoxScene],
            output: output,
            error: error
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        Png.Load(Path.Combine(Out, "front.png"))
            .IsRed(80, 45)
            .Should()
            .BeTrue("the scene gets the renderer's resources, so its box has a mesh");
        output.ToString().Should().Contain(Path.Combine(Out, "front.png")).And.Contain(Path.Combine(Out, "state.json"));
    }

    [Fact]
    public void AScriptThatOnlyCapturesGetsOneHeadlessRendererWithoutImpostors()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );
        var made = new List<VulkanRenderer>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                made.Add(VulkanRenderer.CreateHeadless(options));
                return made[^1];
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        made.Should().ContainSingle();
        File.Exists(Path.Combine(Out, "front.png")).Should().BeTrue();
        // The box is about 40 px tall, below the impostor threshold, so a renderer with impostors on would capture it and
        // draw it as an impostor. A scripted run draws it as geometry, so its images do not depend on impostor state.
        made[0].LastFrameStats.Impostors.Should().Be(0);
        made[0].LastFrameStats.ImpostorCaptures.Should().Be(0);
    }

    [Fact]
    public void AScriptThatOnlyFilmsGetsAHeadlessRendererAndWritesThePathOfEveryFrame()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 2, "every": 1 } ] }"""
        );

        // The lit box shows its color by the hemisphere's light, which the environment map replaces.
        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, "--environment-map", "off"],
            scenes: [LitBoxScene],
            output: output,
            error: error
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        Png.Load(Path.Combine(Out, "box-000000.png"))
            .IsRed(80, 45)
            .Should()
            .BeTrue("the scene gets the renderer's resources, so its box has a mesh");
        Png.Load(Path.Combine(Out, "box-000001.png")).IsRed(80, 45).Should().BeTrue();
        output
            .ToString()
            .Should()
            .Contain(Path.Combine(Out, "box-000000.png"))
            .And.Contain(Path.Combine(Out, "box-000001.png"));
    }

    [Fact]
    public void ARendererThatCannotStartExitsWithTheToolErrorCodeAndWritesNoImage()
    {
        string script = ScriptFile("""{ "scene": "lit-box", "steps": [ { "do": "capture", "name": "front" } ] }""");

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: _ => throw new InvalidOperationException("no Vulkan driver")
        );

        code.Should().Be(ScriptCommand.ToolError);
        error.ToString().Should().Contain(script).And.Contain("no Vulkan driver");
        File.Exists(Path.Combine(Out, "front.png")).Should().BeFalse();
    }

    [Fact]
    public void TheDemoRunsItsDropBoxScript()
    {
        string root = RepositoryRoot();

        int code = ScriptCommand.Run(
            args: ["--script", Path.Combine(root, "scripts", "drop-box.json"), "--headless", "--out", Out],
            scenes: DemoScenes.All,
            output: output,
            error: error
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        using var landed = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, "landed.json")));
        landed.RootElement.GetProperty("camera").GetString().Should().Be("camera");
        JsonElement box = landed
            .RootElement.GetProperty("entities")
            .EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == "box");
        box.GetProperty("pose")
            .GetProperty("position")[1]
            .GetDouble()
            .Should()
            .BeApproximately(0.5, 0.02, "the 1 m box rests on the floor whose top is at y = 0");
        // The camera at (0, 2.5, 9), tilted down 6.3 degrees, sees the box's center at y = 3 on row 256 and at y = 0.5 on
        // row 428 of the 720 rows.
        var start = Png.Load(Path.Combine(Out, "start.png"));
        start.IsOrange(640, 256).Should().BeTrue("the box starts at y = 3");
        var end = Png.Load(Path.Combine(Out, "landed.png"));
        end.IsOrange(640, 428).Should().BeTrue("the box rests on the floor");
        end.IsOrange(640, 256).Should().BeFalse("the box has left its start");
    }

    [Fact]
    public void AHeadlessFilmWritesTheGpuTimeOfEachPassOverItsFinishedFramesToAFileWhosePathItPrints()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 6, "every": 1 } ] }"""
        );
        var made = new List<VulkanRenderer>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                made.Add(VulkanRenderer.CreateHeadless(options));
                return made[^1];
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        if (!made[0].TimesPasses)
            Assert.Skip($"{made[0].DeviceName} writes no timestamps on its graphics queue.");
        string file = Path.Combine(Out, ScriptCommand.GpuTimingsFile);
        using var timings = JsonDocument.Parse(File.ReadAllText(file));
        JsonElement[] passes = [.. timings.RootElement.GetProperty("passes").EnumerateArray()];
        passes.Select(pass => pass.GetProperty("pass").GetString()).Should().Equal(FramePasses);
        passes
            .Select(pass => pass.GetProperty("frames").GetInt32())
            .Should()
            .AllBeEquivalentTo(
                6,
                "each capture reads its own frame after its wait, and the first frame's metering draw is read after a newer frame"
            );
        passes
            .Should()
            .AllSatisfy(pass =>
                pass.GetProperty("p95Ms")
                    .GetDouble()
                    .Should()
                    .BeGreaterThanOrEqualTo(pass.GetProperty("medianMs").GetDouble())
            );
        output.ToString().Should().Contain(file);
    }

    [Fact]
    public void AHeadlessFilmWritesTheGpuSpanOfItsFramesBesideThePassesWithAMeanNoShorterThanItsLongestPass()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 6, "every": 1 } ] }"""
        );
        var made = new List<VulkanRenderer>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                made.Add(VulkanRenderer.CreateHeadless(options));
                return made[^1];
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        if (!made[0].TimesPasses)
            Assert.Skip($"{made[0].DeviceName} writes no timestamps on its graphics queue.");
        using var timings = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, ScriptCommand.GpuTimingsFile)));
        JsonElement frame = timings.RootElement.GetProperty("frame");
        int spans = frame.GetProperty("frames").GetInt32();
        spans.Should().Be(6, "the film times one span per film frame");
        JsonElement[] passes = [.. timings.RootElement.GetProperty("passes").EnumerateArray()];
        passes
            .Should()
            .AllSatisfy(pass =>
                pass.GetProperty("frames")
                    .GetInt32()
                    .Should()
                    .Be(spans, "the span counts the same frames as the passes")
            );
        double longestPass = passes.Max(pass => pass.GetProperty("meanMs").GetDouble());
        frame
            .GetProperty("meanMs")
            .GetDouble()
            .Should()
            .BeGreaterThanOrEqualTo(longestPass, "the span of each frame holds all of its passes")
            .And.BeLessThan(1_000);
        frame
            .GetProperty("p95Ms")
            .GetDouble()
            .Should()
            .BeGreaterThanOrEqualTo(frame.GetProperty("medianMs").GetDouble());
    }

    [Fact]
    public void AHeadlessFilmPrintsIndividualPassTimesOnlyOnADriverWhoseStampsDoNotLatchLate()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 6, "every": 1 } ] }"""
        );
        var made = new List<VulkanRenderer>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                made.Add(VulkanRenderer.CreateHeadless(options));
                return made[^1];
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        if (!made[0].TimesPasses)
            Assert.Skip($"{made[0].DeviceName} writes no timestamps on its graphics queue.");
        output
            .ToString()
            .Split(Environment.NewLine)
            .Where(line => line.StartsWith("GPU ", StringComparison.Ordinal))
            .Select(line => line[..line.IndexOf(": mean ", StringComparison.Ordinal)])
            .Should()
            .Equal(
                made[0].StampsLatchLate
                    ? ["GPU frame span"]
                    : [.. FramePasses.Select(pass => "GPU " + pass), "GPU frame span"]
            );
    }

    [Fact]
    public void AHeadlessFilmReportsEveryRequestedGpuRunBesideTheUnchangedRawPasses()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 3, "every": 1 } ] }"""
        );
        var made = new List<VulkanRenderer>();

        int code = ScriptCommand.Run(
            args:
            [
                "--script",
                script,
                "--headless",
                "--out",
                Out,
                "--video",
                "none",
                "--gpu-run",
                "bloom..capture",
                "--gpu-run",
                "shadow maps..direct light",
            ],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                made.Add(VulkanRenderer.CreateHeadless(options));
                return made[^1];
            }
        );

        if (!made[0].TimesPasses)
            Assert.Skip($"{made[0].DeviceName} writes no timestamps on its graphics queue.");
        code.Should().Be(ScriptCommand.Ok, error.ToString());
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, ScriptCommand.GpuTimingsFile)));
        JsonElement[] runs = [.. json.RootElement.GetProperty("runs").EnumerateArray()];
        runs.Select(run => (run.GetProperty("first").GetString(), run.GetProperty("last").GetString()))
            .Should()
            .Equal(("bloom", "capture"), ("shadow maps", "direct light"));
        runs.Should().AllSatisfy(run => run.GetProperty("timing").GetProperty("frames").GetInt32().Should().Be(3));
        json.RootElement.GetProperty("passes")
            .EnumerateArray()
            .Select(pass => pass.GetProperty("pass").GetString())
            .Should()
            .Equal(FramePasses);
        json.RootElement.GetProperty("stampsLatchLate").GetBoolean().Should().Be(made[0].StampsLatchLate);
        output
            .ToString()
            .Should()
            .Contain("GPU run bloom..capture: mean ")
            .And.Contain("GPU run shadow maps..direct light: mean ");
    }

    [Theory]
    [InlineData("unknown..capture", "unknown pass 'unknown'")]
    [InlineData("capture..bloom", "is reversed")]
    public void AnInvalidGpuRunIsACommandLineErrorRatherThanAnInternalFailure(string run, string problem)
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "box" } ] }"""
        );
        var made = new List<VulkanRenderer>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, "--video", "none", "--gpu-run", run],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                made.Add(VulkanRenderer.CreateHeadless(options));
                return made[^1];
            }
        );

        if (!made[0].TimesPasses)
            Assert.Skip($"{made[0].DeviceName} writes no timestamps on its graphics queue.");
        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("--gpu-run").And.Contain(problem).And.NotContain("The run failed");
        File.Exists(Path.Combine(Out, ScriptCommand.GpuTimingsFile)).Should().BeFalse();
    }

    [Fact]
    public void GpuRunsOnAScriptWithoutRenderedFramesExplainWhyNoTimingsExist()
    {
        string script = ScriptFile("""{ "scene": "lit-box", "steps": [ { "do": "dump", "name": "box" } ] }""");

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, "--gpu-run", "bloom..capture"],
            scenes: [LitBoxScene],
            output: output,
            error: error
        );

        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("--gpu-run").And.Contain("no GPU timings");
    }

    [Fact]
    public void AHeadlessFilmPrintsTheMarkersOfItsTimedSpanOnceEachBeforeTheFilesAndTheGpuTimings()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 6, "every": 1 }, { "do": "dump", "name": "end" } ] }"""
        );

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, "--video", "none"],
            scenes: [LitBoxScene],
            output: output,
            error: error
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        string[] lines = output.ToString().Split(Environment.NewLine);
        lines
            .Where(line => line is ScriptCommand.TimedStart or ScriptCommand.TimedEnd)
            .Should()
            .Equal(
                [ScriptCommand.TimedStart, ScriptCommand.TimedEnd],
                "tools/gpu_clock.py takes gpu-clock's samples between the two lines as the clock of the timed frames"
            );
        Array
            .IndexOf(lines, ScriptCommand.TimedEnd)
            .Should()
            .BeLessThan(
                Array.IndexOf(lines, Path.Combine(Out, "end.json")),
                "the timed span ends when the script's frames end, before the run writes its files"
            );
    }

    [Fact]
    public void AScriptWithDumpsOnlyPrintsNoMarkersOfATimedSpan()
    {
        string script = ScriptFile("""{ "scene": "lit-box", "steps": [ { "do": "dump", "name": "only" } ] }""");

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out],
            scenes: [LitBoxScene],
            output: output,
            error: error
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        output.ToString().Should().NotContain(ScriptCommand.TimedStart).And.NotContain(ScriptCommand.TimedEnd);
    }

    [Fact]
    public void AFilmOfOneFrameStillPrintsBothMarkersSoThatEveryTimedStartHasItsEnd()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 1, "every": 1 } ] }"""
        );

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, "--video", "none"],
            scenes: [LitBoxScene],
            output: output,
            error: error
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        output
            .ToString()
            .Split(Environment.NewLine)
            .Where(line => line is ScriptCommand.TimedStart or ScriptCommand.TimedEnd)
            .Should()
            .Equal(
                [ScriptCommand.TimedStart, ScriptCommand.TimedEnd],
                "tools/gpu_clock.py leaves out a span that no timed-end closes, and the first shown frame opens the span"
            );
    }

    [Fact]
    public void AHeadlessFilmExplainsWhyIndividualPassTimesAreOmittedOnlyWhenItsDriverLatchesStampsLate()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 6, "every": 1 } ] }"""
        );
        var made = new List<VulkanRenderer>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                made.Add(VulkanRenderer.CreateHeadless(options));
                return made[^1];
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        if (!made[0].TimesPasses)
            Assert.Skip($"{made[0].DeviceName} writes no timestamps on its graphics queue.");
        string[] lines = output.ToString().Split(Environment.NewLine);
        int span = Array.FindIndex(lines, line => line.StartsWith("GPU frame span: ", StringComparison.Ordinal));
        string[] expected = made[0].StampsLatchLate ? [ScriptCommand.LateStampsNote] : [$"GPU {FramePasses[^1]}"];
        lines[(span - expected.Length)..span]
            .Select(line =>
                line.StartsWith("GPU ", StringComparison.Ordinal)
                    ? line[..line.IndexOf(": mean ", StringComparison.Ordinal)]
                    : line
            )
            .Should()
            .Equal(
                expected,
                "the note replaces individual pass lines on a driver whose stamps latch late, and only there"
            );
        if (made[0].StampsLatchLate)
            output.ToString().Should().Contain("Individual pass times are omitted");
    }

    [Fact]
    public void TheSizeOptionReplacesTheSizeOfTheScriptsWindow()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, "--size", "200x100"],
            scenes: [LitBoxScene],
            output: output,
            error: error
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        var front = Png.Load(Path.Combine(Out, "front.png"));
        (front.Width, front.Height).Should().Be((200, 100));
    }

    [Theory]
    [InlineData(new string[0], ToneCurve.AcesFitted)]
    [InlineData(new[] { "--tone-curve", "hable" }, ToneCurve.Hable)]
    public void TheToneCurveOptionChoosesTheCurveOfTheHeadlessRendererWhichIsTheRenderersDefaultWithoutIt(
        string[] toneCurve,
        ToneCurve expected
    )
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );
        var curves = new List<ToneCurve>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, .. toneCurve],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                curves.Add(options.ToneCurve);
                return VulkanRenderer.CreateHeadless(options);
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        curves.Should().Equal(expected);
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--bloom", "on" }, false)]
    [InlineData(new[] { "--bloom", "off" }, true)]
    public void TheBloomOptionTurnsTheHeadlessRenderersBloomOffAndOnKeepsTheRenderersDefault(string[] bloom, bool off)
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );
        var settings = new List<object?>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, .. bloom],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                settings.Add(options.Bloom.Value);
                return VulkanRenderer.CreateHeadless(options);
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        object expected = off ? new NoBloom() : new RendererOptions().Bloom.Value!;
        settings.Should().ContainSingle().Which.Should().Be(expected, "records compare by value");
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--screen-trace", "on" }, false)]
    [InlineData(new[] { "--screen-trace", "off" }, true)]
    public void TheScreenTraceOptionTurnsTheHeadlessRenderersTracerOffAndOnKeepsTheRenderersDefault(
        string[] screenTrace,
        bool off
    )
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );
        var settings = new List<object?>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, .. screenTrace],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                settings.Add(options.ScreenTrace.Value);
                return VulkanRenderer.CreateHeadless(options);
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        object expected = off ? new NoScreenTrace() : new RendererOptions().ScreenTrace.Value!;
        settings.Should().ContainSingle().Which.Should().Be(expected, "records compare by value");
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--subsurface", "on" }, false)]
    [InlineData(new[] { "--subsurface", "off" }, true)]
    public void TheSubsurfaceOptionTurnsTheHeadlessRenderersSkinScatterOffAndOnKeepsTheRenderersDefault(
        string[] subsurface,
        bool off
    )
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );
        var settings = new List<object?>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, .. subsurface],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                settings.Add(options.Subsurface.Value);
                return VulkanRenderer.CreateHeadless(options);
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        object expected = off ? new NoSubsurface() : new RendererOptions().Subsurface.Value!;
        settings.Should().ContainSingle().Which.Should().Be(expected, "records compare by value");
    }

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "--pass-timestamps", "on" }, true)]
    [InlineData(new[] { "--pass-timestamps", "off" }, false)]
    public void ThePassTimestampsOptionTurnsTheHeadlessRenderersPassStampsOffAndOnKeepsThem(
        string[] passTimestamps,
        bool kept
    )
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );
        var settings = new List<bool>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, .. passTimestamps],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                settings.Add(options.PassTimestamps);
                return VulkanRenderer.CreateHeadless(options);
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        settings
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(kept, "a headless run is a measured run unless told otherwise");
    }

    [Theory]
    [InlineData(new string[0], false, 2)]
    [InlineData(new[] { "--in-flight", "1" }, false, 1)]
    [InlineData(new[] { "--in-flight", "2" }, true, 2)]
    public void TheInFlightOptionSetsTheRenderersFrameCountAndWhetherCapturesWait(
        string[] inFlight,
        bool kept,
        int frames
    )
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );
        var settings = new List<(bool Captures, int Frames)>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, .. inFlight],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                settings.Add((options.CapturesInFlight, options.FramesInFlight));
                return VulkanRenderer.CreateHeadless(options);
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        settings.Should().Equal([(kept, frames)]);
    }

    [Fact]
    public void AFilmWithTwoFramesInFlightWritesTheImagesAndTheReadingsOfAFilmThatWaitsForEachFrame()
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 5, "every": 1 } ] }"""
        );
        string waited = Path.Combine(folder, "waited");
        string inFlight = Path.Combine(folder, "in-flight");

        int waitedCode = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", waited],
            scenes: [LitBoxScene],
            output: output,
            error: error
        );
        int inFlightCode = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", inFlight, "--in-flight", "2"],
            scenes: [LitBoxScene],
            output: output,
            error: error
        );

        waitedCode.Should().Be(ScriptCommand.Ok, error.ToString());
        inFlightCode.Should().Be(ScriptCommand.Ok, error.ToString());
        string[] images = [.. Directory.GetFiles(waited, "*.png").Select(Path.GetFileName).Order()!];
        images.Should().HaveCount(5);
        Directory.GetFiles(inFlight, "*.png").Select(Path.GetFileName).Order().Should().Equal(images);
        foreach (string image in images)
            File.ReadAllBytes(Path.Combine(inFlight, image))
                .Should()
                .Equal(File.ReadAllBytes(Path.Combine(waited, image)), $"{image} is the same frame");
        File.ReadAllText(Path.Combine(inFlight, ScriptCommand.ExposureFile))
            .Should()
            .Be(File.ReadAllText(Path.Combine(waited, ScriptCommand.ExposureFile)), "each capture reads its own frame");
        using var timings = JsonDocument.Parse(File.ReadAllText(Path.Combine(inFlight, ScriptCommand.GpuTimingsFile)));
        if (timings.RootElement.TryGetProperty("frame", out JsonElement frame))
            frame.GetProperty("frames").GetInt32().Should().Be(5, "the end of the run hands over the frames in flight");
    }

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "--upscaling", "off" }, false)]
    public void TheUpscalingOptionChoosesTheUpscalingOfTheHeadlessRendererWhichIsTheRenderersDefaultWithoutIt(
        string[] upscaling,
        bool expected
    )
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );
        var chosen = new List<bool>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, .. upscaling],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                chosen.Add(options.Upscaling is Fsr3Upscaling);
                return VulkanRenderer.CreateHeadless(options);
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        chosen.Should().Equal(expected);
    }

    public static TheoryData<string[], LocalShadowSetting, int> ShadowOptions =>
        new()
        {
            { [], LocalShadowAtlas.Default, 1 },
            { ["--local-shadows", "off"], new NoLocalShadows(), 1 },
            {
                // The renderer is real, so its tiles stay within the 256 layers that Vulkan guarantees: 12 tiles, 12 MB.
                ["--local-shadow-tiles", "12", "--cascaded-lights", "2"],
                LocalShadowAtlas.Default with
                {
                    Tiles = 12,
                },
                2
            },
        };

    [Theory]
    [MemberData(nameof(ShadowOptions))]
    public void TheShadowOptionsChooseTheShadowsOfTheHeadlessRendererWhichAreTheRenderersDefaultWithoutThem(
        string[] shadows,
        LocalShadowSetting expectedLocalShadows,
        int expectedCascadedLights
    )
    {
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );
        var chosen = new List<(LocalShadowSetting LocalShadows, int CascadedLights)>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, .. shadows],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                chosen.Add((options.LocalShadows, options.CascadedLights));
                return VulkanRenderer.CreateHeadless(options);
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        chosen.Should().Equal((expectedLocalShadows, expectedCascadedLights));
    }

    [Fact]
    public void ARendererLikeAScriptedRunsExposesLightsAndShadowsTheSceneAsTheHeadlessRendererOfAScriptedRunDoes()
    {
        // The Host tests read a demo scene through GpuTesting.LikeAScriptedRun, so its light must reach each surface as
        // in a scripted run: a lamp that casts shadows must not light the far side of a wall in one and not the other.
        string script = ScriptFile(
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }"""
        );
        var chosen = new List<(object? Exposure, object? EnvironmentMap, object? LocalShadows, int CascadedLights)>();

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                chosen.Add(
                    (
                        options.Exposure.Value,
                        options.EnvironmentMap.Value,
                        options.LocalShadows.Value,
                        options.CascadedLights
                    )
                );
                return VulkanRenderer.CreateHeadless(options);
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        RendererOptions like = LikeAScriptedRunOptions(width: 160, height: 90);
        chosen
            .Should()
            .Equal((like.Exposure.Value, like.EnvironmentMap.Value, like.LocalShadows.Value, like.CascadedLights));
    }
}
