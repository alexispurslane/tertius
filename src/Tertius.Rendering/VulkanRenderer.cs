using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

namespace Tertius.Rendering;

/// <summary>
/// The deferred Vulkan renderer. It draws a flat <see cref="RenderSnapshot"/> and knows nothing about reference
/// frames: every position is relative to the render origin, which is the camera position at the current tick.
/// </summary>
/// <remarks>
/// <para>One frame, in recording order (see <see cref="FrameGraph"/>):</para>
/// <list type="number">
/// <item>Uploads queued by the Create methods, through per-frame staging memory.</item>
/// <item>CPU batching (<see cref="SceneBatcher"/>): interpolation by alpha, culling, depth ranges, sorting, one indirect
/// command per (range, mesh) run. The impostor manager (<see cref="IInstanceProcessor"/>) takes the objects it draws
/// as impostors out of the batch and chooses this frame's captures.</item>
/// <item>Sun shadow maps (<see cref="ShadowPass"/>): four cascades over the first 2 km, casters from the whole snapshot.</item>
/// <item>Impostor captures (<see cref="ImpostorPass"/>): up to four objects, chosen by <see cref="ImpostorManager"/>
/// during batching, each rendered alone into its tile of the impostor atlas.</item>
/// <item>G-buffer: far, middle and near depth ranges, each one vkCmdDrawIndexedIndirect, plus one instanced draw of
/// the impostor billboards in that range.</item>
/// <item>Lighting: one fullscreen pass into the HDR target.</item>
/// <item>Transparent objects: forward-shaded, blended, back to front.</item>
/// <item>Tone mapping into the swapchain image or the offscreen output.</item>
/// <item>The overlay (<see cref="OutlinePass"/>): the frame outlines of the snapshot, blended over the tone-mapped image.</item>
/// <item>Capture readback, when requested. After the GPU finishes the frame, <see cref="Render"/> hands the pixels to
/// the capture's <see cref="ICaptureSink"/>.</item>
/// </list>
/// <para>Threading: the Create methods, <see cref="Resize"/> and <see cref="CaptureNextFrame"/> may be called from
/// any thread. <see cref="Render"/> and <see cref="Dispose"/> belong to the render thread.</para>
/// <para>Ids: each Create method numbers its ids from 0, and none of them is ever None. On an instance,
/// <see cref="MeshId.None"/> draws nothing and <see cref="MaterialId.None"/> draws with the default material (a white
/// non-metal of roughness 0.5). In a material, a <see cref="TextureId.None"/> slot uses the built-in default for that
/// slot. These defaults live in slots that no id can name.</para>
/// </remarks>
public sealed unsafe class VulkanRenderer : IRenderer
{
    private readonly VulkanDevice device;
    private readonly ValidationLog log;
    private readonly bool headless;
    private readonly Stack<IDisposable> owned = new();
    private readonly DeferredReleases releases;
    private readonly DescriptorLayouts layouts;
    private readonly IOutputTarget output;
    private readonly FrameResources[] frames;
    private readonly UploadQueue uploads;
    private readonly GeometryStore geometry;
    private readonly TextureStore textures;
    private readonly MaterialStore materials;
    private readonly BrdfTableImages brdfTables;
    private readonly RenderTargets targets;
    private readonly ImpostorAtlas? impostorAtlas;
    private readonly BarrierBatch barriers = new();
    private readonly GpuTimer timer;
    private readonly FrameContext context;
    private IWindow? window;
    private Action<Vector2D<int>>? onFramebufferResize;
    private GpuBuffer? captureBuffer;
    private ICaptureSink? pendingCapture;
    private long pendingResize = -1;
    private long frameNumber;
    private bool disposed;

