using Tertius.Host.Scripting;
using Tertius.Rendering;

namespace Tertius.Host.Tests;

/// <summary>
/// The three cases of the command line of <see cref="ScriptCommand"/>, free play, a script run in the window and a headless
/// script run: the options that each case takes, and the exact problem with an option that a case does not take.
/// </summary>
public sealed class ScriptCommandOptionsTests
{
    // Each option of the headless renderer, with a value that a headless run takes.
    private static readonly string[][] RendererOptions =
    [
        ["--tone-curve", "hable"],
        ["--screen-space-light", "0.5"],
        ["--screen-space-light-steps", "4"],
        ["--bloom", "off"],
        ["--reflections", "off"],
        ["--anti-aliasing", "smaa"],
        ["--local-shadows", "off"],
        ["--local-shadow-tiles", "8"],
        ["--cascaded-lights", "2"],
        ["--environment-map", "off"],
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
                    "--screen-space-light",
                    "0.5",
                    "--screen-space-light-steps",
                    "4",
                    "--bloom",
                    "off",
                    "--reflections",
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
                    "--screen-space-light",
                    "off",
                    "--screen-space-light-steps",
                    "4",
                ],
                "--screen-space-light-steps sets the steps of the estimate, so leave out --screen-space-light off."
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
            .Be(new ScriptCommand.RunOptions(new ScriptCommand.FreePlay("drop-box", Speed: 1, Record: null)));
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
                    new ScriptCommand.FreePlay("drop-box", Speed: 2, Record: "sessions/today.json")
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
                            Target: new PngFolder("o"),
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
                "--screen-space-light",
                "0.5",
                "--screen-space-light-steps",
                "4",
                "--bloom",
                "off",
                "--reflections",
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
            ],
            out _
        );

        var renderer = new ScriptCommand.RendererOverrides
        {
            ToneCurve = ToneCurve.Hable,
            ScreenSpaceLight = VisibilityBitmaskLight.Default with { RadiusMeters = 0.5f, StepsPerSide = 4 },
            BloomOff = true,
            ReflectionsOff = true,
            AntiAliasing = AntiAliasing.None,
            LocalShadows = LocalShadowAtlas.Default with { Tiles = 8 },
            CascadedLights = 2,
            EnvironmentMapOff = true,
        };
        options
            .Should()
            .Be(
                new ScriptCommand.RunOptions(
                    new ScriptCommand.ScriptRun(
                        Script: "s.json",
                        Out: "o",
                        Size: new ScriptWindow(800, 600),
                        Display: new ScriptCommand.Headless(Target: new PngFolder("o"), Renderer: renderer)
                    )
                )
            );
    }
}
