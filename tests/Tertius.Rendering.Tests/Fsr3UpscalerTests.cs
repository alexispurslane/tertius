using System.Globalization;
using System.Numerics;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="Fsr3Upscaler"/> on the GPU, over AMD's pinned shaders (docs/plans/temporal-renderer.md, "FSR 3.1"): no
/// message under synchronization validation, the history over frames, resets and exposure changes, a reference image,
/// the exposure step, the reactive mask generator, refused inputs and determinism. Most tests wait for each frame.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class Fsr3UpscalerTests
{
    private static readonly Extent2D LargestRenderSize = new(64, 48);
    private static readonly Extent2D OutputSize = new(96, 72);

    [Fact]
    public void SharpeningChangesAnEdgeWithoutChangingTheInputOrTheRenderSize()
    {
        using var plain = new Fsr3Bench(LargestRenderSize, OutputSize);
        using var sharpened = new Fsr3Bench(LargestRenderSize, OutputSize);
        plain.FillPattern();
        sharpened.FillPattern();
        plain.Upscale(index: 0, reset: true);
        Fsr3Inputs inputs = sharpened.Inputs(index: 0, reset: true);
        inputs = inputs with { Frame = inputs.Frame with { Sharpness = 1 } };
        sharpened.Submit(command => sharpened.Upscaler.Record(command, sharpened.Barriers, inputs));
        sharpened.ReadOutput().Should().NotEqual(plain.ReadOutput());
        sharpened.Device.Log.Snapshot().Should().BeEmpty();
    }

    // The variable that turns on the measurement of FSR's GPU time.
    private const string TimingVariable = "TERTIUS_FSR_TIMING";

    // The color of the tests that upscale one color.
    private static readonly Vector4 Constant = new(x: 0.5f, y: 0.25f, z: 0.125f, w: 1f);

    [Fact]
    public void AConstantColorUpscalesToTheSameColorAtEveryPixelOfTheOutput()
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        bench.Fill(Constant);

        for (int index = 0; index < 4; index++)
            bench.Upscale(index, reset: index == 0);

        ShouldAllBeTheConstant(bench.ReadOutput());
    }

    [Theory]
    [InlineData(96u, 72u)]
    [InlineData(95u, 73u)]
    public void SharpeningWritesPositiveFiniteColorIncludingTheOutputBorders(uint width, uint height)
    {
        using var bench = new Fsr3Bench(LargestRenderSize, new(width, height));
        bench.Fill(Constant);
        Fsr3Inputs inputs = bench.Inputs(index: 0, reset: true);
        inputs = inputs with { Frame = inputs.Frame with { Sharpness = 1 } };

        bench.Submit(command => bench.Upscaler.Record(command, bench.Barriers, inputs));

        bench
            .ReadOutput()
            .Should()
            .OnlyContain(pixel =>
                float.IsFinite(pixel.X)
                && pixel.X > 0
                && float.IsFinite(pixel.Y)
                && pixel.Y > 0
                && float.IsFinite(pixel.Z)
                && pixel.Z > 0
            );
        bench.Device.Log.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void AConstantColorUpscalesToTheSameColorBelowTheLargestRenderSizeAndAfterTheRenderSizeChanges()
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        bench.Fill(Constant);

        // Below the largest render size, AMD's passes clamp their reads by the largest render size and reproject by
        // the last frame's render size.
        bench.RenderSize = new(48, 32);
        for (int index = 0; index < 4; index++)
            bench.Upscale(index, reset: index == 0);
        bench.RenderSize = new(56, 40);
        for (int index = 4; index < 8; index++)
            bench.Upscale(index, reset: false);

        ShouldAllBeTheConstant(bench.ReadOutput());
        string.Join("\n", bench.Device.Log.Snapshot()).Should().BeEmpty("a smaller render size breaks no valid usage");
    }

    [Fact]
    public void AReactiveMaskOfAFormatOtherThanR8UnormIsRefused()
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        using GpuImage wide = bench.CreateImage(LargestRenderSize, Format.R16Sfloat, "fsr reactive in R16F");
        Fsr3Inputs inputs = bench.Inputs(index: 0, reset: true) with { Reactive = wide };

        Action record = () =>
            bench.Submit(commandBuffer => bench.Upscaler.Record(commandBuffer, new BarrierBatch(), inputs));

        record
            .Should()
            .Throw<ArgumentException>("AMD's generator stores the mask through an r8 declaration")
            .WithMessage("*R8*");
    }

    [Fact]
    public void UpscalingReportsNothingToValidation()
    {
        ValidationLog log;
        using (var bench = new Fsr3Bench(LargestRenderSize, OutputSize))
        {
            log = bench.Device.Log;
            bench.FillPattern();
            for (int index = 0; index < 6; index++)
                bench.Upscale(index, reset: index == 0);
        }

        // Joined, so that a failure shows every message.
        string.Join("\n", log.Snapshot())
            .Should()
            .BeEmpty("every image must match its shader's declared format, and every pass its barriers and layouts");
    }

    [Fact]
    public void AResetFrameShowsNoneOfTheColorOfTheFramesBeforeIt()
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        bench.Fill(new Vector4(x: 4f, y: 0f, z: 0f, w: 1f));
        for (int index = 0; index < 8; index++)
            bench.Upscale(index, reset: index == 0);

        bench.Fill(new Vector4(x: 0f, y: 0f, z: 2f, w: 1f));
        bench.Upscale(index: 8, reset: true);

        bench
            .ReadOutput()
            .Select(pixel => pixel.X)
            .Should()
            .AllSatisfy(red => red.Should().BeLessThan(1e-3f, "a reset reads no history of the red frames"));
    }

    [Fact]
    public void TheExposureStepWritesAmdsRatioOfThisFramesPreExposureToTheLastFrames()
    {
        Fsr3Golden.Frame[] golden = Fsr3Golden.Values.Sequences.Single(sequence => sequence.Name == "small").Frames;
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        bench.FillPattern();

        var written = new List<float>();
        float lastStops = MathF.Log2(golden[0].PreExposure);
        for (int index = 0; index < golden.Length; index++)
        {
            float stops = MathF.Log2(golden[index].PreExposure);
            bench.SetExposure(stops: stops, previousStops: lastStops);
            bench.Upscale(index, reset: golden[index].Reset);
            written.Add(bench.ReadDeltaPreExposure());
            lastStops = stops;
        }

        written.Should().Equal(golden.Select(frame => frame.Constants["deltaPreExposure"]));
    }

    [Fact]
    public void TheClearStepZeroesEveryTexelOfTheReconstructedDepthTheCounterAndEachLevelOfTheLuminancePyramid()
    {
        // 8,200 × 64 gives the luminance pyramid at half of it 13 levels, the most that the clear step binds.
        using var bench = new Fsr3Bench(largestRenderSize: new(8200, 64), outputSize: new(8200, 64));
        Fsr3Upscaler upscaler = bench.Upscaler;
        upscaler.Image(Fsr3Image.SpdMips).MipLevels.Should().Be(13);
        foreach (Fsr3Image image in Fsr3Upscaler.ZeroedEachFrame)
            bench.FillEveryLevel(upscaler.Image(image), new Vector4(1f));

        bench.Submit(commandBuffer =>
        {
            upscaler.DeclareClearStep(bench.Barriers);
            bench.Barriers.Flush(bench.Device.Vk, commandBuffer);
            upscaler.DispatchClearStep(commandBuffer);
        });

        foreach (Fsr3Image image in Fsr3Upscaler.ZeroedEachFrame)
        {
            GpuImage cleared = upscaler.Image(image);
            for (uint level = 0; level < cleared.MipLevels; level++)
                bench
                    .ReadWords(cleared, level)
                    .Should()
                    .AllSatisfy(word => word.Should().Be(0u), $"the clear step zeroes level {level} of {image}");
        }
    }

    [Fact]
    public void AnUpscaleZeroesTheLevelsOfTheLuminancePyramidThatNoPassOfAmdsWrites()
    {
        // AMD's passes write levels 0 to 5, so in a pyramid of 9 levels only the clear step writes levels 6 to 8.
        using var bench = new Fsr3Bench(largestRenderSize: new(520, 300), outputSize: new(780, 450));
        GpuImage pyramid = bench.Upscaler.Image(Fsr3Image.SpdMips);
        bench.FillPattern();
        bench.Upscale(index: 0, reset: true);
        bench.FillEveryLevel(pyramid, new Vector4(1f));

        bench.Upscale(index: 1, reset: false);

        for (uint level = 6; level < pyramid.MipLevels; level++)
            bench
                .ReadWords(pyramid, level)
                .Should()
                .AllSatisfy(word => word.Should().Be(0u), $"every frame zeroes level {level} of the pyramid");
    }

    [Fact]
    public void TwoUpscalersOfTheSameFramesGiveTheSameBits()
    {
        static Vector4[] Run()
        {
            using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
            bench.FillPattern();
            for (int index = 0; index < 5; index++)
                bench.Upscale(index, reset: index == 0);
            return bench.ReadOutput();
        }

        Run().Should().Equal(Run());
    }

    [Fact]
    public void TheFirstFrameOfAnUpscalerReadsNoHistoryEvenWithoutAReset()
    {
        using var reset = new Fsr3Bench(LargestRenderSize, OutputSize);
        using var unreset = new Fsr3Bench(LargestRenderSize, OutputSize);
        reset.FillPattern();
        unreset.FillPattern();

        reset.Upscale(index: 0, reset: true);
        unreset.Upscale(index: 0, reset: false);

        unreset.ReadOutput().Should().Equal(reset.ReadOutput(), "an upscaler's first frame has no history to read");
    }

    [Fact]
    public void WithoutResetsTheUpscaleOfAStillPatternSettles()
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        bench.FillPattern();
        for (int index = 0; index < 11; index++)
            bench.Upscale(index, reset: index == 0);
        Vector4[] tenth = bench.ReadOutput();

        bench.Upscale(index: 11, reset: false);

        // A frame that draws alone moves the pattern's edges with its jitter: two such frames differ by 0.19 to 0.42 on
        // average. With the history, the measured difference is 0.002 to 0.004 from the eighth frame on.
        MeanDifference(bench.ReadOutput(), tenth)
            .Should()
            .BeLessThan(0.02f, "each frame blends its jittered samples into the history of the frames before it");
    }

    [Fact]
    public void AResetFrameUpscalesAsTheFirstFrameOfANewUpscalerDoes()
    {
        using var settled = new Fsr3Bench(LargestRenderSize, OutputSize);
        using var fresh = new Fsr3Bench(LargestRenderSize, OutputSize);
        settled.FillPattern();
        fresh.FillPattern();
        for (int index = 0; index < 8; index++)
            settled.Upscale(index, reset: index == 0);

        settled.Upscale(index: 8, reset: true);
        fresh.Upscale(index: 8, reset: true);

        settled
            .ReadOutput()
            .Should()
            .Equal(fresh.ReadOutput(), "a reset reads none of the history of the frames before it");
    }

    [Fact]
    public void AChangeOfThePreExposureScalesTheHistoryByTheRatioAlone()
    {
        static Vector4[] Run(bool brighter)
        {
            using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
            bench.FillPattern();
            for (int index = 0; index < 12; index++)
                bench.Upscale(index, reset: index == 0);
            if (brighter)
            {
                // One stop more: the pre-exposed color doubles, and so must the history that FSR reprojects.
                bench.FillPattern(scale: 2f);
                bench.SetExposure(stops: 1, previousStops: 0);
            }

            bench.Upscale(index: 12, reset: false);
            return bench.ReadOutput();
        }

        Vector4[] same = Run(brighter: false);
        Vector4[] brighter = Run(brighter: true);

        // The exposure image reads as 1, so only deltaPreExposure, 2 here, scales the history. The measured difference
        // is 0.0004 on average, from the half floats of the history and AMD's tone map of the doubled color.
        MeanDifference(brighter, [.. same.Select(pixel => 2f * pixel)])
            .Should()
            .BeLessThan(0.01f, "the brighter frame's history is the settled history at twice the pre-exposure");
    }

    [Fact]
    public void ANewUpscalerOverTheMemoryOfAnOldOneReadsNoneOfItsImages()
    {
        using var reused = new Fsr3Bench(LargestRenderSize, OutputSize);
        using var fresh = new Fsr3Bench(LargestRenderSize, OutputSize);
        reused.FillPattern(scale: 4f, inverted: true);
        for (int index = 0; index < 8; index++)
            reused.Upscale(index, reset: index == 0);
        reused.ReplaceUpscaler();
        reused.FillPattern();
        fresh.FillPattern();

        reused.Upscale(index: 0, reset: false);
        fresh.Upscale(index: 0, reset: false);

        reused.ReadOutput().Should().Equal(fresh.ReadOutput(), "the first frame clears every image that it reads");
    }

    [Fact]
    public void TheFirstFrameAfterAChangeOfTheSceneFollowsTheNewScene()
    {
        using var changed = new Fsr3Bench(LargestRenderSize, OutputSize);
        using var settled = new Fsr3Bench(LargestRenderSize, OutputSize);
        changed.FillPattern();
        settled.FillPattern(inverted: true);
        for (int index = 0; index < 10; index++)
        {
            changed.Upscale(index, reset: index == 0);
            settled.Upscale(index, reset: index == 0);
        }
        changed.FillPattern(inverted: true);

        changed.Upscale(index: 10, reset: false);
        settled.Upscale(index: 10, reset: false);

        // FSR compares this frame's luma with the last frame's and drops the history where the shading changed. The
        // measured difference is 0.59 on average; with the last frame's luma read as this frame's, it is 2.6.
        MeanDifference(changed.ReadOutput(), settled.ReadOutput())
            .Should()
            .BeLessThan(1.2f, "the shading change pyramid reads this frame's luma and the last frame's");
    }

    [Fact]
    public void TheSettledPatternAndTheFramesAfterAChangeOfTheSceneMatchTheirReferenceImage()
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        bench.FillPattern();
        for (int index = 0; index < 10; index++)
            bench.Upscale(index, reset: index == 0);
        var frames = new List<Vector4[]> { bench.ReadOutput() };
        bench.FillPattern(inverted: true);
        for (int index = 10; index < 13; index++)
        {
            bench.Upscale(index, reset: false);
            frames.Add(bench.ReadOutput());
        }

        // From the left: the tenth frame of the still pattern, then the first three frames of the inverted pattern.
        ReferenceImages.Verify(
            actual: Panels(frames, OutputSize),
            referencePath: Path.Combine(ReferenceImages.Folder, "fsr3-pattern.png")
        );
    }

    [Fact]
    public void AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard()
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        using GpuBuffer written = bench.CreateGpuExposureBuffer();
        bench.FillPattern();

        // In the frame, the exposure pass writes the buffer on the GPU in the same command buffer as the upscale.
        bench.Submit(commandBuffer =>
        {
            bench.WriteOnGpu(commandBuffer: commandBuffer, buffer: written, stops: 1f, previousStops: 0f);
            Fsr3Inputs inputs = bench.Inputs(index: 0, reset: true) with { Exposure = written };
            bench.Upscaler.Record(commandBuffer, bench.Barriers, inputs);
        });

        bench.ReadDeltaPreExposure().Should().Be(2f);
        string.Join("\n", bench.Device.Log.Snapshot())
            .Should()
            .BeEmpty("the exposure step waits for the write of the exposure buffer");
    }

    [Fact]
    public void TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation()
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        bench.FillPattern();

        // The upscaler has two frames in flight, so the second frame writes sets that the first frame does not bind.
        bench.Submit(commandBuffer =>
        {
            bench.Upscaler.Record(commandBuffer, bench.Barriers, bench.Inputs(index: 0, reset: true));
            bench.Upscaler.Record(commandBuffer, bench.Barriers, bench.Inputs(index: 1, reset: false));
        });

        string.Join("\n", bench.Device.Log.Snapshot())
            .Should()
            .BeEmpty("a set that a recorded frame binds stays as it was");
    }

    [Fact]
    public void TimedBatchesUpscaleTheSameDrawsAsOneUpscalePerSubmission()
    {
        using var batched = new Fsr3Bench(
            largestRenderSize: LargestRenderSize,
            outputSize: OutputSize,
            validation: true,
            framesInFlight: 4
        );
        using var single = new Fsr3Bench(LargestRenderSize, OutputSize);
        if (!batched.Device.Timestamps.Supported)
            Assert.Skip($"{batched.Device.DeviceName} writes no timestamps on its queue.");
        batched.FillPattern();
        single.FillPattern();
        for (int index = 0; index < 8; index++)
            single.Upscale(index, reset: index == 0);

        batched.TimedUpscales(firstBatch: 0, batches: 1, batchSize: 4, batchesInFlight: 1);
        batched.TimedUpscales(firstBatch: 1, batches: 1, batchSize: 4, batchesInFlight: 1);

        batched
            .ReadOutput()
            .Should()
            .Equal(single.ReadOutput(), "the second call's batch continues the draws after the first call's batch");
    }

    [Fact]
    public void ATimedBatchOfAsManyUpscalesAsTheUpscalersFramesInFlightReportsNothingToValidation()
    {
        using var bench = new Fsr3Bench(
            largestRenderSize: LargestRenderSize,
            outputSize: OutputSize,
            validation: true,
            framesInFlight: 4
        );
        if (!bench.Device.Timestamps.Supported)
            Assert.Skip($"{bench.Device.DeviceName} writes no timestamps on its queue.");
        bench.FillPattern();

        bench.TimedUpscales(firstBatch: 0, batches: 2, batchSize: 4, batchesInFlight: 1);

        string.Join("\n", bench.Device.Log.Snapshot())
            .Should()
            .BeEmpty("each upscale of a batch writes a set that no earlier upscale of its command buffer binds");
    }

    [Fact]
    public void ATimedBatchOfMoreUpscalesThanTheUpscalersFramesInFlightIsRefused()
    {
        using var bench = new Fsr3Bench(
            largestRenderSize: LargestRenderSize,
            outputSize: OutputSize,
            validation: true,
            framesInFlight: 2
        );

        Action time = () => bench.TimedUpscales(firstBatch: 0, batches: 1, batchSize: 3, batchesInFlight: 1);

        time.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*frames in flight*");
    }

    [Fact]
    public void TimedBatchesInFlightUpscaleTheSameDrawsAsOneUpscalePerSubmission()
    {
        using var batched = new Fsr3Bench(
            largestRenderSize: LargestRenderSize,
            outputSize: OutputSize,
            validation: true,
            framesInFlight: 8
        );
        using var single = new Fsr3Bench(LargestRenderSize, OutputSize);
        if (!batched.Device.Timestamps.Supported)
            Assert.Skip($"{batched.Device.DeviceName} writes no timestamps on its queue.");
        batched.FillPattern();
        single.FillPattern();
        for (int index = 0; index < 12; index++)
            single.Upscale(index, reset: index == 0);

        double[] times = batched.TimedUpscales(firstBatch: 0, batches: 3, batchSize: 4, batchesInFlight: 2);

        times.Should().HaveCount(3).And.AllSatisfy(time => time.Should().BePositive());
        batched.ReadOutput().Should().Equal(single.ReadOutput(), "batches in flight run in the order of their draws");
    }

    [Fact]
    public void TwoTimedBatchesInFlightOfHalfTheUpscalersFramesInFlightReportNothingToValidation()
    {
        using var bench = new Fsr3Bench(
            largestRenderSize: LargestRenderSize,
            outputSize: OutputSize,
            validation: true,
            framesInFlight: 8
        );
        if (!bench.Device.Timestamps.Supported)
            Assert.Skip($"{bench.Device.DeviceName} writes no timestamps on its queue.");
        bench.FillPattern();

        bench.TimedUpscales(firstBatch: 0, batches: 5, batchSize: 4, batchesInFlight: 2);

        string.Join("\n", bench.Device.Log.Snapshot())
            .Should()
            .BeEmpty("each upscale writes a set that no command buffer still in flight binds");
    }

    [Fact]
    public void TimedBatchesInFlightOfMoreUpscalesThanTheUpscalersFramesInFlightAreRefused()
    {
        using var bench = new Fsr3Bench(
            largestRenderSize: LargestRenderSize,
            outputSize: OutputSize,
            validation: true,
            framesInFlight: 4
        );

        Action time = () => bench.TimedUpscales(firstBatch: 0, batches: 2, batchSize: 4, batchesInFlight: 2);

        time.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*frames in flight*");
    }

    [Fact]
    public void ATimedBatchReportsTheTimeOfOneOfItsUpscalesNotOfTheWholeBatch()
    {
        using var bench = new Fsr3Bench(
            largestRenderSize: LargestRenderSize,
            outputSize: OutputSize,
            validation: true,
            framesInFlight: 16
        );
        if (!bench.Device.Timestamps.Supported)
            Assert.Skip($"{bench.Device.DeviceName} writes no timestamps on its queue.");
        bench.FillPattern();
        // A batch of 2 and a batch of 16 take turns, so that a change of the GPU's clock reaches both alike. Pair n
        // upscales draws 32n and 32n + 1 in the small batch and draws 32n + 16 to 32n + 31 in the large one, so no draw
        // repeats.
        var small = new List<double>();
        var large = new List<double>();
        for (int pair = 0; pair < 9; pair++)
        {
            small.Add(bench.TimedUpscales(firstBatch: 16 * pair, batches: 1, batchSize: 2, batchesInFlight: 1)[0]);
            large.Add(bench.TimedUpscales(firstBatch: 2 * pair + 1, batches: 1, batchSize: 16, batchesInFlight: 1)[0]);
        }

        double smallMedian = small.Order().ElementAt(small.Count / 2);
        large
            .Order()
            .ElementAt(large.Count / 2)
            .Should()
            .BeLessThan(
                3 * smallMedian,
                "an upscale took 0.8 to 1.4 times as long in a batch of 16 as in a batch of 2, and the span of the batch of "
                    + "16 is about 8 times as long"
            );
    }

    [Fact]
    public void TheGeneratorMarksEveryPixelWhoseColorDiffersFromTheOpaqueOnlyColorWithAmdsBinaryValue()
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        bench.Fill(colorValue: new Vector4(x: 2f, y: 0f, z: 0f, w: 1f), opaqueOnlyValue: Vector4.Zero);

        bench.Upscale(index: 0, reset: true);

        // Tone mapped, the colors differ by 2/3, above AMD's threshold of 0.2, and an R8 texel holds 0.9 within 1/255.
        bench
            .ReadReactive()
            .Should()
            .AllSatisfy(reactive => reactive.Should().BeApproximately(0.9f, 1f / 255f, "AMD's sample's binary value"));
    }

    [Theory]
    [InlineData(65u, 48u)]
    [InlineData(64u, 49u)]
    [InlineData(0u, 48u)]
    [InlineData(64u, 0u)]
    public void ARenderSizeOutsideTheLargestRenderSizeIsRefused(uint width, uint height)
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize) { RenderSize = new(width, height) };
        Fsr3Inputs inputs = bench.Inputs(index: 0, reset: true);

        Action record = () =>
            bench.Submit(commandBuffer => bench.Upscaler.Record(commandBuffer, new BarrierBatch(), inputs));

        record.Should().Throw<ArgumentException>().WithMessage("*not within the largest render size*");
    }

    [Theory]
    [InlineData(97u, 72u)]
    [InlineData(96u, 73u)]
    public void AnOutputOfAnotherSizeThanTheOutputSizeIsRefused(uint width, uint height)
    {
        using var bench = new Fsr3Bench(LargestRenderSize, OutputSize);
        using GpuImage other = bench.CreateImage(
            new(width, height),
            Format.R16G16B16A16Sfloat,
            "fsr output of another size"
        );
        Fsr3Inputs inputs = bench.Inputs(index: 0, reset: true) with { Output = other };

        Action record = () =>
            bench.Submit(commandBuffer => bench.Upscaler.Record(commandBuffer, new BarrierBatch(), inputs));

        record.Should().Throw<ArgumentException>().WithMessage("*not of the output size*");
    }

    [Fact]
    public void AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTime()
    {
        // A measurement, not a check: it runs only on request, in a Release build under the GPU lock (tools/README.md,
        // "FSR 3.1 golden values"), and prints the time of FSR's dispatches by timestamps around Record.
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(TimingVariable) == "1",
            $"Set {TimingVariable}=1 to measure FSR's GPU time."
        );
        using var bench = new Fsr3Bench(new Extent2D(1280, 720), new Extent2D(1920, 1080), validation: false);
        if (!bench.Device.Timestamps.Supported)
            Assert.Skip($"{bench.Device.DeviceName} writes no timestamps on its queue.");
        bench.FillPattern();

        // The first 60 frames warm the caches and the driver's pipelines. The marker lines around the 300 timed frames let
        // tools/gpu_clock.py take gpu-clock's clock over them alone (tools/README.md, "The GPU clock of a timed run").
        bench.TimedUpscales(firstBatch: 0, batches: 60, batchSize: 1, batchesInFlight: 1);
        double[] frames = TimedSpan.Run(() =>
            bench.TimedUpscales(firstBatch: 60, batches: 300, batchSize: 1, batchesInFlight: 1)
        );
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        double[] times = [.. frames.Order()];

        double median = times[times.Length / 2];
        output.WriteLine(
            $"FSR 3.1 from 1280 x 720 to 1920 x 1080 on {bench.Device.DeviceName}: median {median:F3} ms, "
                + $"10th percentile {times[times.Length / 10]:F3} ms, 90th percentile {times[times.Length * 9 / 10]:F3} ms "
                + $"over {times.Length} frames"
        );
        // Every frame's time in the order of the frames, for tools/gpu-calibration/analyze.py, rounds.sh and upscalers.sh.
        output.WriteLine(
            "frame times, ms: "
                + string.Join(' ', frames.Select(time => time.ToString("F4", CultureInfo.InvariantCulture)))
        );
        median.Should().BePositive();
    }

    [Fact]
    public void AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTimePerFrameWhenEightFramesShareASubmission() =>
        MeasureBatchedUpscale(largestSize: new(1280, 720), activeSize: new(1280, 720));

    [Theory]
    [InlineData(1280u, 720u)]
    [InlineData(960u, 536u)]
    [InlineData(1920u, 1080u)]
    public void TheMaximumWindowAllocationReportsItsGpuTimeAtEachActiveSize(uint activeWidth, uint activeHeight) =>
        MeasureBatchedUpscale(largestSize: new(1920, 1080), activeSize: new(activeWidth, activeHeight));

    private static void MeasureBatchedUpscale(Extent2D largestSize, Extent2D activeSize)
    {
        // A measurement, not a check: it runs only on request, in a Release build under the GPU lock, and it is the
        // measurement of tools/fsr3-timing/time-round.sh (tools/README.md, "FSR 3.1 timing"). FSR's frames run back to
        // back on the GPU, eight per submission, so that the GPU idles between frames as little as MetalFX's frames in
        // tools/gpu-calibration/metalfx.swift do, and its clock holds from run to run (gpu-calibration.md, "MetalFX
        // against FSR"). A frame's time is the span of its batch over 8, which holds whenever MoltenVK latches a stamp.
        // Two batches wait on the queue at once, so that the GPU stays busy while the CPU records the next batch. With one,
        // the GPU idled whenever another process held the CPU between batches, and its clock fell by up to 40 % for about
        // 1.5 s (ter-q58r, rounds at a 1-minute load of 5 to 15).
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(TimingVariable) == "1",
            $"Set {TimingVariable}=1 to measure FSR's GPU time."
        );
        const int Batch = 8;
        const int WarmUpBatches = 60;
        const int TimedBatches = 150;
        const int InFlight = 2;
        using var bench = new Fsr3Bench(
            largestRenderSize: largestSize,
            outputSize: new Extent2D(1920, 1080),
            validation: false,
            framesInFlight: Batch * InFlight
        )
        {
            RenderSize = activeSize,
        };
        if (!bench.Device.Timestamps.Supported)
            Assert.Skip($"{bench.Device.DeviceName} writes no timestamps on its queue.");
        bench.FillPattern();

        // The warm-up batches, about 1 s on the development Mac, warm the caches and the driver, and let the GPU's
        // governor reach its clock under full load: after 8 batches, the clock still rose over the first 0.4 s of the
        // timed batches (ter-q58r). The timed batches, about 2.4 s, hold about 24 of gpu-clock's samples. The marker
        // lines around them let tools/gpu_clock.py take gpu-clock's clock over them alone. The line of the counts tells
        // tools/gpu-calibration/fsr-passes.py which command buffers of a trace are the warm-up.
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        output.WriteLine($"warm-up batches: {WarmUpBatches}, timed batches: {TimedBatches}");
        bench.TimedUpscales(firstBatch: 0, batches: WarmUpBatches, batchSize: Batch, batchesInFlight: InFlight);
        double[] frames = TimedSpan.Run(() =>
            bench.TimedUpscales(
                firstBatch: WarmUpBatches,
                batches: TimedBatches,
                batchSize: Batch,
                batchesInFlight: InFlight
            )
        );

        double[] sorted = [.. frames.Order()];
        CultureInfo inv = CultureInfo.InvariantCulture;
        output.WriteLine(
            string.Create(
                inv,
                $"FSR 3.1 in batches of {Batch}, maximum {largestSize.Width} x {largestSize.Height}, active {activeSize.Width} x {activeSize.Height}, output 1920 x 1080 on {bench.Device.DeviceName}: median {sorted[sorted.Length / 2]:F3} ms, 10th percentile {sorted[sorted.Length / 10]:F3} ms, 90th percentile {sorted[sorted.Length * 9 / 10]:F3} ms over {sorted.Length} batches"
            )
        );
        // Every batch's frame time in the order of the batches, for tools/fsr3-timing/analyze.py.
        output.WriteLine("frame times, ms: " + string.Join(' ', frames.Select(time => time.ToString("F4", inv))));
        sorted[0].Should().BePositive();
    }

    [Fact]
    public void TheLargestWindowAllocationHandlesTheControllersActiveSizesUnderValidation()
    {
        using var bench = new Fsr3Bench(new Extent2D(1920, 1080), new Extent2D(1920, 1080));
        bench.FillPattern();
        Extent2D[] sizes = [new(1920, 1080), new(1280, 720), new(960, 536), new(1920, 1080)];
        for (int frame = 0; frame < sizes.Length; frame++)
        {
            bench.RenderSize = sizes[frame];
            bench.Upscale(frame, reset: frame == 0);
        }

        bench
            .ReadOutput()
            .Should()
            .OnlyContain(pixel => float.IsFinite(pixel.X) && float.IsFinite(pixel.Y) && float.IsFinite(pixel.Z));
        bench.Device.Log.Snapshot().Should().BeEmpty();
    }

    // The mean over the pixels of the summed absolute differences of red, green and blue.
    private static float MeanDifference(Vector4[] pixels, Vector4[] others) =>
        pixels
            .Zip(others)
            .Average(pair =>
                MathF.Abs(pair.First.X - pair.Second.X)
                + MathF.Abs(pair.First.Y - pair.Second.Y)
                + MathF.Abs(pair.First.Z - pair.Second.Z)
            );

    // The outputs side by side, left to right, two stops down and sRGB-encoded, so that the red squares at 3 stay below
    // white and FLIP sees a change of the history in them.
    private static Capture Panels(List<Vector4[]> outputs, Extent2D size)
    {
        static byte Encoded(float linear)
        {
            float c = Math.Clamp(linear / 4f, 0f, 1f);
            float srgb = c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;
            return (byte)MathF.Round(srgb * 255f);
        }

        int width = (int)size.Width;
        int height = (int)size.Height;
        return TestImages.Fill(
            width: width * outputs.Count,
            height: height,
            color: (x, y) =>
            {
                Vector4 pixel = outputs[x / width][y * width + x % width];
                return (Encoded(pixel.X), Encoded(pixel.Y), Encoded(pixel.Z));
            }
        );
    }

    // FSR keeps its history in half floats. 1e-3 is two half-float steps at 0.5, four at 0.25 and eight at 0.125.
    private static void ShouldAllBeTheConstant(Vector4[] pixels)
    {
        const float Tolerance = 1e-3f;
        pixels
            .Should()
            .AllSatisfy(pixel =>
            {
                pixel.X.Should().BeApproximately(Constant.X, Tolerance);
                pixel.Y.Should().BeApproximately(Constant.Y, Tolerance);
                pixel.Z.Should().BeApproximately(Constant.Z, Tolerance);
            });
    }

    /// <summary>
    /// A validating headless device, the inputs of one upscale at the largest render size, an output and an upscaler,
    /// which records each frame alone and waits for it. The upscaler has 2 frames in flight, as the renderer has by
    /// default, unless a timing in batches asks for one per upscale of its batch.
    /// </summary>
    internal sealed unsafe class Fsr3Bench : IDisposable
    {
        private readonly DeviceAllocator allocator;
        private readonly ShaderLibrary shaders;
        private readonly CommandPool pool;
        private readonly Fence fence;
        private readonly GpuImage color;
        private readonly GpuImage opaqueOnly;
        private readonly GpuImage reactive;
        private readonly GpuImage depth;
        private readonly GpuImage motion;
        private readonly GpuImage output;
        private readonly GpuBuffer exposure;
        private readonly GpuBuffer readback;
        private readonly int framesInFlight;

        // Mip level 0 and layer 0 of a color image, for a copy.
        private static readonly ImageSubresourceLayers Level0 = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            LayerCount = 1,
        };

        public Fsr3Bench(Extent2D largestRenderSize, Extent2D outputSize, bool validation = true)
            : this(
                largestRenderSize: largestRenderSize,
                outputSize: outputSize,
                validation: validation,
                framesInFlight: 2
            ) { }

        public Fsr3Bench(Extent2D largestRenderSize, Extent2D outputSize, bool validation, int framesInFlight)
        {
            this.framesInFlight = framesInFlight;
            Device = VulkanDevice.Create(validation: validation, surfaceExtensions: [], createSurface: null);
            allocator = new DeviceAllocator(Device);
            shaders = new ShaderLibrary(Device, ShaderLibrary.EngineShaders);
            LargestRenderSize = largestRenderSize;
            RenderSize = largestRenderSize;
            color = CreateImage(largestRenderSize, Format.R16G16B16A16Sfloat, "fsr color");
            opaqueOnly = CreateImage(largestRenderSize, Format.R16G16B16A16Sfloat, "fsr opaque only");
            reactive = CreateImage(largestRenderSize, Format.R8Unorm, "fsr reactive");
            depth = CreateImage(largestRenderSize, Format.R32Sfloat, "fsr depth");
            motion = CreateImage(largestRenderSize, Format.R16G16B16A16Sfloat, "fsr motion");
            output = CreateImage(outputSize, Format.R16G16B16A16Sfloat, "fsr output");
            exposure = GpuBuffer.Create(
                device: Device,
                allocator: allocator,
                size: 16,
                usage: BufferUsageFlags.StorageBufferBit,
                memoryUsage: MemoryUsage.Upload,
                name: "fsr test exposure"
            );
            SetExposure(stops: 0, previousStops: 0);
            readback = GpuBuffer.Create(
                device: Device,
                allocator: allocator,
                size: (ulong)outputSize.Width * outputSize.Height * 8,
                usage: BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                memoryUsage: MemoryUsage.Readback,
                name: "fsr test readback"
            );
            Upscaler = CreateUpscaler(outputSize);
            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
                QueueFamilyIndex = Device.QueueFamily,
            };
            Device
                .Vk.CreateCommandPool(
                    device: Device.Device,
                    pCreateInfo: &poolInfo,
                    pAllocator: null,
                    pCommandPool: out pool
                )
                .Check("vkCreateCommandPool");
            var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            Device
                .Vk.CreateFence(device: Device.Device, pCreateInfo: &fenceInfo, pAllocator: null, pFence: out fence)
                .Check("vkCreateFence");
        }

        public VulkanDevice Device { get; }
        public Fsr3Upscaler Upscaler { get; private set; }

        /// <summary>The barriers of every submission, which carry each image's state from one to the next.</summary>
        public BarrierBatch Barriers { get; } = new();
        public Extent2D LargestRenderSize { get; }

        /// <summary>The render size of the next upscales: the largest render size until a test sets it.</summary>
        public Extent2D RenderSize { get; set; }

        /// <summary>Fills the color and the opaque-only image with one value, the depth with another, and the motion with 0.</summary>
        public void Fill(Vector4 value) => Fill(colorValue: value, opaqueOnlyValue: value);

        /// <summary>Fills the color and the opaque-only image with a value each, the depth with one, and the motion with 0.</summary>
        public void Fill(Vector4 colorValue, Vector4 opaqueOnlyValue) =>
            Submit(commandBuffer =>
            {
                Clear(commandBuffer, color, colorValue);
                Clear(commandBuffer, opaqueOnly, opaqueOnlyValue);
                Clear(commandBuffer, depth, new Vector4(0.5f));
                Clear(commandBuffer, motion, Vector4.Zero);
            });

        /// <summary>
        /// Fills the color and the opaque-only image with a pattern of colored squares, the depth with two depths and the
        /// motion with 0, so that every pass of FSR sees edges. <paramref name="scale"/> multiplies every color, and
        /// <paramref name="inverted"/> swaps the squares of the two colors.
        /// </summary>
        public void FillPattern(float scale = 1f, bool inverted = false)
        {
            int pixels = (int)(LargestRenderSize.Width * LargestRenderSize.Height);
            var colors = new Half[pixels * 4];
            float[] depths = new float[pixels];
            for (int y = 0; y < LargestRenderSize.Height; y++)
            for (int x = 0; x < LargestRenderSize.Width; x++)
            {
                int pixel = y * (int)LargestRenderSize.Width + x;
                bool square = (x / 8 + y / 8) % 2 == (inverted ? 1 : 0);
                colors[pixel * 4 + 0] = (Half)(scale * (square ? 3f : 0.1f));
                colors[pixel * 4 + 1] = (Half)(scale * x / 64f);
                colors[pixel * 4 + 2] = (Half)(scale * y / 48f);
                colors[pixel * 4 + 3] = (Half)1f;
                depths[pixel] = square ? 0.25f : 0.0625f;
            }

            Upload(color, colors);
            Upload(opaqueOnly, colors);
            Upload(depth, depths);
            Submit(commandBuffer => Clear(commandBuffer, motion, Vector4.Zero));
        }

        /// <summary>Writes the exposure buffer: this frame's stops and the last frame's.</summary>
        public void SetExposure(float stops, float previousStops) => exposure.Write([stops, previousStops, 0f, 0f]);

        /// <summary>Records the upscale of draw <paramref name="index"/>, with AMD's jitter of that draw, and waits for it.</summary>
        public void Upscale(int index, bool reset) =>
            Submit(commandBuffer => Upscaler.Record(commandBuffer, Barriers, Inputs(index, reset)));

        /// <summary>The inputs of draw <paramref name="index"/>.</summary>
        public Fsr3Inputs Inputs(int index, bool reset)
        {
            int phases = Fsr3Upscaler.JitterPhaseCount(RenderSize.Width, output.Extent.Width);
            return new()
            {
                Color = color,
                OpaqueOnly = opaqueOnly,
                Reactive = reactive,
                TransparencyAndComposition = null,
                Depth = depth,
                Motion = motion,
                Output = output,
                Exposure = exposure,
                Frame = new()
                {
                    RenderSize = RenderSize,
                    Jitter = -Fsr3Upscaler.JitterOffset(index, phases),
                    FrameMilliseconds = 1000f / 60f,
                    NearMeters = 0.05f,
                    VerticalFov = MathF.PI / 3f,
                    Reset = reset,
                },
            };
        }

        /// <summary>
        /// The mean GPU time in ms of an upscale in each of <paramref name="batches"/> submissions of
        /// <paramref name="batchSize"/> upscales, by the span of the batch's timestamps: a stamp before each upscale and
        /// one after the last, so every command before the last stamp from the first. The batches continue the bench's
        /// draws after <paramref name="firstBatch"/> batches of <paramref name="batchSize"/>, and only draw 0 resets the
        /// accumulation. Up to <paramref name="batchesInFlight"/> batches wait on the queue at once, so that the GPU starts
        /// the next batch while the CPU records the one after it. 1 waits for each batch before the next, and a batch of
        /// 1 in flight alone is the time of each upscale alone.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchSize"/> times
        /// <paramref name="batchesInFlight"/> is more than the upscaler's frames in flight. Each upscale writes the sets of
        /// the upscale that many before it, and a set that a command buffer in flight binds must not change
        /// (VUID-vkEndCommandBuffer-commandBuffer-00059, VUID-vkUpdateDescriptorSets-None-03047).</exception>
        public double[] TimedUpscales(int firstBatch, int batches, int batchSize, int batchesInFlight)
        {
            if (batchSize * batchesInFlight > framesInFlight)
                throw new ArgumentOutOfRangeException(
                    nameof(batchSize),
                    $"{batchesInFlight} batches of {batchSize} upscales in flight need as many frames in flight as their "
                        + $"upscales, and the upscaler has {framesInFlight}."
                );
            Vk vk = Device.Vk;
            uint stampCount = (uint)batchSize + 1;
            var queries = new QueryPool[batchesInFlight];
            var fences = new Fence[batchesInFlight];
            var commandBuffers = new CommandBuffer[batchesInFlight];
            try
            {
                CreateSlots(stampCount, queries, fences);
                double[] times = new double[batches];
                // Batch n goes into slot n % batchesInFlight. Before a batch takes its slot, the batch before it in that
                // slot finishes, so its sets, which this batch's upscales write again, are free, and its stamps are read.
                for (int batch = 0; batch < batches + batchesInFlight; batch++)
                {
                    int slot = batch % batchesInFlight;
                    int done = batch - batchesInFlight;
                    if (done >= 0 && done < batches)
                    {
                        Finish(commandBuffers[slot], fences[slot]);
                        times[done] = MillisecondsFromFirstToLastStamp(queries[slot], stampCount) / batchSize;
                    }
                    if (batch >= batches)
                        continue;
                    int firstDraw = (firstBatch + batch) * batchSize;
                    QueryPool stamps = queries[slot];
                    commandBuffers[slot] = Start(
                        commandBuffer =>
                            RecordBatch(
                                commandBuffer: commandBuffer,
                                stamps: stamps,
                                firstDraw: firstDraw,
                                batchSize: batchSize
                            ),
                        fences[slot]
                    );
                }

                return times;
            }
            finally
            {
                // A failure can leave batches on the queue, which must finish before their pools and fences go.
                Device.WaitIdle();
                for (int slot = 0; slot < batchesInFlight; slot++)
                {
                    vk.DestroyQueryPool(Device.Device, queries[slot], null);
                    vk.DestroyFence(Device.Device, fences[slot], null);
                }
            }
        }

        /// <summary>
        /// Creates a pool of <paramref name="stampCount"/> timestamps and an unsignaled fence for each slot of
        /// <paramref name="queries"/> and <paramref name="fences"/>.
        /// </summary>
        private void CreateSlots(uint stampCount, QueryPool[] queries, Fence[] fences)
        {
            Vk vk = Device.Vk;
            var info = new QueryPoolCreateInfo
            {
                SType = StructureType.QueryPoolCreateInfo,
                QueryType = QueryType.Timestamp,
                QueryCount = stampCount,
            };
            var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            for (int slot = 0; slot < queries.Length; slot++)
            {
                vk.CreateQueryPool(
                        device: Device.Device,
                        pCreateInfo: &info,
                        pAllocator: null,
                        pQueryPool: out queries[slot]
                    )
                    .Check("vkCreateQueryPool");
                vk.CreateFence(
                        device: Device.Device,
                        pCreateInfo: &fenceInfo,
                        pAllocator: null,
                        pFence: out fences[slot]
                    )
                    .Check("vkCreateFence");
            }
        }

        /// <summary>
        /// Records one batch: the upscales of <paramref name="batchSize"/> draws from <paramref name="firstDraw"/>, with a
        /// stamp of <paramref name="stamps"/> before each and one after the last.
        /// </summary>
        private void RecordBatch(CommandBuffer commandBuffer, QueryPool stamps, int firstDraw, int batchSize)
        {
            Vk vk = Device.Vk;
            vk.CmdResetQueryPool(
                commandBuffer: commandBuffer,
                queryPool: stamps,
                firstQuery: 0,
                queryCount: (uint)batchSize + 1
            );
            for (int frame = 0; frame <= batchSize; frame++)
            {
                vk.CmdWriteTimestamp2(
                    commandBuffer: commandBuffer,
                    stage: PipelineStageFlags2.AllCommandsBit,
                    queryPool: stamps,
                    query: (uint)frame
                );
                int draw = firstDraw + frame;
                if (frame < batchSize)
                    Upscaler.Record(commandBuffer, Barriers, Inputs(draw, reset: draw == 0));
            }
        }

        /// <summary>
        /// The time in ms from the first of the <paramref name="stampCount"/> timestamps of <paramref name="queries"/> to
        /// the last.
        /// </summary>
        private double MillisecondsFromFirstToLastStamp(QueryPool queries, uint stampCount)
        {
            Span<ulong> stamps = stackalloc ulong[(int)stampCount];
            fixed (ulong* data = stamps)
                Device
                    .Vk.GetQueryPoolResults(
                        device: Device.Device,
                        queryPool: queries,
                        firstQuery: 0,
                        queryCount: stampCount,
                        dataSize: stampCount * sizeof(ulong),
                        pData: data,
                        stride: sizeof(ulong),
                        flags: QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit
                    )
                    .Check("vkGetQueryPoolResults");
            return Device.Timestamps.Milliseconds(stamps[0], stamps[^1]);
        }

        /// <summary>The output's pixels, row by row.</summary>
        public Vector4[] ReadOutput()
        {
            int pixels = (int)(output.Extent.Width * output.Extent.Height);
            Span<Half> halves = ReadBack(output).AsSpan<Half>(pixels * 4);
            var result = new Vector4[pixels];
            for (int pixel = 0; pixel < pixels; pixel++)
                result[pixel] = new(
                    x: (float)halves[pixel * 4],
                    y: (float)halves[pixel * 4 + 1],
                    z: (float)halves[pixel * 4 + 2],
                    w: (float)halves[pixel * 4 + 3]
                );
            return result;
        }

        /// <summary>The reactive mask's pixels, row by row, from 0 to 1.</summary>
        public float[] ReadReactive()
        {
            int pixels = (int)(reactive.Extent.Width * reactive.Extent.Height);
            return [.. ReadBack(reactive).AsSpan<byte>(pixels).ToArray().Select(texel => texel / 255f)];
        }

        /// <summary>The words of <paramref name="level"/> of <paramref name="image"/>, row by row, one per texel of 4 bytes.</summary>
        public uint[] ReadWords(GpuImage image, uint level)
        {
            Extent2D extent = LevelExtent(image, level);
            int bytes = (int)(extent.Width * extent.Height * FormatBytes(image.Format));
            return ReadBack(image, level).AsSpan<uint>(bytes / sizeof(uint)).ToArray();
        }

        /// <summary>Fills every level of <paramref name="image"/> with <paramref name="value"/>.</summary>
        public void FillEveryLevel(GpuImage image, Vector4 value) =>
            Submit(commandBuffer => Clear(commandBuffer, image, value));

        /// <summary>Copies <paramref name="level"/> of <paramref name="image"/> to the readback buffer and waits for it.</summary>
        private GpuBuffer ReadBack(GpuImage image, uint level = 0)
        {
            Submit(commandBuffer =>
            {
                Barriers.Image(image, ImageUse.TransferSource);
                Barriers.Buffer(readback, BufferUse.TransferDestination);
                Barriers.Flush(Device.Vk, commandBuffer);
                Extent2D extent = LevelExtent(image, level);
                var region = new BufferImageCopy
                {
                    ImageSubresource = Level0 with { MipLevel = level },
                    ImageExtent = new(extent.Width, extent.Height, 1),
                };
                Device.Vk.CmdCopyImageToBuffer(
                    commandBuffer: commandBuffer,
                    srcImage: image.Handle,
                    srcImageLayout: ImageLayout.TransferSrcOptimal,
                    dstBuffer: readback.Handle,
                    regionCount: 1,
                    pRegions: &region
                );
                Barriers.Buffer(readback, BufferUse.HostRead);
                Barriers.Flush(Device.Vk, commandBuffer);
            });
            return readback;
        }

        /// <summary>The ratio of the pre-exposures that the exposure step wrote into FSR's constants.</summary>
        public float ReadDeltaPreExposure()
        {
            ulong offset = (ulong)
                System.Runtime.InteropServices.Marshal.OffsetOf<Fsr3Constants>(nameof(Fsr3Constants.DeltaPreExposure));
            Submit(commandBuffer =>
            {
                Barriers.Buffer(Upscaler.Constants, BufferUse.TransferSource);
                Barriers.Buffer(readback, BufferUse.TransferDestination);
                Barriers.Flush(Device.Vk, commandBuffer);
                var region = new BufferCopy(offset, 0, sizeof(float));
                Device.Vk.CmdCopyBuffer(
                    commandBuffer: commandBuffer,
                    srcBuffer: Upscaler.Constants.Handle,
                    dstBuffer: readback.Handle,
                    regionCount: 1,
                    pRegions: &region
                );
                Barriers.Buffer(readback, BufferUse.HostRead);
                Barriers.Flush(Device.Vk, commandBuffer);
            });
            return readback.AsSpan<float>(1)[0];
        }

        /// <summary>Records <paramref name="record"/> into a new command buffer, submits it and waits for it.</summary>
        public void Submit(Action<CommandBuffer> record) => Finish(Start(record, fence), fence);

        /// <summary>
        /// Records <paramref name="record"/> into a new command buffer and submits it with <paramref name="signal"/>,
        /// which must be unsignaled, without a wait. <see cref="Finish"/> waits for it.
        /// </summary>
        private CommandBuffer Start(Action<CommandBuffer> record, Fence signal)
        {
            Vk vk = Device.Vk;
            var allocate = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = pool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            vk.AllocateCommandBuffers(Device.Device, &allocate, out CommandBuffer commandBuffer)
                .Check("vkAllocateCommandBuffers");
            var begin = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            vk.BeginCommandBuffer(commandBuffer, &begin).Check("vkBeginCommandBuffer");
            record(commandBuffer);
            vk.EndCommandBuffer(commandBuffer).Check("vkEndCommandBuffer");
            var submit = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
            };
            vk.QueueSubmit(queue: Device.Queue, submitCount: 1, pSubmits: &submit, fence: signal)
                .Check("vkQueueSubmit");
            return commandBuffer;
        }

        /// <summary>
        /// Waits for <paramref name="signal"/>, the fence of <paramref name="commandBuffer"/>'s submission, resets it
        /// and frees the command buffer.
        /// </summary>
        private void Finish(CommandBuffer commandBuffer, Fence signal)
        {
            Vk vk = Device.Vk;
            vk.WaitForFences(
                    device: Device.Device,
                    fenceCount: 1,
                    pFences: &signal,
                    waitAll: true,
                    timeout: ulong.MaxValue
                )
                .Check("vkWaitForFences");
            vk.ResetFences(Device.Device, 1, &signal).Check("vkResetFences");
            vk.FreeCommandBuffers(
                device: Device.Device,
                commandPool: pool,
                commandBufferCount: 1,
                pCommandBuffers: &commandBuffer
            );
        }

        public void Dispose()
        {
            Device.WaitIdle();
            Upscaler.Dispose();
            Device.Vk.DestroyFence(Device.Device, fence, null);
            Device.Vk.DestroyCommandPool(Device.Device, pool, null);
            readback.Dispose();
            exposure.Dispose();
            output.Dispose();
            motion.Dispose();
            depth.Dispose();
            reactive.Dispose();
            opaqueOnly.Dispose();
            color.Dispose();
            shaders.Dispose();
            allocator.Dispose();
            Device.Dispose();
        }

        /// <summary>An exposure buffer in GPU memory, which only the GPU writes.</summary>
        public GpuBuffer CreateGpuExposureBuffer() =>
            GpuBuffer.Create(
                device: Device,
                allocator: allocator,
                size: 16,
                usage: BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                memoryUsage: MemoryUsage.GpuOnly,
                name: "fsr test exposure in GPU memory"
            );

        /// <summary>Records a write of this frame's stops and the last frame's into <paramref name="buffer"/>.</summary>
        public void WriteOnGpu(CommandBuffer commandBuffer, GpuBuffer buffer, float stops, float previousStops)
        {
            Barriers.Buffer(buffer, BufferUse.TransferDestination);
            Barriers.Flush(Device.Vk, commandBuffer);
            float* values = stackalloc float[] { stops, previousStops, 0f, 0f };
            Device.Vk.CmdUpdateBuffer(
                commandBuffer: commandBuffer,
                dstBuffer: buffer.Handle,
                dstOffset: 0,
                dataSize: 4 * sizeof(float),
                pData: values
            );
        }

        /// <summary>Replaces the upscaler with a new one, which the allocator gives the memory of the old one.</summary>
        public void ReplaceUpscaler()
        {
            Device.WaitIdle();
            Upscaler.Dispose();
            Upscaler = CreateUpscaler(output.Extent);
        }

        private Fsr3Upscaler CreateUpscaler(Extent2D outputSize) =>
            new(
                device: Device,
                allocator: allocator,
                shaders: shaders,
                maxRenderSize: LargestRenderSize,
                outputSize: outputSize,
                framesInFlight: framesInFlight
            );

        /// <summary>An input or output image: sampled, stored, cleared, uploaded and read back.</summary>
        public GpuImage CreateImage(Extent2D extent, Format format, string name) =>
            GpuImage.Create(
                device: Device,
                allocator: allocator,
                extent: extent,
                format: format,
                usage: ImageUsageFlags.SampledBit
                    | ImageUsageFlags.StorageBit
                    | ImageUsageFlags.TransferDstBit
                    | ImageUsageFlags.TransferSrcBit,
                mipLevels: 1,
                name: name
            );

        private static Extent2D LevelExtent(GpuImage image, uint level) =>
            new(Math.Max(image.Extent.Width >> (int)level, 1), Math.Max(image.Extent.Height >> (int)level, 1));

        // The bytes of a texel of the formats that the tests read as words.
        private static uint FormatBytes(Format format) =>
            format switch
            {
                Format.R32Uint or Format.R16G16Sfloat => 4,
                _ => throw new ArgumentException($"The tests read no {format} image as words.", nameof(format)),
            };

        // Clears every level of the image.
        private void Clear(CommandBuffer commandBuffer, GpuImage image, Vector4 value)
        {
            Barriers.Image(image, ImageUse.TransferDestination, discard: true);
            Barriers.Flush(Device.Vk, commandBuffer);
            var clear = new ClearColorValue
            {
                Float32_0 = value.X,
                Float32_1 = value.Y,
                Float32_2 = value.Z,
                Float32_3 = value.W,
            };
            var range = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LevelCount = image.MipLevels,
                LayerCount = 1,
            };
            Device.Vk.CmdClearColorImage(
                commandBuffer: commandBuffer,
                image: image.Handle,
                imageLayout: ImageLayout.TransferDstOptimal,
                pColor: &clear,
                rangeCount: 1,
                pRanges: &range
            );
        }

        private void Upload<T>(GpuImage image, T[] texels)
            where T : unmanaged
        {
            ulong bytes = (ulong)(texels.Length * sizeof(T));
            using var staging = GpuBuffer.Create(
                device: Device,
                allocator: allocator,
                size: bytes,
                usage: BufferUsageFlags.TransferSrcBit,
                memoryUsage: MemoryUsage.Upload,
                name: "fsr test upload"
            );
            staging.Write(texels);
            Submit(commandBuffer =>
            {
                Barriers.Image(image, ImageUse.TransferDestination, discard: true);
                Barriers.Flush(Device.Vk, commandBuffer);
                var region = new BufferImageCopy
                {
                    ImageSubresource = Level0,
                    ImageExtent = new(image.Extent.Width, image.Extent.Height, 1),
                };
                Buffer source = staging.Handle;
                Device.Vk.CmdCopyBufferToImage(
                    commandBuffer: commandBuffer,
                    srcBuffer: source,
                    dstImage: image.Handle,
                    dstImageLayout: ImageLayout.TransferDstOptimal,
                    regionCount: 1,
                    pRegions: &region
                );
            });
        }
    }
}
