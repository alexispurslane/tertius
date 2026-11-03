using System.Numerics;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="Fsr3Upscaler"/> on the GPU, over AMD's pinned shaders (docs/plans/temporal-renderer.md, "FSR 3.1"): an
/// upscale of a known input under the validation layer with its synchronization checks, a reset, the exposure step and
/// the determinism of the result. Each test records one frame per submission and waits for it.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class Fsr3UpscalerTests
{
    private static readonly Extent2D LargestRenderSize = new(64, 48);
    private static readonly Extent2D OutputSize = new(96, 72);

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

        // The first frames warm the caches and the driver's pipelines.
        double[] times = [.. bench.TimedUpscales(count: 360).Skip(60).Order()];

        double median = times[times.Length / 2];
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"FSR 3.1 from 1280 x 720 to 1920 x 1080 on {bench.Device.DeviceName}: median {median:F3} ms, "
                + $"10th percentile {times[times.Length / 10]:F3} ms, 90th percentile {times[times.Length * 9 / 10]:F3} ms "
                + $"over {times.Length} frames"
        );
        median.Should().BePositive();
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
    /// which records each frame alone and waits for it.
    /// </summary>
    internal sealed unsafe class Fsr3Bench : IDisposable
    {
        private readonly DeviceAllocator allocator;
        private readonly ShaderLibrary shaders;
        private readonly BarrierBatch barriers = new();
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

        // Mip level 0 and layer 0 of a color image, for a copy.
        private static readonly ImageSubresourceLayers Level0 = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            LayerCount = 1,
        };

        public Fsr3Bench(Extent2D largestRenderSize, Extent2D outputSize, bool validation = true)
        {
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
            Upscaler = new Fsr3Upscaler(
                device: Device,
                allocator: allocator,
                shaders: shaders,
                maxRenderSize: largestRenderSize,
                outputSize: outputSize,
                framesInFlight: 2
            );
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
        public Fsr3Upscaler Upscaler { get; }
        public Extent2D LargestRenderSize { get; }

        /// <summary>The render size of the next upscales: the largest render size until a test sets it.</summary>
        public Extent2D RenderSize { get; set; }

        /// <summary>Fills the color, the opaque-only image and the depth with one value each, and the motion with 0.</summary>
        public void Fill(Vector4 value) =>
            Submit(commandBuffer =>
            {
                Clear(commandBuffer, color, value);
                Clear(commandBuffer, opaqueOnly, value);
                Clear(commandBuffer, depth, new Vector4(0.5f));
                Clear(commandBuffer, motion, Vector4.Zero);
            });

        /// <summary>
        /// Fills the color and the opaque-only image with a pattern of colored squares, the depth with two depths and the
        /// motion with 0, so that every pass of FSR sees edges.
        /// </summary>
        public void FillPattern()
        {
            int pixels = (int)(LargestRenderSize.Width * LargestRenderSize.Height);
            var colors = new Half[pixels * 4];
            float[] depths = new float[pixels];
            for (int y = 0; y < LargestRenderSize.Height; y++)
            for (int x = 0; x < LargestRenderSize.Width; x++)
            {
                int pixel = y * (int)LargestRenderSize.Width + x;
                bool square = (x / 8 + y / 8) % 2 == 0;
                colors[pixel * 4 + 0] = (Half)(square ? 3f : 0.1f);
                colors[pixel * 4 + 1] = (Half)(x / 64f);
                colors[pixel * 4 + 2] = (Half)(y / 48f);
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
            Submit(commandBuffer => Upscaler.Record(commandBuffer, barriers, Inputs(index, reset)));

        /// <summary>The inputs of draw <paramref name="index"/>.</summary>
        public Fsr3Inputs Inputs(int index, bool reset)
        {
            int phases = Fsr3Upscaler.JitterPhaseCount(RenderSize.Width, output.Extent.Width);
            return new()
            {
                Color = color,
                OpaqueOnly = opaqueOnly,
                Reactive = reactive,
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
        /// The GPU time in ms of each of <paramref name="count"/> upscales, the first of which resets, by timestamps around
        /// Record: every command before the second stamp, from the first.
        /// </summary>
        public double[] TimedUpscales(int count)
        {
            Vk vk = Device.Vk;
            var info = new QueryPoolCreateInfo
            {
                SType = StructureType.QueryPoolCreateInfo,
                QueryType = QueryType.Timestamp,
                QueryCount = 2,
            };
            vk.CreateQueryPool(
                    device: Device.Device,
                    pCreateInfo: &info,
                    pAllocator: null,
                    pQueryPool: out QueryPool queries
                )
                .Check("vkCreateQueryPool");
            try
            {
                double[] times = new double[count];
                for (int index = 0; index < count; index++)
                {
                    int draw = index;
                    Submit(commandBuffer =>
                    {
                        vk.CmdResetQueryPool(
                            commandBuffer: commandBuffer,
                            queryPool: queries,
                            firstQuery: 0,
                            queryCount: 2
                        );
                        vk.CmdWriteTimestamp2(
                            commandBuffer: commandBuffer,
                            stage: PipelineStageFlags2.AllCommandsBit,
                            queryPool: queries,
                            query: 0
                        );
                        Upscaler.Record(commandBuffer, barriers, Inputs(draw, reset: draw == 0));
                        vk.CmdWriteTimestamp2(
                            commandBuffer: commandBuffer,
                            stage: PipelineStageFlags2.AllCommandsBit,
                            queryPool: queries,
                            query: 1
                        );
                    });
                    times[index] = MillisecondsBetweenStamps(queries);
                }

                return times;
            }
            finally
            {
                vk.DestroyQueryPool(Device.Device, queries, null);
            }
        }

        /// <summary>The time in ms from the first timestamp of <paramref name="queries"/> to the second.</summary>
        private double MillisecondsBetweenStamps(QueryPool queries)
        {
            Span<ulong> stamps = stackalloc ulong[2];
            fixed (ulong* data = stamps)
                Device
                    .Vk.GetQueryPoolResults(
                        device: Device.Device,
                        queryPool: queries,
                        firstQuery: 0,
                        queryCount: 2,
                        dataSize: 2 * sizeof(ulong),
                        pData: data,
                        stride: sizeof(ulong),
                        flags: QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit
                    )
                    .Check("vkGetQueryPoolResults");
            return Device.Timestamps.Milliseconds(stamps[0], stamps[1]);
        }

        /// <summary>The output's pixels, row by row.</summary>
        public Vector4[] ReadOutput()
        {
            Submit(commandBuffer =>
            {
                barriers.Image(output, ImageUse.TransferSource);
                barriers.Buffer(readback, BufferUse.TransferDestination);
                barriers.Flush(Device.Vk, commandBuffer);
                var region = new BufferImageCopy
                {
                    ImageSubresource = Level0,
                    ImageExtent = new(output.Extent.Width, output.Extent.Height, 1),
                };
                Device.Vk.CmdCopyImageToBuffer(
                    commandBuffer: commandBuffer,
                    srcImage: output.Handle,
                    srcImageLayout: ImageLayout.TransferSrcOptimal,
                    dstBuffer: readback.Handle,
                    regionCount: 1,
                    pRegions: &region
                );
                barriers.Buffer(readback, BufferUse.HostRead);
                barriers.Flush(Device.Vk, commandBuffer);
            });
            int pixels = (int)(output.Extent.Width * output.Extent.Height);
            Span<Half> halves = readback.AsSpan<Half>(pixels * 4);
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

        /// <summary>The ratio of the pre-exposures that the exposure step wrote into FSR's constants.</summary>
        public float ReadDeltaPreExposure()
        {
            ulong offset = (ulong)
                System.Runtime.InteropServices.Marshal.OffsetOf<Fsr3Constants>(nameof(Fsr3Constants.DeltaPreExposure));
            Submit(commandBuffer =>
            {
                barriers.Buffer(Upscaler.Constants, BufferUse.TransferSource);
                barriers.Buffer(readback, BufferUse.TransferDestination);
                barriers.Flush(Device.Vk, commandBuffer);
                var region = new BufferCopy(offset, 0, sizeof(float));
                Device.Vk.CmdCopyBuffer(
                    commandBuffer: commandBuffer,
                    srcBuffer: Upscaler.Constants.Handle,
                    dstBuffer: readback.Handle,
                    regionCount: 1,
                    pRegions: &region
                );
                barriers.Buffer(readback, BufferUse.HostRead);
                barriers.Flush(Device.Vk, commandBuffer);
            });
            return readback.AsSpan<float>(1)[0];
        }

        /// <summary>Records <paramref name="record"/> into a new command buffer, submits it and waits for it.</summary>
        public void Submit(Action<CommandBuffer> record)
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
            Fence wait = fence;
            vk.QueueSubmit(queue: Device.Queue, submitCount: 1, pSubmits: &submit, fence: wait).Check("vkQueueSubmit");
            vk.WaitForFences(
                    device: Device.Device,
                    fenceCount: 1,
                    pFences: &wait,
                    waitAll: true,
                    timeout: ulong.MaxValue
                )
                .Check("vkWaitForFences");
            vk.ResetFences(Device.Device, 1, &wait).Check("vkResetFences");
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

        private void Clear(CommandBuffer commandBuffer, GpuImage image, Vector4 value)
        {
            barriers.Image(image, ImageUse.TransferDestination, discard: true);
            barriers.Flush(Device.Vk, commandBuffer);
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
                LevelCount = 1,
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
                barriers.Image(image, ImageUse.TransferDestination, discard: true);
                barriers.Flush(Device.Vk, commandBuffer);
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