    private VulkanRenderer(
        RendererOptions options,
        VulkanDevice device,
        Func<VulkanDevice, DeviceAllocator, IOutputTarget> createOutput,
        bool headless
    )
    {
        this.device = device;
        this.headless = headless;
        log = device.Log;
        try
        {
            Allocator = Own(new DeviceAllocator(device));
            releases = Own(new DeferredReleases());
            ShaderLibrary shaders = Own(new ShaderLibrary(device));
            layouts = Own(new DescriptorLayouts(device, options.FramesInFlight));
            output = Own(createOutput(device, Allocator));
            geometry = Own(new GeometryStore(device, Allocator));
            textures = Own(new TextureStore(device, Allocator));
            materials = Own(new MaterialStore(device, Allocator));
            brdfTables = Own(new BrdfTableImages(device, Allocator));
            uploads = new((int)device.Properties.Limits.MaxImageDimension2D);
            Extent2D extent = output.Extent.Width > 0 && output.Extent.Height > 0 ? output.Extent : new(1, 1);
            targets = Own(new RenderTargets(device: device, allocator: Allocator, layouts: layouts, extent: extent));
            ShadowMap = Own(new ShadowMap(device, Allocator, options.ShadowMapSize));
            // Written once: the shadow map never changes, and resizing rewrites only the other G-buffer bindings.
            layouts.WriteImage(set: targets.GBufferSet, binding: 5, arrayElement: 0, image: ShadowMap.Image);
            frames = new FrameResources[options.FramesInFlight];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = Own(
                    new FrameResources(
                        device: device,
                        allocator: Allocator,
                        layouts: layouts,
                        textures: textures,
                        tables: brdfTables,
                        index: i
                    )
                );

            Graph.Add(
                FrameGraph.Stage.ShadowMaps,
                new ShadowPass(device: device, shaders: shaders, layouts: layouts, map: ShadowMap)
            );
            var geometryPass = new GeometryPass(device, shaders, layouts);
            Graph.Add(FrameGraph.Stage.GBuffer, geometryPass);
            if (options.Impostors)
            {
                impostorAtlas = Own(
                    new ImpostorAtlas(
                        device: device,
                        allocator: Allocator,
                        layouts: layouts,
                        tiles: options.ImpostorTiles
                    )
                );
                Impostors = new(options.ImpostorTiles);
                Batcher.Processors.Add(Impostors);
                var impostorPass = new ImpostorPass(
                    device: device,
                    shaders: shaders,
                    layouts: layouts,
                    atlas: impostorAtlas,
                    manager: Impostors
                );
                Graph.Add(FrameGraph.Stage.ImpostorCaptures, impostorPass);
                geometryPass.RangeDrawers.Add(impostorPass);
            }

            Graph.Add(FrameGraph.Stage.Lighting, new LightingPass(device, shaders, layouts));
            Graph.Add(FrameGraph.Stage.Transparent, new TransparentPass(device, shaders, layouts));
            Graph.Add(
                FrameGraph.Stage.ToneMap,
                new ToneMapPass(device: device, shaders: shaders, layouts: layouts, outputFormat: output.Format)
            );
            Graph.Add(
                FrameGraph.Stage.Overlay,
                new OutlinePass(device: device, shaders: shaders, layouts: layouts, outputFormat: output.Format)
            );
            Graph.Add(FrameGraph.Stage.Readback, new CapturePass());
            Own(Graph);
            timer = Own(new GpuTimer(device, options.FramesInFlight));

            context = new()
            {
                Device = device,
                Layouts = layouts,
                Targets = targets,
                Geometry = geometry,
                Materials = materials,
                Barriers = barriers,
                Shadows = ShadowMap,
                Timer = timer,
            };
        }
        catch
        {
            ReleaseOwned();
            device.Dispose();
            throw;
        }
    }

    /// <summary>Every validation warning and error so far, including those reported during <see cref="Dispose"/>.</summary>
    public IReadOnlyList<string> ValidationMessages => log.Snapshot();

    /// <summary>The physical device in use.</summary>
    public string DeviceName => device.DeviceName;

    /// <summary>Linear scale applied to the HDR image before tone mapping.</summary>
    public float Exposure { get; set; } = 1f;

    /// <summary>Statistics of the last <see cref="Render"/> call.</summary>
    public RenderStats LastFrameStats { get; private set; }

    /// <summary>The size of the image the renderer draws.</summary>
    public (int Width, int Height) OutputSize => ((int)output.Extent.Width, (int)output.Extent.Height);

    internal DeviceAllocator Allocator { get; }

    /// <summary>True when the device writes timestamps, so <see cref="RenderStats.GpuTimings"/> reports the passes.</summary>
    public bool TimesPasses => timer.Supported;

    internal ShadowMap ShadowMap { get; }

    /// <summary>The impostor manager, or null when impostors are off. A test and tooling hook.</summary>
    internal ImpostorManager? Impostors { get; }

    internal SceneBatcher Batcher { get; } = new();

    internal FrameGraph Graph { get; } = new();

    internal PreparedScene LastScene { get; } = new();

