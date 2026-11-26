using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The short-history fix, step 3 of accumulate_spatial.comp (https://github.com/alexispurslane/tertius/wiki/temporal-renderer, section 8, step 3): a
/// history shorter than 4 frames takes the length-weighted mean of 9 taps of its surface. The specular signal turns the
/// 3 × 3 square per pixel at spacings 4 and 2, by the blue-noise mask, so that the rare bright samples after a camera cut
/// spread without a regular lattice (t1-3er4, docs/research/cut-grid-probe). The diffuse signal keeps the square. A wall
/// fills a view of 256 × 128 render pixels. The test samples are 0, except one bright sample in each cell of 16 × 16
/// pixels, at a place in the cell that moves against the blue-noise mask's cells. A turned square reaches at most 6 pixels
/// from its center, so the light of each bright sample stays in its cell.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ShortHistoryFixRenderTests
{
    private const int Width = 256,
        Height = 128,
        Cell = 16;

    /// <summary>The light of a bright sample: 9 × 9, so that the square gives each of its 9 receivers 9 / (n + 1).</summary>
    private const float Bright = 81f;

    private static readonly SignalParameters Specular = SignalParameters.Specular(0.4f);

    /// <summary>A pixel's offset from a bright sample, in render pixels.</summary>
    private readonly record struct Tap(int X, int Y);

    private static readonly SignalParameters Diffuse = AccumulatorRenderTests.DiffuseTestSignal with
    {
        Cap = 16,
        SmoothCap = 16,
    };

    /// <summary>The bright sample's place in cell (i, j), moved by <paramref name="shift"/> draws: 6 to 9 pixels into the cell.</summary>
    private static (int Column, int Row) BrightPlace(int i, int j, int shift = 0) =>
        (i * Cell + 6 + (i + 2 * j + shift) % 4, j * Cell + 6 + (2 * i + j + 3 * shift) % 4);

    /// <summary>The samples of a draw: 0 with one bright sample per cell, moved by <paramref name="shift"/>.</summary>
    private static Func<int, int, Vector4> BrightSamples(int shift = 0) =>
        (column, row) =>
        {
            (int brightColumn, int brightRow) = BrightPlace(column / Cell, row / Cell, shift);
            bool bright = column == brightColumn && row == brightRow;
            return new Vector4(new Vector3(bright ? Bright : 0f), 1f);
        };

    /// <summary>
    /// The samples of a draw with the near wall: bright in the 4 columns of the far wall next to the edge, columns 128 to
    /// 131, and 0 elsewhere. Every near-wall pixel up to 6 columns left of the edge has taps in the stripe.
    /// </summary>
    private static readonly Func<int, int, Vector4> BrightStripeRightOfTheEdge = (column, _) =>
        new(new Vector3(column is >= (Width / 2) and < (Width / 2 + 4) ? Bright : 0f), 1f);

    /// <summary>
    /// A renderer that accumulates the test signal of <paramref name="parameters"/> on a wall of roughness
    /// <paramref name="roughness"/> that fills the view, and, with <paramref name="nearLeftHalf"/>, a second wall 10 m nearer
    /// that covers the left half of the view, columns 0 to 127.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, RenderSnapshot Scene) Accumulating(
        SignalParameters parameters,
        float roughness = 0.5f,
        bool nearLeftHalf = false,
        ScreenTraceSetting screenTrace = default
    )
    {
        // No warm-up draws: with the screen tracer, a still's warm-up draws would lengthen every history before the
        // tests' first draw.
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, screenTrace: screenTrace, warmUpFrames: 0);
        MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId paint = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: roughness));
        RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        scene.Instances.Add(
            Gpu.Instance(
                mesh: cube,
                material: paint,
                position: new(0, 0, -20),
                scale: new(80, 60, 1),
                chainDepth: TestChains.CamerasFrame,
                entity: 1
            )
        );
        if (nearLeftHalf)
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: paint,
                    position: new(-20, 0, -10),
                    scale: new(40, 40, 1),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 2
                )
            );
        var sample = new SamplePass(
            renderer.CreateSignal(parameters: parameters, name: "test", parts: 2),
            renderer.Allocator
        );
        renderer.Graph.Add(FrameGraph.Stage.DirectLight, sample);
        renderer.Graph.Add(FrameGraph.Stage.DirectLight, new AccumulatePass(sample.Signal));
        return (renderer, sample, scene);
    }

    /// <summary>Draws one frame of <paramref name="scene"/> one tick after the last.</summary>
    private static void Draw(VulkanRenderer renderer, RenderSnapshot scene)
    {
        scene.Time += 1.0 / 60.0;
        renderer.Render(scene, 1f);
    }

    /// <summary>
    /// Draws <paramref name="zeroDraws"/> frames of samples 0 from a reset, then one frame of the bright samples, or with
    /// <paramref name="nearLeftHalf"/> of the bright stripe right of the edge. The fix of the last draw runs at spacing
    /// 4 >> <paramref name="zeroDraws"/>. Returns the slow history of part 0.
    /// </summary>
    private static float[] FixedBrightSamples(SignalParameters parameters, int zeroDraws, bool nearLeftHalf = false)
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: parameters,
            nearLeftHalf: nearLeftHalf
        );
        try
        {
            sample.Value = new(x: 0f, y: 0f, z: 0f, w: 1f);
            for (int draw = 0; draw < zeroDraws; draw++)
                Draw(renderer, scene);
            sample.Samples = nearLeftHalf ? BrightStripeRightOfTheEdge : BrightSamples();
            Draw(renderer, scene);
            return renderer.ReadImage(sample.Signal.Slow(0));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The pixels of cell (i, j) that hold light, as offsets from the cell's bright sample at <paramref name="shift"/>, with
    /// their light.
    /// </summary>
    private static List<(Tap Offset, float Light)> ReceiversOf(float[] history, int i, int j, int shift = 0)
    {
        (int column, int row) = BrightPlace(i, j, shift);
        var receivers = new List<(Tap Offset, float Light)>();
        for (int y = j * Cell; y < (j + 1) * Cell; y++)
        for (int x = i * Cell; x < (i + 1) * Cell; x++)
        {
            float light = history[4 * (y * Width + x)];
            if (light != 0f)
                receivers.Add((Offset: new Tap(X: x - column, Y: y - row), Light: light));
        }
        return receivers;
    }

    /// <summary>
    /// The cells away from the image's border: a receiver of a border cell's bright sample can have taps outside the
    /// image, which the fix leaves out, so its weights differ from the others'.
    /// </summary>
    private static IEnumerable<(int I, int J)> Cells()
    {
        for (int j = 1; j < Height / Cell - 1; j++)
        for (int i = 1; i < Width / Cell - 1; i++)
            yield return (i, j);
    }

    /// <summary>Whether <paramref name="offset"/> is a tap of the axis-aligned square at <paramref name="spacing"/>.</summary>
    private static bool OnTheSquare(Tap offset, int spacing) =>
        offset.X % spacing == 0
        && offset.Y % spacing == 0
        && Math.Abs(offset.X) <= spacing
        && Math.Abs(offset.Y) <= spacing;

    /// <summary>The share of the receivers other than the bright sample's own pixel that lie off the square's taps.</summary>
    private static double ShareOffTheSquare(float[] history, int spacing, int shift = 0)
    {
        int others = 0,
            off = 0;
        foreach ((int i, int j) in Cells())
        foreach ((Tap offset, _) in ReceiversOf(history: history, i: i, j: j, shift: shift))
        {
            if (offset == default)
                continue;
            others++;
            if (!OnTheSquare(offset, spacing))
                off++;
        }
        return (double)off / others;
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 2)]
    public void ASpecularShortHistorySpreadsMostOfABrightSampleOffTheSquaresLattice(int zeroDraws, int spacing)
    {
        float[] history = FixedBrightSamples(Specular, zeroDraws);

        ShareOffTheSquare(history, spacing)
            .Should()
            .BeGreaterThan(
                0.5,
                "7 of 8 orientations at spacing 4, and 3 of 4 at spacing 2, share no tap with the axis-aligned square"
            );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheDiffuseShortHistorySpreadsEachBrightSampleOverTheAxisAlignedSquare(int zeroDraws)
    {
        int spacing = 4 >> zeroDraws;
        float[] history = FixedBrightSamples(Diffuse, zeroDraws);

        foreach ((int i, int j) in Cells())
        {
            List<(Tap Offset, float Light)> receivers = ReceiversOf(history: history, i: i, j: j);
            receivers.Select(r => r.Offset).Should().OnlyContain(offset => OnTheSquare(offset, spacing));
            receivers.Should().HaveCount(9, "cell ({0}, {1}) is one surface", i, j);
            receivers
                .Select(r => r.Light)
                .Should()
                .AllSatisfy(light => light.Should().Be(Bright / (zeroDraws + 1) / 9));
        }
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void ASpecularBrightSampleReachesNoFartherThanTheSpacingAndAHalf(int zeroDraws, int spacing)
    {
        float[] history = FixedBrightSamples(Specular, zeroDraws);

        int reach = spacing == 1 ? 1 : spacing + spacing / 2;
        foreach ((int i, int j) in Cells())
            ReceiversOf(history: history, i: i, j: j)
                .Should()
                .OnlyContain(r => Math.Abs(r.Offset.X) <= reach && Math.Abs(r.Offset.Y) <= reach);
    }

    [Fact]
    public void AtSpacingOneTheSpecularShortHistoryKeepsTheSquare()
    {
        float[] history = FixedBrightSamples(Specular, zeroDraws: 2);

        foreach ((int i, int j) in Cells())
        {
            List<(Tap Offset, float Light)> receivers = ReceiversOf(history: history, i: i, j: j);
            receivers.Should().HaveCount(9);
            receivers.Select(r => r.Offset).Should().OnlyContain(offset => OnTheSquare(offset, 1));
            receivers.Sum(r => r.Light).Should().BeApproximately(Bright / 3, 1e-3f);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AConstantSpecularSampleStaysExactlyConstantThroughTheFix(int zeroDraws)
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(parameters: Specular);
        try
        {
            sample.Value = new(x: 0.25f, y: 0.5f, z: 1f, w: 1f);
            for (int draw = 0; draw <= zeroDraws; draw++)
                Draw(renderer, scene);

            float[] history = renderer.ReadImage(sample.Signal.Slow(0));
            for (int pixel = 0; pixel < Width * Height; pixel++)
                new Vector3(history[4 * pixel], history[4 * pixel + 1], history[4 * pixel + 2])
                    .Should()
                    .Be(new Vector3(0.25f, 0.5f, 1f), "pixel {0} reads only taps of that sample", pixel);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ASpecularBrightSampleSendsNoLightAcrossADepthEdge(int zeroDraws)
    {
        // The near wall covers columns 0 to 127. The bright stripe lies on the far wall in columns 128 to 131, in reach of
        // the fix's taps from every near-wall pixel up to 6 columns left of the edge.
        float[] history = FixedBrightSamples(Specular, zeroDraws, nearLeftHalf: true);

        for (int row = 0; row < Height; row++)
        for (int column = 0; column < Width / 2 - 1; column++)
            history[4 * (row * Width + column)]
                .Should()
                .Be(0f, "pixel ({0}, {1}) shows the near wall, which held no bright sample", column, row);
    }

    /// <summary>
    /// Draws of a wall whose roughness changes in every draw, 0.38 and 0.8 in turn: the change rejects every history, so
    /// each draw's fix runs at spacing 4 on samples of that draw alone, at the draw's index since the reset. Returns the
    /// slow history of part 0 after each draw, the bright samples of draw k moved by <paramref name="moving"/> ? k : 0.
    /// </summary>
    private static List<float[]> ContinuouslyShortHistories(int draws, bool moving)
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: Specular,
            roughness: 0.38f
        );
        try
        {
            MaterialId rough = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.8f));
            MaterialId smooth = scene.Instances[0].Material;
            var histories = new List<float[]>();
            for (int draw = 0; draw < draws; draw++)
            {
                RenderInstance wall = scene.Instances[0];
                wall.Material = draw % 2 == 0 ? smooth : rough;
                scene.Instances[0] = wall;
                sample.Samples = BrightSamples(moving ? draw : 0);
                Draw(renderer, scene);
                histories.Add(renderer.ReadImage(sample.Signal.Slow(0)));
            }
            return histories;
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void OverTheMasksPlacesAndPhasesASpecularBrightSampleKeepsItsLightOnAverage()
    {
        const int draws = 8;
        List<float[]> histories = ContinuouslyShortHistories(draws, moving: true);

        var energies = new List<double>();
        for (int draw = 0; draw < draws; draw++)
        {
            float[] history = histories[draw];
            for (int pixel = 0; pixel < Width * Height; pixel++)
                history[4 * pixel + 3]
                    .Should()
                    .Be(1f, "the change of roughness in draw {0} rejects every history", draw);
            foreach ((int i, int j) in Cells())
                energies.Add(ReceiversOf(history: history, i: i, j: j, shift: draw).Sum(r => (double)r.Light) / Bright);
        }

        energies.Should().OnlyContain(e => double.IsFinite(e) && e > 0);
        // Over the 672 bright samples, one sample's light spreads with a deviation of about 0.29, so the mean's deviation
        // is about 0.011: the bound is about 3.6 deviations.
        energies
            .Average()
            .Should()
            .BeApproximately(1.0, 0.04, "the mask's bytes are uniform over its cells and frames");
    }

    [Fact]
    public void AContinuouslyShortSpecularHistoryTurnsItsSquaresFromDrawToDraw()
    {
        const int draws = 4;
        List<float[]> histories = ContinuouslyShortHistories(draws, moving: false);

        for (int draw = 1; draw < draws; draw++)
        {
            int changed = Cells()
                .Count(cell =>
                    !ReceiversOf(history: histories[draw], i: cell.I, j: cell.J)
                        .Select(r => r.Offset)
                        .ToHashSet()
                        .SetEquals(
                            ReceiversOf(history: histories[draw - 1], i: cell.I, j: cell.J).Select(r => r.Offset)
                        )
                );
            changed
                .Should()
                .BeGreaterThan(
                    Cells().Count() / 2,
                    "draw {0} reads another frame of the mask than draw {1}",
                    draw,
                    draw - 1
                );
        }
    }

    /// <summary>The representatives u, v, u + v and u − v of the turned squares (accumulate_spatial.comp).</summary>
    private static readonly Tap[][] TurnedSquares4 =
    [
        [new(4, 0), new(0, 4), new(4, 4), new(4, -4)],
        [new(4, 1), new(-1, 4), new(3, 5), new(5, -3)],
        [new(4, 2), new(-2, 4), new(2, 5), new(5, -2)],
        [new(3, 2), new(-2, 3), new(1, 6), new(6, -1)],
        [new(3, 3), new(-3, 3), new(0, 6), new(6, 0)],
        [new(2, 3), new(-3, 2), new(-1, 6), new(6, 1)],
        [new(2, 4), new(-4, 2), new(-2, 5), new(5, 2)],
        [new(1, 4), new(-4, 1), new(-3, 5), new(5, 3)],
    ];

    private static readonly Tap[][] TurnedSquares2 =
    [
        [new(2, 0), new(0, 2), new(2, 2), new(2, -2)],
        [new(2, 1), new(-1, 2), new(1, 3), new(3, -1)],
        [new(1, 1), new(-1, 1), new(0, 3), new(3, 0)],
        [new(2, -1), new(1, 2), new(3, 1), new(1, -3)],
    ];

    /// <summary>pcg3d of blue_noise.glsl (Jarzynski and Olano 2020).</summary>
    private static (uint X, uint Y, uint Z) Pcg3d(uint x, uint y, uint z)
    {
        unchecked
        {
            x = x * 1664525u + 1013904223u;
            y = y * 1664525u + 1013904223u;
            z = z * 1664525u + 1013904223u;
            x += y * z;
            y += z * x;
            z += x * y;
            x ^= x >> 16;
            y ^= y >> 16;
            z ^= z >> 16;
            x += y * z;
            y += z * x;
            z += x * y;
            return (x, y, z);
        }
    }

    /// <summary>The mask's radial byte at <paramref name="column"/>, <paramref name="row"/> for <paramref name="frame"/>: blue_noise.glsl's blueNoiseBytesOf.</summary>
    private static byte RadialByte(int column, int row, uint frame)
    {
        uint maskFrame = frame & (BlueNoiseMask.Frames - 1);
        (uint shiftX, uint shiftY, _) = Pcg3d(x: frame / BlueNoiseMask.Frames, y: 0u, z: 0x5348u);
        uint x = unchecked((uint)column + shiftX) & (BlueNoiseMask.Side - 1);
        uint y = unchecked((uint)row + shiftY) & (BlueNoiseMask.Side - 1);
        long texelX = maskFrame % BlueNoiseMask.TilesAcross * BlueNoiseMask.Side + x;
        long texelY = maskFrame / BlueNoiseMask.TilesAcross * BlueNoiseMask.Side + y;
        return BlueNoiseMask.Shared.Texels[2 * (texelY * BlueNoiseMask.ImageSide + texelX)];
    }

    /// <summary>
    /// The receivers of cell (i, j)'s bright sample in a reference model of the specular fix at <paramref name="spacing"/> in
    /// draw <paramref name="drawIndex"/>: each pixel's square comes from the mask frame half a period ahead of the draw.
    /// </summary>
    private static HashSet<Tap> ExpectedReceivers(int i, int j, int spacing, uint drawIndex)
    {
        (int column, int row) = BrightPlace(i, j);
        var receivers = new HashSet<Tap>();
        for (int y = j * Cell; y < (j + 1) * Cell; y++)
        for (int x = i * Cell; x < (i + 1) * Cell; x++)
        {
            byte radial = RadialByte(column: x, row: y, frame: unchecked(drawIndex + BlueNoiseMask.Frames / 2));
            Tap[] square = spacing == 4 ? TurnedSquares4[radial >> 5] : TurnedSquares2[radial >> 6];
            var source = new Tap(X: column - x, Y: row - y);
            if (source == default || square.Any(d => d == source || new Tap(X: -d.X, Y: -d.Y) == source))
                receivers.Add(source with { X = -source.X, Y = -source.Y });
        }
        return receivers;
    }

    [Fact]
    public void TheSpecularFixTakesEachPixelsTurnFromTheMaskFrameHalfAPeriodAheadOfTheDraw()
    {
        // Draws 0, 31, 32 and 33 of continuously short histories: their mask frames are 32, 63, 0 and 1, and draw 32's
        // frame starts the second period of the mask, which reads it at another shift.
        List<float[]> histories = ContinuouslyShortHistories(draws: 34, moving: false);

        foreach (int draw in new[] { 0, 31, 32, 33 })
        foreach ((int i, int j) in Cells())
            ReceiversOf(history: histories[draw], i: i, j: j)
                .Select(r => r.Offset)
                .Should()
                .BeEquivalentTo(
                    ExpectedReceivers(i: i, j: j, spacing: 4, drawIndex: (uint)draw),
                    "cell ({0}, {1}) in draw {2}",
                    i,
                    j,
                    draw
                );
    }

    [Fact]
    public void AtSpacingTwoTheSpecularFixTakesOneOfFourTurnsFromTheMask()
    {
        float[] history = FixedBrightSamples(Specular, zeroDraws: 1);

        foreach ((int i, int j) in Cells())
            ReceiversOf(history: history, i: i, j: j)
                .Select(r => r.Offset)
                .Should()
                .BeEquivalentTo(ExpectedReceivers(i: i, j: j, spacing: 2, drawIndex: 1), "cell ({0}, {1})", i, j);
    }

    /// <summary>The fix in every specular history and with the screen tracer on and off (tests/STYLE.md, "Long tests").</summary>
    // It sets TERTIUS_SPECULAR_HISTORY, which every test beside it would read.
    [Collection(ProcessEnvironmentCollection.Name)]
    public sealed class EverySpecularHistory
    {
        [Theory]
        [InlineData("original", false)]
        [InlineData("original", true)]
        [InlineData("precise-capped", false)]
        [InlineData("precise-capped", true)]
        [InlineData("precise-uncapped", false)]
        [InlineData("precise-uncapped", true)]
        public void EverySpecularHistoryTurnsTheSquareWithoutAValidationMessage(string mode, bool screenTrace)
        {
            string? previous = Environment.GetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY");
            VulkanRenderer? renderer = null;
            try
            {
                Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", mode);
                ScreenTraceSetting setting = new NoScreenTrace();
                if (screenTrace)
                    setting = ScreenTrace.Default;
                (VulkanRenderer created, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: Specular,
                    screenTrace: setting
                );
                renderer = created;
                sample.Samples = BrightSamples();
                Draw(renderer, scene);

                float[] history = renderer.ReadImage(sample.Signal.Slow(0));
                (int column, int row) = BrightPlace(i: 5, j: 3);
                history[4 * (row * Width + column) + 3].Should().Be(1f, "the fix of the first draw runs at spacing 4");
                ShareOffTheSquare(history, spacing: 4).Should().BeGreaterThan(0.5);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", previous);
                if (renderer is not null)
                    Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }
}
