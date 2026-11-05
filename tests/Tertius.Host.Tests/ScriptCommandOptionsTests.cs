using System.Text.RegularExpressions;
using Tertius.Host.Scripting;
using Tertius.Rendering;

namespace Tertius.Host.Tests;

/// <summary>
/// The three cases of the command line of <see cref="ScriptCommand"/>, free play, a script run in the window and a headless
/// script run: the options that each case takes, and the exact problem with an option that a case does not take.
/// </summary>
public sealed class ScriptCommandOptionsTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void AnExplicitInFlightCountReplacesAThreeFrameRenderer(int count, bool captures)
    {
        var renderer = new RendererOptions { FramesInFlight = 3, CapturesInFlight = true };
        var overrides = new ScriptCommand.RendererOverrides { FramesInFlight = count };

        overrides.ApplyTo(renderer);

        renderer.FramesInFlight.Should().Be(count);
        renderer.CapturesInFlight.Should().Be(captures);
    }

    // Each option of the headless renderer, with a value that a headless run takes.
    private static readonly string[][] RendererOptions =
    [
        ["--tone-curve", "hable"],
        ["--screen-trace", "off"],
        ["--bloom", "off"],
        ["--anti-aliasing", "smaa"],
        ["--local-shadows", "off"],
        ["--local-shadow-tiles", "8"],
        ["--cascaded-lights", "2"],
        ["--environment-map", "off"],
        ["--subsurface", "off"],
        ["--in-flight", "2"],
    ];

    public static TheoryData<string[]> AcceptedCommandLines =>
        new()
        {
            { ["--scene", "drop-box"] },
            { ["--scene", "drop-box", "--speed", "2", "--record", "r.json"] },
            { ["--script", "s.json", "--out", "o"] },
            { ["--script", "s.json", "--out", "o", "--speed", "0.5", "--size", "800x600"] },
            { ["--script", "s.json", "--headless", "--out", "o"] },
            { ["--script", "s.json", "--headless", "--out", "o", "--video", "none", "--size", "800x600"] },
            {
                [
                    "--script",
                    "s.json",
                    "--headless",
                    "--out",
                    "o",
                    "--video",
                    "v.mkv",
                    "--fps",
                    "24",
                    "--size",
                    "800x600",
                    "--tone-curve",
                    "hable",
                    "--screen-trace",
                    "off",
                    "--bloom",
                    "off",
                    "--anti-aliasing",
                    "off",
                    "--local-shadows",
                    "on",
                    "--local-shadow-tiles",
                    "8",
                    "--cascaded-lights",
                    "2",
                    "--environment-map",
                    "off",
                    "--subsurface",
                    "off",
                    "--in-flight",
                    "2",
                ]
            },
        };

    [Theory]
    [MemberData(nameof(AcceptedCommandLines))]
    public void EachCaseTakesEveryOptionThatBelongsToIt(string[] args)
    {
        ScriptCommand.ReadOptions(args, out string? problem);

        problem.Should().BeNull();
    }

    public static TheoryData<string[], string> RefusedChoicesAndFreePlays =>
        new()
        {
            { [], "Give --script or --scene." },
            { ["--script", "s.json", "--scene", "drop-box", "--out", "o"], "Give --script or --scene, not both." },
            { ["--script", "s.json"], "The option --out is missing." },
            { ["--script", "s.json", "--headless"], "The option --out is missing." },
            {
                ["--scene", "drop-box", "--headless"],
                "--scene is free play, and free play needs the window. Leave out --headless."
            },
            {
                ["--scene", "drop-box", "--out", "o"],
                "--out applies to a script. Free play writes only the file that --record names."
            },
            { ["--scene", "drop-box", "--size", "800x600"], "--size applies to a script, so give --script." },
            {
                ["--scene", "drop-box", "--video", "v.mkv"],
                "--video applies to a headless run. The window writes PNG files, so add --headless or leave out --video."
            },
            { ["--scene", "drop-box", "--fps", "30"], "--fps applies to a video, so give --video too." },
        };

    public static TheoryData<string[], string> RefusedWindowRuns =>
        new()
        {
            {
                ["--script", "s.json", "--out", "o", "--record", "r.json"],
                "--record applies to free play, so give --scene. A script is a record already."
            },
            {
                ["--script", "s.json", "--out", "o", "--video", "v.mkv"],
                "--video applies to a headless run. The window writes PNG files, so add --headless or leave out --video."
            },
            {
                ["--script", "s.json", "--out", "o", "--video", "none"],
                "--video applies to a headless run. The window writes PNG files, so add --headless or leave out --video."
            },
            {
                ["--script", "s.json", "--out", "o", "--video", "v.mkv", "--fps", "30"],
                "--video applies to a headless run. The window writes PNG files, so add --headless or leave out --video."
            },
            { ["--script", "s.json", "--out", "o", "--fps", "30"], "--fps applies to a video, so give --video too." },
        };

    public static TheoryData<string[], string> RefusedHeadlessRuns =>
        new()
        {
            {
                ["--script", "s.json", "--headless", "--out", "o", "--record", "r.json"],
                "--record applies to free play, so give --scene. A script is a record already."
            },
            {
                ["--script", "s.json", "--headless", "--out", "o", "--speed", "2"],
                "--speed applies to the window. A headless run runs its ticks back to back."
            },
            {
                ["--script", "s.json", "--headless", "--out", "o", "--fps", "30"],
                "--fps applies to a video, so give --video too."
            },
            {
                ["--script", "s.json", "--headless", "--out", "o", "--video", "none", "--fps", "30"],
                "--fps sets the frame rate of a video file, and --video none writes no video."
            },
            {
                [
                    "--script",
                    "s.json",
                    "--headless",
                    "--out",
                    "o",
                    "--local-shadows",
                    "off",
                    "--local-shadow-tiles",
                    "4",
                ],
                "--local-shadow-tiles sets the budget of local shadows, so leave out --local-shadows off."
            },
        };

    [Theory]
    [MemberData(nameof(RefusedChoicesAndFreePlays))]
    [MemberData(nameof(RefusedWindowRuns))]
    [MemberData(nameof(RefusedHeadlessRuns))]
    public void ACommandLineThatNoCaseTakesIsRefusedWithItsProblem(string[] args, string expected)
    {
        ScriptCommand.ReadOptions(args, out string? problem);

        problem.Should().Be(expected);
    }

    // Command lines with two or more problems. Each gets the problem of the option that comes first in the order of the
    // checks: the options that choose the run (--headless, --record, --out), then the missing --out of a script run, then
    // each other option in the order of the usage line, with its place in the run before its value.
    public static TheoryData<string[], string> CommandLinesWithSeveralProblems =>
        new()
        {
            {
                ["--script", "s.json", "--record", "r.json"],
                "--record applies to free play, so give --scene. A script is a record already."
            },
            {
                ["--scene", "drop-box", "--headless", "--out", "o"],
                "--scene is free play, and free play needs the window. Leave out --headless."
            },
            {
                ["--script", "s.json", "--out", "o", "--size", "big", "--tone-curve", "hable"],
                $"--size needs a width and a height of 1 to {ScriptWindow.MaxSide} pixels, such as 1920x1080, not 'big'."
            },
            {
                ["--scene", "drop-box", "--speed", "0", "--video", "v.mkv"],
                "--speed needs a positive number, such as 0.5 or 2, not '0'."
            },
            {
                ["--script", "s.json", "--out", "o", "--speed", "0", "--video", "v.mkv"],
                "--speed needs a positive number, such as 0.5 or 2, not '0'."
            },
            {
                ["--script", "s.json", "--headless", "--out", "o", "--video", "v.txt", "--speed", "2"],
                "--speed applies to the window. A headless run runs its ticks back to back."
            },
        };

    [Theory]
    [MemberData(nameof(CommandLinesWithSeveralProblems))]
    public void ACommandLineWithSeveralProblemsGetsTheProblemOfTheOptionThatIsCheckedFirst(
        string[] args,
        string expected
    )
    {
        ScriptCommand.ReadOptions(args, out string? problem);

        problem.Should().Be(expected);
    }

    // Each case of the command line, and the options that it takes. --headless chooses the display of a script run, so a
    // script run in the window counts it as taken.
    public static TheoryData<string[], string[]> CasesAndTheirOptions =>
        new()
        {
            { ["--scene", "drop-box"], ["--scene", "--speed", "--record"] },
            { ["--script", "s.json", "--out", "o"], ["--script", "--out", "--speed", "--size", "--headless"] },
            {
                ["--script", "s.json", "--headless", "--out", "o"],
                [
                    "--script",
                    "--headless",
                    "--out",
                    "--video",
                    "--fps",
                    "--size",
                    .. RendererOptions.Select(option => option[0]),
                ]
            },
        };

    [Theory]
    [MemberData(nameof(CasesAndTheirOptions))]
    public void EveryOptionOfTheUsageLineIsKnownAndACaseThatDoesNotTakeItRefusesIt(string[] run, string[] taken)
    {
        var usage = new StringWriter();
        ScriptCommand.Run(args: [], scenes: [], output: TextWriter.Null, error: usage);
        string[] options = [.. Regex.Matches(usage.ToString(), "--[a-z-]+").Select(match => match.Value).Distinct()];

        foreach (string option in options.Except(taken))
        {
            string[] given = option == "--headless" ? [option] : [option, "x"];
            ScriptCommand.ReadOptions([.. run, .. given], out string? problem);

            problem.Should().NotBeNull($"{option} does not belong to {string.Join(' ', run)}");
            problem.Should().NotStartWith("Unknown option", $"the usage line names {option}");
        }
    }

    public static TheoryData<string[], string> RendererOptionsOutsideAHeadlessRun()
    {
        var rows = new TheoryData<string[], string>();
        foreach (string[] option in RendererOptions)
        {
            string expected = $"{option[0]} applies to a headless run, so add --headless.";
            rows.Add(["--scene", "drop-box", .. option], expected);
            rows.Add(["--script", "s.json", "--out", "o", .. option], expected);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(RendererOptionsOutsideAHeadlessRun))]
    public void AnOptionOfTheHeadlessRendererIsRefusedInFreePlayAndInTheWindow(string[] args, string expected)
    {
        ScriptCommand.ReadOptions(args, out string? problem);

        problem.Should().Be(expected);
    }

    [Fact]
    public void FreePlayWithoutOptionsRunsItsSceneAtRealTimeAndRecordsNothing()
    {
        ScriptCommand.RunOptions? options = ScriptCommand.ReadOptions(["--scene", "drop-box"], out _);

        options
            .Should()
            .Be(new ScriptCommand.RunOptions(new ScriptCommand.FreePlayRun("drop-box", Speed: 1, Record: null)));
    }

    [Fact]
    public void FreePlayCarriesItsSpeedAndTheFileThatItRecordsTo()
    {
        ScriptCommand.RunOptions? options = ScriptCommand.ReadOptions(
            ["--scene", "drop-box", "--record", "sessions/today.json", "--speed", "2"],
            out _
        );

        options
            .Should()
            .Be(
                new ScriptCommand.RunOptions(
                    new ScriptCommand.FreePlayRun("drop-box", Speed: 2, Record: "sessions/today.json")
                )
            );
    }

    [Fact]
    public void AScriptRunWithoutHeadlessRunsInTheWindowAtRealTimeAndKeepsTheScriptsSize()
    {
        ScriptCommand.RunOptions? options = ScriptCommand.ReadOptions(["--script", "s.json", "--out", "o"], out _);

        options
            .Should()
            .Be(
                new ScriptCommand.RunOptions(
                    new ScriptCommand.ScriptRun(
                        Script: "s.json",
                        Out: "o",
                        Size: null,
                        Display: new ScriptCommand.InWindow(Speed: 1)
                    )
                )
            );
    }

    [Fact]
    public void AScriptRunInTheWindowCarriesItsSpeedAndItsSize()
    {
        ScriptCommand.RunOptions? options = ScriptCommand.ReadOptions(
            ["--script", "s.json", "--out", "o", "--speed", "0.5", "--size", "800x600"],
            out _
        );

        options
            .Should()
            .Be(
                new ScriptCommand.RunOptions(
                    new ScriptCommand.ScriptRun(
                        Script: "s.json",
                        Out: "o",
                        Size: new ScriptWindow(800, 600),
                        Display: new ScriptCommand.InWindow(Speed: 0.5)
                    )
                )
            );
    }

    [Fact]
    public void AHeadlessScriptRunWithoutOptionsWritesPngFilesIntoItsOutputFolderAndKeepsTheRenderersDefaults()
    {
        ScriptCommand.RunOptions? options = ScriptCommand.ReadOptions(
            ["--script", "s.json", "--headless", "--out", "o"],
            out _
        );

        options
            .Should()
            .Be(
                new ScriptCommand.RunOptions(
                    new ScriptCommand.ScriptRun(
                        Script: "s.json",
                        Out: "o",
                        Size: null,
                        Display: new ScriptCommand.Headless(
                            Video: null,
                            Renderer: new ScriptCommand.RendererOverrides()
                        )
                    )
                )
            );
    }

    [Fact]
    public void AHeadlessScriptRunCarriesItsSizeAndEveryOverrideOfTheRenderer()
    {
        ScriptCommand.RunOptions? options = ScriptCommand.ReadOptions(
            [
                "--script",
                "s.json",
                "--headless",
                "--out",
                "o",
                "--size",
                "800x600",
                "--tone-curve",
                "hable",
                "--screen-trace",
                "off",
                "--bloom",
                "off",
                "--anti-aliasing",
                "off",
                "--local-shadows",
                "on",
                "--local-shadow-tiles",
                "8",
                "--cascaded-lights",
                "2",
                "--environment-map",
                "off",
                "--subsurface",
                "off",
                "--in-flight",
                "2",
            ],
            out _
        );

        var renderer = new ScriptCommand.RendererOverrides
        {
            ToneCurve = ToneCurve.Hable,
            ScreenTraceOff = true,
            BloomOff = true,
            AntiAliasing = AntiAliasing.None,
            LocalShadows = LocalShadowAtlas.Default with { Tiles = 8 },
            CascadedLights = 2,
            EnvironmentMapOff = true,
            SubsurfaceOff = true,
            FramesInFlight = 2,
        };
        options
            .Should()
            .Be(
                new ScriptCommand.RunOptions(
                    new ScriptCommand.ScriptRun(
                        Script: "s.json",
                        Out: "o",
                        Size: new ScriptWindow(800, 600),
                        Display: new ScriptCommand.Headless(Video: null, Renderer: renderer)
                    )
                )
            );
    }

    [Fact]
    public void AHeadlessRunWithoutVideoWritesPngFilesIntoTheOutputFolderOfItsScriptRun()
    {
        var headless = new ScriptCommand.Headless(Video: null, Renderer: new ScriptCommand.RendererOverrides());

        headless.TargetIn("o").Should().Be(new CaptureTarget(new PngFolder("o")));
    }

    [Fact]
    public void AHeadlessRunWithVideoSendsItsCapturesToTheVideoAndNotToTheOutputFolder()
    {
        var filmed = new ScriptCommand.Headless(
            Video: new ScriptCommand.HeadlessVideo(new VideoFile("v.mkv", 24)),
            Renderer: new ScriptCommand.RendererOverrides()
        );
        var discarded = new ScriptCommand.Headless(
            Video: new ScriptCommand.HeadlessVideo(new Discard()),
            Renderer: new ScriptCommand.RendererOverrides()
        );

        filmed.TargetIn("o").Should().Be(new CaptureTarget(new VideoFile("v.mkv", 24)));
        discarded.TargetIn("o").Should().Be(new CaptureTarget(new Discard()));
    }
}