    /// <summary>Textures from <see cref="CreateTexture"/> on the GPU, not counting the built-in defaults.</summary>
    internal int TextureCount => textures.UserCount;

    /// <summary>Materials from <see cref="CreateMaterial"/> on the GPU, not counting the default material.</summary>
    internal int MaterialCount => materials.UserCount;

    internal int MeshCount => geometry.Meshes.Length;
    internal int PendingReleases => releases.Count;

    /// <summary>Creates a renderer that draws into an offscreen image of <see cref="RendererOptions.Width"/> by
    /// <see cref="RendererOptions.Height"/>.</summary>
    public static VulkanRenderer CreateHeadless(RendererOptions options) =>
        CreateOffscreen(
            options,
            (device, allocator, extent) =>
                new HeadlessTarget(device: device, allocator: allocator, extent: extent, bgra: false)
        );

    /// <summary>
    /// Creates a renderer with no window that draws into the output that <paramref name="createOutput"/> makes, at
    /// <see cref="RendererOptions.Width"/> by <see cref="RendererOptions.Height"/>. <see cref="CreateHeadless"/> makes
    /// a <see cref="HeadlessTarget"/>. A test and tooling hook: a test makes outputs that skip frames or store BGRA.
    /// </summary>
    internal static VulkanRenderer CreateOffscreen(
        RendererOptions options,
        Func<VulkanDevice, DeviceAllocator, Extent2D, IOutputTarget> createOutput
    )
    {
        CheckOptions(options);
        if (options.Width <= 0 || options.Height <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "A headless renderer needs a positive Width and Height."
            );
        var device = VulkanDevice.Create(options.Validation, [], null);
        var extent = new Extent2D((uint)options.Width, (uint)options.Height);
        return new(
            options: options,
            device: device,
            createOutput: (d, a) => createOutput(d, a, extent),
            headless: true
        );
    }

    /// <summary>
    /// Creates a renderer that presents to <paramref name="window"/>, which must have been created with a Vulkan
    /// graphics API (<c>WindowOptions.API = GraphicsAPI.DefaultVulkan</c>) and initialized. The swapchain follows the
    /// window's framebuffer size (through <see cref="IView.FramebufferResize"/>) and is recreated when it is out of
    /// date or suboptimal.
    /// </summary>
    /// <remarks>
    /// The renderer must be disposed before the window is destroyed, because the swapchain and the surface refer to
    /// the window's Metal layer. Silk.NET's <c>IWindow.Run()</c> destroys the window when it returns, so stop the render
    /// thread and dispose the renderer in <see cref="IView.Closing"/> or before calling <c>Reset</c>; otherwise
    /// MoltenVK crashes at shutdown.
    /// </remarks>
    public static VulkanRenderer CreateForWindow(IWindow window, RendererOptions options)
    {
        ArgumentNullException.ThrowIfNull(window);
        CheckOptions(options);
        IVkSurface surfaceSource =
            window.VkSurface
            ?? throw new ArgumentException(
                "The window has no Vulkan surface. Create it with GraphicsAPI.DefaultVulkan and initialize it first.",
                nameof(window)
            );
        byte** names = surfaceSource.GetRequiredExtensions(out uint count);
        var extensions = new List<string>((int)count);
        for (int i = 0; i < count; i++)
            extensions.Add(
                Marshal.PtrToStringUTF8((nint)names[i])
                    ?? throw new InvalidOperationException("Null surface extension name.")
            );

        var device = VulkanDevice.Create(
            options.Validation,
            extensions,
            instance => surfaceSource.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface()
        );
        Vector2D<int> framebuffer = window.FramebufferSize;
        var extent = new Extent2D(
            (uint)(options.Width > 0 ? options.Width : Math.Max(framebuffer.X, 0)),
            (uint)(options.Height > 0 ? options.Height : Math.Max(framebuffer.Y, 0))
        );
        var renderer = new VulkanRenderer(
            options: options,
            device: device,
            createOutput: (d, _) => new SwapchainTarget(d, extent, options.VSync),
            headless: false
        )
        {
            window = window,
        };
        renderer.onFramebufferResize = size => renderer.Resize(Math.Max(size.X, 0), Math.Max(size.Y, 0));
        window.FramebufferResize += renderer.onFramebufferResize;
        return renderer;
    }

