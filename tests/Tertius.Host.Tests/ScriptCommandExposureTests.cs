using System.Text.Json;
using Tertius.Rendering;
using static Tertius.Host.Tests.GpuTesting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The exposure file of a headless run with a renderer (<see cref="ScriptCommand.ExposureFile"/>): one entry per shown
/// frame, in the order shown, with the exposure in stops that the frame's passes used.
/// </summary>
public sealed class ScriptCommandExposureTests : IDisposable
{
    private readonly string folder = NewFolder();
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string ScriptFile(string name, string json)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>The entries of the exposure file in <paramref name="outFolder"/>.</summary>
    private static (int Frame, string Capture, float Stops)[] Readings(string outFolder)
    {
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(outFolder, ScriptCommand.ExposureFile)));
        return
        [
            .. file
                .RootElement.EnumerateArray()
                .Select(entry =>
                    (
                        entry.GetProperty("frame").GetInt32(),
                        entry.GetProperty("capture").GetString()!,
                        entry.GetProperty("stops").GetSingle()
                    )
                ),
        ];
    }

    [Fact]
    public void AHeadlessFilmUnderAutomaticExposureWritesTheExposureOfEveryShownFrameMovingTowardTheBrightView()
    {
        string settle = ScriptFile(
            "settle.json",
            """
            { "scene": "dark-and-bright", "window": { "width": 160, "height": 90 },
              "steps": [ { "do": "camera", "entity": "bright" }, { "do": "capture", "name": "settled" } ] }
            """
        );
        string film = ScriptFile(
            "film.json",
            """
            { "scene": "dark-and-bright", "window": { "width": 160, "height": 90 },
              "steps": [ { "do": "film", "name": "dark", "frames": 1, "every": 1 }, { "do": "camera", "entity": "bright" },
                         { "do": "film", "name": "bright", "frames": 4, "every": 30 } ] }
            """
        );
        string settledOut = Path.Combine(folder, "settled"),
            filmOut = Path.Combine(folder, "film");

        int settleCode = ScriptCommand.Run(
            args: ["--script", settle, "--headless", "--out", settledOut],
            scenes: [DarkAndBrightScene],
            output: output,
            error: error
        );
        int filmCode = ScriptCommand.Run(
            args: ["--script", film, "--headless", "--out", filmOut],
            scenes: [DarkAndBrightScene],
            output: output,
            error: error
        );

        (settleCode, filmCode).Should().Be((ScriptCommand.Ok, ScriptCommand.Ok), error.ToString());
        // The first frame of a run settles on its own view (docs/plans/auto-exposure.md, "The metering draw").
        float bright = Readings(settledOut).Single().Stops;
        (int Frame, string Capture, float Stops)[] readings = Readings(filmOut);
        readings
            .Select(reading => reading.Capture)
            .Should()
            .Equal(["dark-000000", "bright-000000", "bright-000001", "bright-000002", "bright-000003"]);
        float[] stops = [.. readings.Select(reading => reading.Stops)];
        stops
            .Should()
            .BeInDescendingOrder("each frame of the bright view adapts further into the light")
            .And.OnlyHaveUniqueItems("each frame of the bright view adapts for 0.5 s")
            .And.AllSatisfy(each => each.Should().BeGreaterThan(bright, "the approach never passes its target"));
        (stops[0] - bright).Should().BeGreaterThan(3, "the dark box is about 5 stops darker than the bright one");
    }

    [Fact]
    public void AHeadlessFilmUnderAFixedExposureWritesTheFixedStopsOfEveryShownFrameAndPrintsThePath()
    {
        string script = ScriptFile(
            "film.json",
            """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 3, "every": 1 } ] }"""
        );
        string outFolder = Path.Combine(folder, "out");

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", outFolder],
            scenes: [LitBoxScene],
            output: output,
            error: error,
            createRenderer: options =>
            {
                options.Exposure = new FixedExposure(1.5f);
                return VulkanRenderer.CreateHeadless(options);
            }
        );

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        Readings(outFolder).Should().Equal([(0, "box-000000", 1.5f), (1, "box-000001", 1.5f), (2, "box-000002", 1.5f)]);
        output.ToString().Should().Contain(Path.Combine(outFolder, ScriptCommand.ExposureFile));
    }
}
