using System.Numerics;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The reflection kind's guided share on the GPU (ARCHITECTURE.md, "Guided rays", <see cref="GuidedShares"/>): the share
/// pass against its C# copy, its restart at a camera cut, where it settles, and a glossy floor's reflection of a glowing
/// panel with the guided reflection rays against the lobe alone, through the debug switch TERTIUS_GUIDED_RAYS
/// (<see cref="LightPyramidPass.SwitchedOn"/>). The camera of <see cref="Gpu.Snapshot"/> stands still, and each frame
/// steps one tick.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ReflectionShareRenderTests
{
    private const string Switch = "TERTIUS_GUIDED_RAYS";

    private const int Width = 160,
        Height = 90;

    /// <summary>The blocks across the render size and down it, and the tiles of the reflection share.</summary>
    private const int BlocksWide = (Width + 1) / 2,
        BlocksHigh = (Height + 1) / 2,
        TilesWide = (BlocksWide + ScreenTraceImages.TileBlocks - 1) / ScreenTraceImages.TileBlocks,
        TilesHigh = (BlocksHigh + ScreenTraceImages.TileBlocks - 1) / ScreenTraceImages.TileBlocks;

    /// <summary>The step of each frame after the first: one tick, which continues the last frame.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>The glow of the panel.</summary>
    private const float PanelGlow = 40;

    /// <summary>The roughness of the glossy floor: above the ramp's end, so its tiles' shares act in full.</summary>
    private const float Glossy = 0.4f;

    /// <summary>The center of the glowing panel, 0.6 m over the floor, which stands 1 cm out of the wall.</summary>
    private static readonly Vector3 PanelCenter = new(0.8f, FloorHeight + 0.6f, -6.99f);

    [Fact]
    public void AfterTheFirstDrawEachTilesShareIsOneStepFromTheCeilingByItsBlocksEvidence() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = PanelOverFloor(Glossy);
                try
                {
                    scene.Time += Tick;
                    renderer.Render(scene, 1f);

                    (int moved, List<string> mismatched) = CompareShares(renderer, last: null);
                    mismatched.Should().BeEmpty();
                    moved.Should().BePositive("the glossy floor's rays bring evidence to its tiles");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void EachLaterDrawMovesEveryTilesShareOneStepFromItsLastShare() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = PanelOverFloor(Glossy);
                try
                {
                    scene.Time += Tick;
                    renderer.Render(scene, 1f);
                    float[] last = renderer.ReadImage(renderer.ScreenTraceImages.ReflectionShare);
                    scene.Time += Tick;
                    renderer.Render(scene, 1f);

                    (int moved, List<string> mismatched) = CompareShares(renderer, last);
                    mismatched.Should().BeEmpty();
                    moved.Should().BePositive();
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void ACameraCutRestartsEveryTilesShareAtTheCeiling() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = SunlitFloorAlone();
                try
                {
                    for (int frame = 0; frame < 24; frame++)
                    {
                        scene.Time += Tick;
                        renderer.Render(scene, 1f);
                    }
                    float[] before = renderer.ReadImage(renderer.ScreenTraceImages.ReflectionShare);
                    scene.CameraCuts++;
                    scene.Time += Tick;
                    renderer.Render(scene, 1f);

                    before
                        .Should()
                        .Contain(share => share < GuidedShares.ReflectionCeiling / 2, "the floor's shares fell before the cut");
                    (_, List<string> mismatched) = CompareShares(renderer, last: null);
                    mismatched.Should().BeEmpty("the draw after the cut starts from the ceiling, not from the shares before it");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void OnASunlitFloorWhoseGuideHoldsOnlyItsOwnPlaneTheFloorsSharesFallToZero() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = SunlitFloorAlone();
                try
                {
                    for (int frame = 0; frame < 60; frame++)
                    {
                        scene.Time += Tick;
                        renderer.Render(scene, 1f);
                    }
                    float[] shares = renderer.ReadImage(renderer.ScreenTraceImages.ReflectionShare);

                    // The tiles below the horizon, which lies in the render's middle row, hold only floor.
                    float[] floor = [.. shares.Skip(TilesWide * (TilesHigh / 2 + 1))];
                    floor.Should().NotBeEmpty();
                    floor
                        .Should()
                        .OnlyContain(
                            share => share == 0,
                            "a guided draw at the floor's own plane brings nothing, so a larger share only adds variance"
                        );
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void OnAGlossyFloorBelowAGlowingPanelTheTilesOfThePanelsReflectionKeepTheCeiling() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = PanelOverFloor(Glossy);
                try
                {
                    for (int frame = 0; frame < 60; frame++)
                    {
                        scene.Time += Tick;
                        renderer.Render(scene, 1f);
                    }
                    float[] shares = renderer.ReadImage(renderer.ScreenTraceImages.ReflectionShare);

                    (int x, int y) = MirrorPixel(PanelCenter, Width, Height);
                    float share = shares[y / 16 * TilesWide + x / 16];
                    share
                        .Should()
                        .BeGreaterThan(
                            0.8f * GuidedShares.ReflectionCeiling,
                            "the lobe alone rarely finds the small panel, which the guided draws find"
                        );
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void ASmoothFloorsReflectionsAreThoseOfItsLobeAlone()
    {
        float[] guided = WithSwitch(null, () => FirstSpecularSample(roughness: 0.05f));
        float[] lobe = WithSwitch("off", () => FirstSpecularSample(roughness: 0.05f));

        (_, int wallBottom) = PixelOf(new(PanelCenter.X, FloorHeight, -7), Width, Height);
        int floorStart = 4 * Width * (wallBottom + 2);
        guided[floorStart..]
            .Should()
            .Equal(lobe[floorStart..], "a roughness below the ramp's start gives a share of 0, and the lobe draws as without guiding");
    }

    [Fact]
    public void TwoRunsOfTheSameDrawsGiveTheSameShares()
    {
        float[] first = WithSwitch(null, () => SharesAfter(frames: 12));
        float[] second = WithSwitch(null, () => SharesAfter(frames: 12));

        first.Should().Equal(second);
    }

    [Theory]
    [InlineData(0, "a share of 1/4 gives each cell exactly one guided draw in each window of 4 draws")]
    [InlineData(1, "a share of 1/8 gives each cell exactly one guided draw in each pair of windows")]
    [InlineData(2, "a share of 1/2 gives each cell exactly two guided draws in each window")]
    public void AReflectionCellTakesTheGuidedDrawOnAnExactCount(int channel, string reason)
    {
        float[] turns = GuidedTurns();

        Enumerable.Range(0, Width * Height).Select(pixel => turns[4 * pixel + channel]).Should().OnlyContain(v => v == 1, reason);
    }

    [Fact]
    public void InEachDrawExactlyOneCellOfEachGroupOf2x2CellsTakesTheGuidedDrawAtAQuarter()
    {
        float[] turns = GuidedTurns();

        for (int y = 0; y < Height; y += 2)
        for (int x = 0; x < Width; x += 2)
        {
            float guided = 0;
            for (int cell = 0; cell < 4; cell++)
                guided += turns[4 * ((y + (cell >> 1)) * Width + x + (cell & 1)) + 3];
            guided.Should().Be(1, "the group at ({0}, {1}) holds one cell of each slot, so the frame has no beat", x, y);
        }
    }

    /// <summary>
    /// The glossy floor's reflection of the glowing panel over many frames, with the guided reflection rays and with the
    /// lobe alone, which runs for seconds (tests/STYLE.md, "Long tests").
    /// </summary>
    [Collection(ProcessEnvironmentCollection.Name)]
    public sealed class PanelsReflectionOverManyFrames
    {
        [Fact]
        public void AGlossyFloorsReflectionOfAGlowingPanelKeepsItsMeanAndLosesNoiseWithTheGuidedReflectionRays()
        {
            const int frames = 96,
                skipped = 16;
            bool[] floor = FloorBelowPanel();
            (double guidedMean, double guidedError, double guidedVariance) = WithSwitch(
                null,
                () => Reflection(floor: floor, frames: frames, skipped: skipped)
            );
            (double lobeMean, double lobeError, double lobeVariance) = WithSwitch(
                "off",
                () => Reflection(floor: floor, frames: frames, skipped: skipped)
            );

            double error = Math.Sqrt(guidedError * guidedError + lobeError * lobeError);
            guidedMean
                .Should()
                .BeApproximately(lobeMean, Math.Max(4 * error, 0.03 * lobeMean), $"the standard error is {error}");
            guidedVariance
                .Should()
                .BeLessThan(0.7 * lobeVariance, $"the guided rays find the panel in most frames ({guidedMean}, {lobeMean})");
        }
    }

    /// <summary>
    /// Compares the reflection share after the last frame with the C# copy of the share pass: each tile one step from
    /// <paramref name="last"/>, or from the ceiling for null, by the sums of its blocks' evidence in the block guide, in
    /// rows from the top. Gives how many tiles moved, and a line for each tile that does not match.
    /// </summary>
    private static (int Moved, List<string> Mismatched) CompareShares(VulkanRenderer renderer, float[]? last)
    {
        float[] guide = renderer.ReadImage(renderer.ScreenTraceImages.BlockGuide);
        float[] shares = renderer.ReadImage(renderer.ScreenTraceImages.ReflectionShare);
        shares.Should().HaveCount(TilesWide * TilesHigh);
        var mismatched = new List<string>();
        int moved = 0;
        for (int tileY = 0; tileY < TilesHigh; tileY++)
        for (int tileX = 0; tileX < TilesWide; tileX++)
        {
            float slope = 0,
                moment = 0;
            for (int y = 8 * tileY; y < Math.Min(8 * tileY + 8, BlocksHigh); y++)
            for (int x = 8 * tileX; x < Math.Min(8 * tileX + 8, BlocksWide); x++)
            {
                slope += guide[4 * (y * BlocksWide + x) + 1];
                moment += guide[4 * (y * BlocksWide + x) + 2];
            }
            int tile = tileY * TilesWide + tileX;
            float before = last?[tile] ?? GuidedShares.ReflectionCeiling;
            float expected = GuidedShares.NextShare(before, slope, moment);
            if (Math.Abs(shares[tile] - expected) > 1e-6f)
                mismatched.Add($"({tileX}, {tileY}): {shares[tile]} against {expected}");
            if (shares[tile] != before)
                moved++;
        }
        return (moved, mismatched);
    }

    /// <summary>The specular sample of the first frame of the panel over a floor of <paramref name="roughness"/>.</summary>
    private static float[] FirstSpecularSample(float roughness)
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = PanelOverFloor(roughness);
        try
        {
            scene.Time += Tick;
            renderer.Render(scene, 1f);
            return renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The probe guided_turn_probe.comp at each pixel of a frame of the panel over the glossy floor.</summary>
    private static float[] GuidedTurns()
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = PanelOverFloor(Glossy);
        try
        {
            var probe = ScreenTraceProbe.On(renderer, "guided_turn_probe.comp");
            scene.Time += Tick;
            renderer.Render(scene, 1f);
            return probe.Read();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The reflection share after <paramref name="frames"/> frames of the panel over the glossy floor.</summary>
    private static float[] SharesAfter(int frames)
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = PanelOverFloor(Glossy);
        try
        {
            for (int frame = 0; frame < frames; frame++)
            {
                scene.Time += Tick;
                renderer.Render(scene, 1f);
            }
            return renderer.ReadImage(renderer.ScreenTraceImages.ReflectionShare);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// No sun and no ambient light, a white wall 7 m ahead with a glowing panel on it, 0.6 m wide and 0.4 m high, and a
    /// white metal floor of <paramref name="roughness"/> 1.5 m below the eye.
    /// </summary>
    private static (VulkanRenderer Renderer, RenderSnapshot Scene) PanelOverFloor(float roughness)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        RenderSnapshot scene = Dark();
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        scene.Instances.Add(Floor(renderer, Mirror(roughness)));
        scene.Instances.Add(
            Block(
                box: box,
                material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 1f)),
                center: new(0, 2, -7.5f),
                size: new(30, 7, 1)
            )
        );
        scene.Instances.Add(
            Block(box: box, material: Glow(renderer, new(PanelGlow)), center: PanelCenter, size: new(0.6f, 0.4f, 0.02f))
        );
        return (renderer, scene);
    }

    /// <summary>
    /// A white glossy dielectric floor alone under a high sun and the default sky: the guide holds only the sunlit floor's
    /// diffuse light, so every guided draw from the floor aims at its own plane.
    /// </summary>
    private static (VulkanRenderer Renderer, RenderSnapshot Scene) SunlitFloorAlone()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        RenderSnapshot scene = Gpu.Snapshot(Vector3.Normalize(new(0.3f, 1, 0.2f)));
        scene.Instances.Add(Floor(renderer, MaterialDesc.Color(Vector4.One, roughness: Glossy)));
        return (renderer, scene);
    }

    /// <summary>The floor pixels within 24 px of the panel's mirror image on the screen, row by row from the top.</summary>
    private static bool[] FloorBelowPanel()
    {
        (int x, int y) = MirrorPixel(PanelCenter, Width, Height);
        bool[] floor = new bool[Width * Height];
        for (int row = Height / 2 + 2; row < Height; row++)
        for (int column = 0; column < Width; column++)
            floor[row * Width + column] = Math.Abs(column - x) < 24 && Math.Abs(row - y) < 24;
        floor.Count(pixel => pixel).Should().BeGreaterThan(500);
        return floor;
    }

    /// <summary>
    /// The glossy floor's specular samples over <paramref name="frames"/> frames from frame <paramref name="skipped"/> on:
    /// the mean luminance, its standard error from the frames' means, and the mean over the pixels of each pixel's
    /// variance.
    /// </summary>
    private static (double Mean, double Error, double Variance) Reflection(bool[] floor, int frames, int skipped)
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = PanelOverFloor(Glossy);
        try
        {
            int pixels = floor.Count(pixel => pixel);
            double[] sums = new double[floor.Length];
            double[] squares = new double[floor.Length];
            var frameMeans = new List<double>();
            for (int frame = 0; frame < frames; frame++)
            {
                scene.Time += Tick;
                renderer.Render(scene, 1f);
                if (frame < skipped)
                    continue;
                float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
                double frameSum = 0;
                for (int pixel = 0; pixel < floor.Length; pixel++)
                {
                    if (!floor[pixel])
                        continue;
                    double light = Luminance(new(sample[4 * pixel], sample[4 * pixel + 1], sample[4 * pixel + 2]));
                    sums[pixel] += light;
                    squares[pixel] += light * light;
                    frameSum += light;
                }
                frameMeans.Add(frameSum / pixels);
            }
            int count = frames - skipped;
            double mean = frameMeans.Average();
            double spread = Math.Sqrt(frameMeans.Sum(m => (m - mean) * (m - mean)) / (count - 1));
            double variance = 0;
            for (int pixel = 0; pixel < floor.Length; pixel++)
                if (floor[pixel])
                    variance += squares[pixel] / count - Math.Pow(sums[pixel] / count, 2);
            return (mean, spread / Math.Sqrt(count), variance / pixels);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static float Luminance(Vector3 light) => Vector3.Dot(light, new(0.2126f, 0.7152f, 0.0722f));

    /// <summary>Runs <paramref name="run"/> with the switch at <paramref name="value"/>, and restores it.</summary>
    private static T WithSwitch<T>(string? value, Func<T> run)
    {
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, value);
        try
        {
            return run();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }

    private static void WithSwitch(string? value, Action run) =>
        WithSwitch(
            value,
            () =>
            {
                run();
                return 0;
            }
        );
}