    private static void CheckOptions(RendererOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.FramesInFlight is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(options), "FramesInFlight must be 1 to 4.");
        if (options.Width < 0 || options.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Width and Height cannot be negative.");
        if (options.ShadowMapSize < ShadowMap.MinimumSize)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"ShadowMapSize must be at least {ShadowMap.MinimumSize}."
            );
        if (options.Impostors && options.ImpostorTiles < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "ImpostorTiles must be at least 1.");
    }

    /// <summary>Queues a mesh. It can be drawn from the next <see cref="Render"/> on.</summary>
    public MeshId CreateMesh(MeshData mesh)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return uploads.AddMesh(mesh);
    }

    /// <summary>
    /// Queues a texture. The id is never <see cref="TextureId.None"/>, which a material uses to ask for the default
    /// texture instead.
    /// </summary>
    /// <exception cref="InvalidOperationException">The bindless array is full.</exception>
    public TextureId CreateTexture(TextureData texture)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return uploads.AddTexture(texture);
    }

    /// <summary>
    /// Queues a material. Base and emissive colors are linear. A texture slot holding <see cref="TextureId.None"/>, its
    /// default, uses the built-in default for that slot: a white base color map, a flat normal map, and a
    /// metallic-roughness map that keeps <see cref="MaterialDesc.Metallic"/> and <see cref="MaterialDesc.Roughness"/> as they are.
    /// </summary>
    /// <exception cref="ArgumentException">A texture slot holds an id that this renderer did not create.</exception>
    public MaterialId CreateMaterial(MaterialDesc material)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return uploads.AddMaterial(material);
    }

    /// <summary>Requests a new output size. It takes effect at the start of the next <see cref="Render"/>.</summary>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (headless && (width == 0 || height == 0))
            throw new ArgumentOutOfRangeException(nameof(width), "A headless renderer needs a positive size.");
        Interlocked.Exchange(ref pendingResize, ((long)width << 32) | (uint)height);
    }

    /// <summary>
    /// Asks for the pixels of the next frame that <see cref="Render"/> draws. That <c>Render</c> call waits for the
    /// frame on the GPU and hands its pixels to <paramref name="sink"/> before it returns, on its own thread, once.
    /// Use it for tests, tools and recordings, not for every frame of play: the wait stalls the frames in flight. A
    /// frame that the output skips (a minimized window) hands nothing, and the request waits for the next frame that
    /// draws. A second request before that frame replaces the first.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The swapchain images cannot be captured on this surface: they do not allow transfer reads, or their format is not
    /// one of the 8-bit color formats whose bytes a sink takes.
    /// </exception>
    public void CaptureNextFrame(ICaptureSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!output.CanCapture)
            throw new NotSupportedException(
                $"The swapchain images of this surface cannot be captured: they must allow transfer reads and hold 8-bit color, and their format is {output.Format}."
            );
        Interlocked.Exchange(ref pendingCapture, sink);
    }

    public void Render(RenderSnapshot snapshot, float alpha)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(disposed, this);
        ApplyPendingResize();

        Vk vk = device.Vk;
        FrameResources frame = frames[frameNumber % frames.Length];
        Fence fence = frame.Fence;
        WaitFor(fence);
        // A fence also covers every earlier submission, so all frames up to this slot's last one are done.
        releases.Collect(frame.Number);
        timer.Collect(frame.Index, frame.Number);
        frame.Staging.Reset();

        GpuImage? image = output.Acquire(frame);
        if (image == null)
        {
            LastFrameStats = new()
            {
                SubmittedInstances = snapshot.Instances.Count,
                GpuTimings = timer.Latest,
                Rendered = false,
            };
            return;
        }

        if (image.Extent.Width != targets.Extent.Width || image.Extent.Height != targets.Extent.Height)
        {
            // The swapchain changed size on its own (out of date). The targets are shared by frames in flight.
            device.WaitIdle();
            targets.Resize(image.Extent);
        }

        CommandBuffer commandBuffer = BeginCommands(frame);
        Upload(frame, commandBuffer);
        frame.UpdateSharedDescriptors(materials, textures);

        Batcher.Prepare(
            snapshot: snapshot,
            alpha: alpha,
            width: (int)targets.Extent.Width,
            height: (int)targets.Extent.Height,
            meshes: geometry.Meshes,
            materialCount: materials.Count,
            scene: LastScene
        );

        ICaptureSink? sink = Interlocked.Exchange(ref pendingCapture, null);
        GpuBuffer? capture = sink != null ? CaptureBuffer(image.Extent) : null;

        SetContext(frame: frame, snapshot: snapshot, alpha: alpha, image: image, capture: capture);
        Graph.Prepare(context);
        frame.Write(LastScene, Exposure);
        Graph.Record(context);
        output.FinishRecording(barriers, commandBuffer);
        vk.EndCommandBuffer(commandBuffer).Check("vkEndCommandBuffer");
        // Reset only now: if anything above threw, the fence stays signaled and the next frame does not hang.
        vk.ResetFences(device.Device, 1, &fence).Check("vkResetFences");
        output.SubmitAndPresent(frame);
        frame.Number = frameNumber;
        frameNumber++;

        LastFrameStats = StatsOf(
            scene: LastScene,
            impostors: Impostors,
            indirectDrawCalls: context.IndirectDrawCalls,
            gpuTimings: timer.Latest
        );

        if (sink != null && capture != null)
        {
            WaitFor(fence);
            HandOver(sink, capture, image.Extent);
        }
    }

    private void WaitFor(Fence fence) =>
        device
            .Vk.WaitForFences(
                device: device.Device,
                fenceCount: 1,
                pFences: &fence,
                waitAll: true,
                timeout: ulong.MaxValue
            )
            .Check("vkWaitForFences");

    /// <summary>Resets the command pool of <paramref name="frame"/> and begins its command buffer.</summary>
    private CommandBuffer BeginCommands(FrameResources frame)
    {
        // Resetting the pool also discards a command buffer left recording by an exception in an earlier frame.
        device.Vk.ResetCommandPool(device.Device, frame.CommandPool, 0).Check("vkResetCommandPool");
        CommandBuffer commandBuffer = frame.CommandBuffer;
        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        device.Vk.BeginCommandBuffer(commandBuffer, &begin).Check("vkBeginCommandBuffer");
        return commandBuffer;
    }

    /// <summary>Sets the values of this frame in the context that the passes read.</summary>
    private void SetContext(
        FrameResources frame,
        RenderSnapshot snapshot,
        float alpha,
        GpuImage image,
        GpuBuffer? capture
    )
    {
        context.Frame = frame;
        context.Snapshot = snapshot;
        context.Alpha = alpha;
        context.Scene = LastScene;
        context.Output = image;
        context.Number = frameNumber;
        context.Exposure = Exposure;
        context.EncodeSrgb = output.EncodeSrgbInShader;
        context.CaptureBuffer = capture;
        context.IndirectDrawCalls = 0;
    }

    /// <summary>Records the uploads that the Create methods queued, and the built-in defaults before the first frame.</summary>
    private void Upload(FrameResources frame, CommandBuffer commandBuffer)
    {
        UploadQueue.Pending pending = uploads.Drain();
        if (!pending.IsEmpty || textures.Count == 0 || materials.Count == 0)
        {
            device.BeginLabel(commandBuffer, "uploads");
            textures.Upload(
                commandBuffer: commandBuffer,
                pending: pending.Textures,
                staging: frame.Staging,
                barriers: barriers
            );
            geometry.Upload(
                commandBuffer: commandBuffer,
                pending: pending.Meshes,
                staging: frame.Staging,
                barriers: barriers,
                releases: releases,
                frame: frameNumber
            );
            materials.Upload(
                commandBuffer: commandBuffer,
                pending: pending.Materials,
                staging: frame.Staging,
                barriers: barriers,
                releases: releases,
                frame: frameNumber
            );
            // The first frame always comes here, because it makes the built-in defaults, and the tables go with them.
            brdfTables.Upload(commandBuffer: commandBuffer, staging: frame.Staging, barriers: barriers);
            device.EndLabel(commandBuffer);
        }
    }

    /// <summary>
    /// What a frame drew, from its prepared scene, its impostors and the indirect draws that its passes recorded, with the
    /// GPU timings of the last frame that finished.
    /// </summary>
    internal static RenderStats StatsOf(
        PreparedScene scene,
        ImpostorManager? impostors,
        int indirectDrawCalls,
        GpuTimings? gpuTimings
    ) =>
        new()
        {
            SubmittedInstances = scene.SubmittedInstances,
            VisibleInstances = scene.VisibleCount + (impostors?.Draws.Length ?? 0),
            GpuInstances = scene.InstanceCount,
            FarCommands = scene.Opaque[DepthRanges.Far].Count,
            MiddleCommands = scene.Opaque[DepthRanges.Middle].Count,
            NearCommands = scene.Opaque[DepthRanges.Near].Count,
            TransparentCommands = scene.TransparentCommandCount,
            ShadowCommands =
                scene.ShadowCommands[0].Count
                + scene.ShadowCommands[1].Count
                + scene.ShadowCommands[2].Count
                + scene.ShadowCommands[3].Count,
            ShadowCascades = scene.ShadowCascadeCount,
            ShadowCasters = scene.ShadowCasters,
            ShadowInstances = scene.ShadowInstances,
            Impostors = impostors?.Draws.Length ?? 0,
            ImpostorCaptures = impostors?.Captures.Length ?? 0,
            ImpostorsWaiting = impostors?.Waiting ?? 0,
            ImpostorTilesInUse = impostors?.TilesInUse ?? 0,
            IndirectDrawCalls = indirectDrawCalls,
            Lights = scene.LightCount,
            Outlines = scene.OutlineCount,
            Puffs = scene.PuffCount,
            GpuTimings = gpuTimings,
            Rendered = true,
        };

    private void ApplyPendingResize()
    {
        long packed = Interlocked.Exchange(ref pendingResize, -1);
        if (packed < 0)
            return;
        var extent = new Extent2D((uint)(packed >> 32), (uint)packed);
        device.WaitIdle();
        output.Resize(extent);
        if (output.Extent.Width > 0 && output.Extent.Height > 0)
            targets.Resize(output.Extent);
    }

    /// <summary>
    /// Reads one cascade of the shadow map back to the CPU, row by row from the top: reversed depth, 1 nearest the
    /// sun. A test and tooling hook: it waits for the device to be idle and submits its own copy.
    /// </summary>
    internal float[] ReadShadowCascade(int cascade)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)cascade, (uint)ShadowCascades.Count);
        GpuImage image = ShadowMap.Image;
        int size = ShadowMap.Size;
        int texelBytes = ShadowMap.Format == Format.D32Sfloat ? 4 : 2;
        device.WaitIdle();
        Vk vk = device.Vk;
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = device.QueueFamily,
        };
        vk.CreateCommandPool(
                device: device.Device,
                pCreateInfo: &poolInfo,
                pAllocator: null,
                pCommandPool: out CommandPool pool
            )
            .Check("vkCreateCommandPool");
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        vk.CreateFence(device: device.Device, pCreateInfo: &fenceInfo, pAllocator: null, pFence: out Fence fence)
            .Check("vkCreateFence");
        using var buffer = GpuBuffer.Create(
            device: device,
            allocator: Allocator,
            size: (ulong)(size * size * texelBytes),
            usage: BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.Readback,
            name: "shadow readback"
        );
        try
        {
            var allocate = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = pool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            vk.AllocateCommandBuffers(device.Device, &allocate, out CommandBuffer commandBuffer)
                .Check("vkAllocateCommandBuffers");
            var begin = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            vk.BeginCommandBuffer(commandBuffer, &begin).Check("vkBeginCommandBuffer");
            // The renderer's own barriers continue from the tracked state afterwards, so leave the image as it was.
            ResourceState state = image.State;
            var local = new BarrierBatch();
            local.Image(image, ImageUse.TransferSource);
            local.Buffer(buffer, BufferUse.TransferDestination);
            local.Flush(vk, commandBuffer);
            var region = new BufferImageCopy
            {
                ImageSubresource = new(
                    aspectMask: ImageAspectFlags.DepthBit,
                    mipLevel: 0,
                    baseArrayLayer: (uint)cascade,
                    layerCount: 1
                ),
                ImageExtent = new((uint)size, (uint)size, 1),
            };
            vk.CmdCopyImageToBuffer(
                commandBuffer: commandBuffer,
                srcImage: image.Handle,
                srcImageLayout: ImageLayout.TransferSrcOptimal,
                dstBuffer: buffer.Handle,
                regionCount: 1,
                pRegions: &region
            );
            local.Buffer(buffer, BufferUse.HostRead);
            if (state.Layout != ImageLayout.Undefined)
            {
                local.ImageLevels(
                    image: image,
                    baseMip: 0,
                    mipCount: 1,
                    oldLayout: ImageLayout.TransferSrcOptimal,
                    newLayout: state.Layout,
                    srcStages: PipelineStageFlags2.AllTransferBit,
                    srcAccess: AccessFlags2.None,
                    dstStages: PipelineStageFlags2.AllCommandsBit,
                    dstAccess: AccessFlags2.None
                );
                image.State = state;
            }

            local.Flush(vk, commandBuffer);
            vk.EndCommandBuffer(commandBuffer).Check("vkEndCommandBuffer");
            var submitBuffer = new CommandBufferSubmitInfo
            {
                SType = StructureType.CommandBufferSubmitInfo,
                CommandBuffer = commandBuffer,
            };
            var submit = new SubmitInfo2
            {
                SType = StructureType.SubmitInfo2,
                CommandBufferInfoCount = 1,
                PCommandBufferInfos = &submitBuffer,
            };
            vk.QueueSubmit2(queue: device.Queue, submitCount: 1, pSubmits: &submit, fence: fence)
                .Check("vkQueueSubmit2");
            vk.WaitForFences(
                    device: device.Device,
                    fenceCount: 1,
                    pFences: &fence,
                    waitAll: true,
                    timeout: ulong.MaxValue
                )
                .Check("vkWaitForFences");

            float[] depths = new float[size * size];
            if (texelBytes == 4)
            {
                new ReadOnlySpan<float>(buffer.Mapped, depths.Length).CopyTo(depths);
            }
            else
            {
                var texels = new ReadOnlySpan<ushort>(buffer.Mapped, depths.Length);
                for (int i = 0; i < depths.Length; i++)
                    depths[i] = texels[i] / 65535f;
            }

            return depths;
        }
        finally
        {
            vk.DestroyFence(device.Device, fence, null);
            vk.DestroyCommandPool(device.Device, pool, null);
        }
    }

    /// <summary>
    /// Reads level 0 of one impostor atlas tile back to the CPU, row by row from the top: premultiplied RGBA color
    /// and the premultiplied depth offset. A test and tooling hook: it waits for the device to be idle and submits
    /// its own copy.
    /// </summary>
    internal (float[] Rgba, float[] DepthOffset) ReadImpostorTile(int tile)
    {
        ImpostorAtlas atlas = impostorAtlas ?? throw new InvalidOperationException("Impostors are off.");
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)tile, (uint)atlas.Tiles);
        const int size = ImpostorAtlas.TileSize;
        float[] rgba = ReadLayer(image: atlas.Color, layer: tile, size: size, channels: 4);
        float[] depth = ReadLayer(image: atlas.DepthOffset, layer: tile, size: size, channels: 1);
        return (rgba, depth);
    }

    /// <summary>Copies level 0 of one layer of a 16-bit float color image to the CPU.</summary>
    private float[] ReadLayer(GpuImage image, int layer, int size, int channels)
    {
        device.WaitIdle();
        Vk vk = device.Vk;
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = device.QueueFamily,
        };
        vk.CreateCommandPool(
                device: device.Device,
                pCreateInfo: &poolInfo,
                pAllocator: null,
                pCommandPool: out CommandPool pool
            )
            .Check("vkCreateCommandPool");
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        vk.CreateFence(device: device.Device, pCreateInfo: &fenceInfo, pAllocator: null, pFence: out Fence fence)
            .Check("vkCreateFence");
        int count = size * size * channels;
        using var buffer = GpuBuffer.Create(
            device: device,
            allocator: Allocator,
            size: (ulong)(count * 2),
            usage: BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.Readback,
            name: "impostor readback"
        );
        try
        {
            var allocate = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = pool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            vk.AllocateCommandBuffers(device.Device, &allocate, out CommandBuffer commandBuffer)
                .Check("vkAllocateCommandBuffers");
            var begin = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            vk.BeginCommandBuffer(commandBuffer, &begin).Check("vkBeginCommandBuffer");
            // Leave the image in the state the renderer's own barriers continue from.
            ResourceState state = image.State;
            var local = new BarrierBatch();
            local.Image(image, ImageUse.TransferSource);
            local.Buffer(buffer, BufferUse.TransferDestination);
            local.Flush(vk, commandBuffer);
            var region = new BufferImageCopy
            {
                ImageSubresource = new(
                    aspectMask: ImageAspectFlags.ColorBit,
                    mipLevel: 0,
                    baseArrayLayer: (uint)layer,
                    layerCount: 1
                ),
                ImageExtent = new((uint)size, (uint)size, 1),
            };
            vk.CmdCopyImageToBuffer(
                commandBuffer: commandBuffer,
                srcImage: image.Handle,
                srcImageLayout: ImageLayout.TransferSrcOptimal,
                dstBuffer: buffer.Handle,
                regionCount: 1,
                pRegions: &region
            );
            local.Buffer(buffer, BufferUse.HostRead);
            if (state.Layout != ImageLayout.Undefined)
            {
                local.ImageLevels(
                    image: image,
                    baseMip: 0,
                    mipCount: image.MipLevels,
                    oldLayout: ImageLayout.TransferSrcOptimal,
                    newLayout: state.Layout,
                    srcStages: PipelineStageFlags2.AllTransferBit,
                    srcAccess: AccessFlags2.None,
                    dstStages: PipelineStageFlags2.AllCommandsBit,
                    dstAccess: AccessFlags2.None
                );
                image.State = state;
            }

            local.Flush(vk, commandBuffer);
            vk.EndCommandBuffer(commandBuffer).Check("vkEndCommandBuffer");
            var submitBuffer = new CommandBufferSubmitInfo
            {
                SType = StructureType.CommandBufferSubmitInfo,
                CommandBuffer = commandBuffer,
            };
            var submit = new SubmitInfo2
            {
                SType = StructureType.SubmitInfo2,
                CommandBufferInfoCount = 1,
                PCommandBufferInfos = &submitBuffer,
            };
            vk.QueueSubmit2(queue: device.Queue, submitCount: 1, pSubmits: &submit, fence: fence)
                .Check("vkQueueSubmit2");
            vk.WaitForFences(
                    device: device.Device,
                    fenceCount: 1,
                    pFences: &fence,
                    waitAll: true,
                    timeout: ulong.MaxValue
                )
                .Check("vkWaitForFences");

            float[] values = new float[count];
            var halves = new ReadOnlySpan<Half>(buffer.Mapped, count);
            for (int i = 0; i < count; i++)
                values[i] = (float)halves[i];
            return values;
        }
        finally
        {
            vk.DestroyFence(device.Device, fence, null);
            vk.DestroyCommandPool(device.Device, pool, null);
        }
    }

    private GpuBuffer CaptureBuffer(Extent2D extent)
    {
        ulong size = (ulong)extent.Width * extent.Height * 4;
        if (captureBuffer == null || captureBuffer.Size < size)
        {
            // Only Render uses this buffer and it waits for the capture frame, so the old one is idle.
            captureBuffer?.Dispose();
            captureBuffer = GpuBuffer.Create(
                device: device,
                allocator: Allocator,
                size: size,
                usage: BufferUsageFlags.TransferDstBit,
                memoryUsage: MemoryUsage.Readback,
                name: "capture readback"
            );
        }

        return captureBuffer;
    }

    /// <summary>
    /// Hands the pixels of a captured frame, read back into <paramref name="buffer"/>, to <paramref name="sink"/>. Every
    /// sink gets RGBA: for a BGRA output, red and blue are swapped in place in the buffer first. The buffer can be
    /// larger than the frame, after a resize to a smaller size, so the sink gets only the frame's bytes.
    /// </summary>
    private void HandOver(ICaptureSink sink, GpuBuffer buffer, Extent2D extent)
    {
        int width = (int)extent.Width,
            height = (int)extent.Height;
        var pixels = new Span<byte>(buffer.Mapped, width * height * 4);
        if (output.BgraOrder)
            for (int i = 0; i < pixels.Length; i += 4)
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        sink.Take(new(pixels, width, height));
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        owned.Push(resource);
        return resource;
    }

    private void ReleaseOwned()
    {
        captureBuffer?.Dispose();
        captureBuffer = null;
        // Reverse creation order: passes and frames before the stores and layouts they use, the allocator last.
        while (owned.Count > 0)
        {
            IDisposable resource = owned.Pop();
            if (resource is DeviceAllocator memory && memory.LiveAllocations > 0)
                log.Add(
                    $"[error, Tertius] leak: {memory.LiveAllocations} device memory allocations were still live at shutdown."
                );
            resource.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (window != null && onFramebufferResize != null)
            window.FramebufferResize -= onFramebufferResize;
        // Not checked: even after a device loss everything must still be destroyed.
        device.Vk.DeviceWaitIdle(device.Device);
        ReleaseOwned();
        device.Dispose();
    }
}
