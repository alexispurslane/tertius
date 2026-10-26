using System.Globalization;
using System.Text;
using Tertius.Demo;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The counts of the cost of the shadows of every light (docs/plans/local-light-shadows.md, "Cost"), which the GPU
/// timings of a headless run leave out: for each lamp hall and each star scene, at 1920x1080 with a scripted run's
/// options and the cost's budget of tiles, the <see cref="RenderStats"/> of the first frame after 10 ticks and of the third
/// frame after it, one tick apart: the lights, the tiles in use and drawn, the casters, the cascade sets and the shadow
/// commands. It is an explicit test, so a normal run reports it as not run. It runs in a Release build, with the variable
/// <see cref="Variable"/> set to the file of its report:
/// <c>TERTIUS_LOCAL_SHADOW_COUNTS=report.md dotnet test -c Release --project tests/Tertius.Host.Tests -- --filter-class "*LocalShadowCountsMeasurement" --explicit only</c>
/// .
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class LocalShadowCountsMeasurement
{
    public const string Variable = "TERTIUS_LOCAL_SHADOW_COUNTS";

    /// <summary>Each scene of the cost, with its budget of tiles and its far lights with cascade sets.</summary>
    private static readonly (string Scene, int Tiles, int CascadedLights)[] Runs =
    [
        .. new[] { 4, 16, 64 }.SelectMany(lamps =>
            new[] { "", "-spot", "-moving", "-spot-moving" }.Select(variant =>
                (
                    Scene: $"lamp-hall-{lamps}{variant}",
                    Tiles: lamps == 64 && !variant.Contains("spot", StringComparison.Ordinal) ? 384 : 64,
                    CascadedLights: 1
                )
            )
        ),
        .. new[] { 1, 2, 4 }.SelectMany(stars =>
            new[] { 0, 1, 2, 4 }.Select(cascaded => (Scene: $"stars-{stars}", Tiles: 64, CascadedLights: cascaded))
        ),
    ];

    [Fact(Explicit = true)]
    public void WritesItsReportWhereTheVariableNamesAFile()
    {
        string reportPath = Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path
            ? path
            : throw new InvalidOperationException($"Set {Variable} to the file of the report.");
        var report = new StringBuilder();
        report.AppendLine(
            "| Scene | Tiles | Cascaded lights | Frame | Lights | Tiles in use | Tiles drawn | Casters | Cascade sets | Shadow commands | Draw calls |"
        );
        report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach ((string sceneName, int tiles, int cascadedLights) in Runs)
        {
            using var renderer = VulkanRenderer.CreateHeadless(
                new()
                {
                    Width = 1920,
                    Height = 1080,
                    Impostors = false,
                    LocalShadows = LocalShadowAtlas.Default with { Tiles = tiles },
                    CascadedLights = cascadedLights,
                }
            );
            using World world = NewJoltWorld();
            DemoScenes.All.Single(s => s.Name == sceneName).Build(world, renderer);
            for (int i = 0; i < 10; i++)
                world.Tick();
            for (int frame = 1; frame <= 3; frame++)
            {
                world.PublishRenderSnapshot();
                renderer.Render(world.RenderSnapshots.AcquireLatest() ?? world.RenderSnapshots.ReadBuffer, alpha: 1);
                RenderStats stats = renderer.LastFrameStats;
                if (frame != 2)
                    report.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"| {sceneName} | {tiles} | {cascadedLights} | {frame} | {stats.Lights} | {stats.ShadowTilesInUse} | {stats.ShadowTilesDrawn} | {stats.ShadowCasters} | {stats.CascadeSets} | {stats.ShadowCommands} | {stats.IndirectDrawCalls} |"
                    );
                world.Tick();
            }
        }

        File.WriteAllText(reportPath, report.ToString());
    }
}
