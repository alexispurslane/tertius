using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Vector2 = System.Numerics.Vector2;

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
/// <item>The shadow maps (<see cref="ShadowPass"/>): a set of four cascades over the first 2 km for each far light with a
/// cascade set, from the frame's casters, then, under <see cref="LocalShadowAtlas"/>, the stale tiles of the near lights
/// in the local shadow atlas (<see cref="LocalShadowPass"/>).</item>
/// <item>The environment map, under <see cref="SceneEnvironmentMap"/>: the faces that this frame draws
/// (<see cref="EnvironmentFacePass"/>), then the specular map and the irradiance (<see cref="EnvironmentPrefilterPass"/>).</item>
/// <item>Impostor captures (<see cref="ImpostorPass"/>): up to four objects, chosen by <see cref="ImpostorManager"/>
/// during batching, each rendered alone into its tile of the impostor atlas.</item>
/// <item>G-buffer: far, middle and near depth ranges, each one vkCmdDrawIndexedIndirect, plus one instanced draw of
/// the impostor billboards in that range.</item>
/// <item>The depth pyramid (<see cref="DepthPyramidPass"/>), under <see cref="ScreenTrace"/>: the min-max inverse depth
/// of the distance target, level by level, its small mips in one dispatch (<see cref="PyramidTail"/>), and the
/// finest level, each pixel's center w.</item>
/// <item>The surface test (<see cref="SurfaceTestPass"/>): the pass bits of each pixel's last place, this frame's surface
/// history and FSR's device depth.</item>
/// <item>The direct pass (<see cref="DirectLightPass"/>): one fullscreen pass into the direct light's two images, the
/// diffuse and the specular light of every light that the main view keeps, each divided by its material factor and
/// pre-exposed.</item>
/// <item>The screen tracer (<see cref="ScreenTraceKernel"/>), under <see cref="ScreenTrace"/>: the ray plan, the
/// reflection rays and the diffuse rays, each kind's block rays and own rays through the depth pyramid, and the resolve,
/// which writes the samples of the indirect diffuse and indirect specular signals. Then their accumulations
/// (<see cref="AccumulatePass"/>).</item>
/// <item>The composite (<see cref="CompositePass"/>): one fullscreen pass into the HDR target, the direct light's
/// images and the two indirect signals' slow histories times their material factors, plus the emission, capped. Under
/// <see cref="NoScreenTrace"/>, the environment's closed forms take the place of the histories.</item>
/// <item>The skin scatter (<see cref="SkinScatterPass"/>), under <see cref="SeparableSubsurface"/>: the diffuse light of
/// skin pixels, which the composite writes into the diffuse image, summed along the rows and then the
/// columns, and the difference added to the lit image at skin pixels.</item>
/// <item>The haze (<see cref="HazePass"/>), when the atmosphere has one: one fullscreen pass that blends the haze over
/// the opaque surfaces of the lit image.</item>
/// <item>Transparent objects: forward-shaded, blended, back to front, each seen through the haze.</item>
/// <item>The exposure pass (<see cref="ExposurePass"/>), under <see cref="AutoExposure"/>: the meter and the adapt step,
/// which writes the exposure of the next frame.</item>
/// <item>Bloom (<see cref="BloomPass"/>), when <see cref="RendererOptions.Bloom"/> is on: the glow of the bright light
/// in the bloom levels.</item>
/// <item>Tone mapping into the swapchain image or the offscreen output, with the glow added before the tone curve.</item>
/// <item>The overlay (<see cref="OutlinePass"/>): the frame outlines of the snapshot, blended over the output image.</item>
/// <item>Capture readback, when requested. After the GPU finishes the frame, <see cref="Render"/> hands the pixels to
/// the capture's <see cref="ICaptureSink"/>.</item>
/// </list>
/// <para>A headless frame whose step is 0, endless or at least <see cref="FrameHistory.MeteringStepSeconds"/> is a still:
/// it resets every history and draws its warm-up draws before the shown draw, the first of which adapts the exposure and
/// draws all six faces of the environment map (docs/plans/temporal-renderer.md, "The still rule").</para>
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
    private readonly DeformStore deforms;

    /// <summary>The kept deformed positions, or null without the deformation.</summary>
    private readonly KeptPositions? keptPositions;

    /// <summary><see cref="RendererOptions.Deformation"/>: off, every frame passes the batcher an empty deform region.</summary>
    private readonly bool deformation;
    private readonly TextureStore textures;
    private readonly MaterialStore materials;
    private readonly RenderTargets targets;
    private readonly ImpostorAtlas? impostorAtlas;
    private readonly SurfaceHistory surfaceHistory;
    private readonly SignalScratch signalScratch;

    // The screen tracer's images and its two signals, under ScreenTrace, or null.
    private readonly ScreenTraceImages? screenTrace;
    private readonly CompositePass compositePass;
    private readonly BarrierBatch barriers = new();
    private readonly GpuTimer timer;
    private readonly ExposureSetting exposureSetting;
    private readonly EnvironmentMap? environmentMap;
    private readonly ShadowAtlas? shadowAtlas;
    private readonly LocalShadowPass? localShadows;

    /// <summary>The served chain of the frame being drawn, frame ids root first, in its first M entries.</summary>
    private readonly long[] servedChain = new long[EnvironmentChain.MaxMaps];
    private readonly FrameContext context;
    private IWindow? window;
    private Action<Vector2D<int>>? onFramebufferResize;
    private readonly bool capturesInFlight;

    // The readback buffer of each frame slot, made at its first capture. A slot reuses its buffer only after it handed
    // over the capture of its last frame.
    private readonly GpuBuffer?[] captureBuffers;

    // The capture that the last frame of each slot holds until the renderer hands it over, or null.
    private readonly SlotCapture?[] slotCaptures;
    private ICaptureSink? pendingCapture;
    private long pendingResize = -1;

    // The images and tile records of key and fill, or null under EveryLightPair.
    private readonly KeyFillImages? keyFillImages;
    private FrameClock clock;

    /// <summary>The record of the last drawn frame, whose count of drawn frames numbers every frame of the renderer.</summary>
    private readonly FrameHistory history;
    private readonly UpscalingSetting upscaling;

    /// <summary>Which draws jitter, selected once: the debug switch of ticket tiunr-4b2g, by default FSR's draws.</summary>
    private readonly DebugJitter debugJitter = DebugJitter.Selected();
    private readonly UpscalePass? upscalePass;
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
        upscaling = headless ? options.Upscaling.ForHeadless() : options.Upscaling;
        if (upscaling is Fsr3Upscaling { RenderScale: FrameTimeTarget target })
            ScaleController = new(target);
        capturesInFlight = options.CapturesInFlight;
        captureBuffers = new GpuBuffer?[options.FramesInFlight];
        slotCaptures = new SlotCapture?[options.FramesInFlight];
        clock = headless ? FrameClock.Film : FrameClock.Window;
        exposureSetting = options.Exposure;
        deformation = options.Deformation;
        history = new(StillRule.For(options, headless));
        log = device.Log;
        try
        {
            var historyMode = SpecularHistoryMode.Selected();
            var instrumentation = HistoryInstrumentation.Selected();
            CheckOptionsOnDevice(options, device);
            Allocator = Own(new DeviceAllocator(device));
            releases = Own(new DeferredReleases());
            ShaderLibrary shaders = Own(new ShaderLibrary(device, ShaderLibrary.EngineShaders));

            layouts = Own(
                new DescriptorLayouts(
                    device: device,
                    framesInFlight: options.FramesInFlight,
                    glowLevels: GlowLevelsOf(options.Bloom),
                    historyMode: historyMode,
                    instrumentation: instrumentation
                )
            );
            output = Own(createOutput(device, Allocator));
            geometry = Own(new GeometryStore(device, Allocator));
            deforms = CreateDeformStore();
            keptPositions = deformation ? Own(new KeptPositions(device, Allocator)) : null;
            textures = Own(new TextureStore(device, Allocator));
            materials = Own(new MaterialStore(device, Allocator));
            BrdfTableImages = Own(new BrdfTableImages(device, Allocator));
            uploads = CreateUploads();

            targets = CreateTargets(options);
            surfaceHistory = Own(
                new SurfaceHistory(device: device, allocator: Allocator, layouts: layouts, targets: targets)
            );
            signalScratch = Own(
                new SignalScratch(device: device, allocator: Allocator, targets: targets, layouts: layouts)
            );

            // Before every pass, whose shading constants say whether the scatter is on.
            ScatterImages = CreateScatterImages(options.Subsurface);
            keyFillImages = CreateKeyFillImages(options.LightPairs);
            Exposure = CreateExposure(options);
            environmentMap = CreateEnvironmentMap(options);
            ShadingConstants = ShadingConstantsFor(options, device.Driver, environmentMap: environmentMap is not null);
            (ShadowMap, shadowAtlas) = CreateShadowImages(options);
            // After the shadow map, whose size and depth format shape the cascade boxes that the batcher fits.
            Batcher = CreateBatcher(options);
            frames = CreateFrames(options.FramesInFlight);

            (impostorAtlas, Impostors, localShadows) = AddPassesThroughGeometry(shaders, options);
            // Before the composite, which reads the tracer's signals.
            screenTrace = AddScreenTracePasses(shaders, options.ScreenTrace);
            // After the tracer, whose light-change mask FSR reads.
            upscalePass = AddUpscalePass(shaders, options.FramesInFlight);
            compositePass = AddPassesAfterGeometry(shaders, options);
            AddScatterPasses(shaders);
            Own(Graph);
            timer = Own(new GpuTimer(device, options.FramesInFlight, eachPass: options.PassTimestamps));
            context = CreateContext();
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

    /// <summary>The driver of the physical device, by its Vulkan 1.2 driver id.</summary>
    internal DriverId Driver => device.Driver;

    /// <summary>Statistics of the last <see cref="Render"/> call.</summary>
    public RenderStats LastFrameStats { get; private set; }

    /// <summary>The size of the image the renderer draws.</summary>
    public (int Width, int Height) OutputSize => ((int)output.Extent.Width, (int)output.Extent.Height);

    internal DeviceAllocator Allocator { get; }

    /// <summary>True when the device writes timestamps, so <see cref="RenderStats.GpuTimings"/> reports the passes.</summary>
    public bool TimesPasses => timer.Supported;

    /// <summary>
    /// True on a driver that latches each pass's stamp after the next pass's work has started, so a pass's time can hold part
    /// of the next pass's work: MoltenVK (ARCHITECTURE.md, "GPU timings").
    /// </summary>
    public bool StampsLatchLate => device.Driver == DriverId.Moltenvk;

    /// <summary>
    /// True on a driver that makes a shader pay for the values that it holds at once, so that nine gathers of four
    /// comparisons each cost the shading passes more than sixteen reads of one mean each: MoltenVK. Measured on an Apple
    /// M1 Max, the corner means took 0.37 ms less in the deck's direct and transparent passes. On every other driver a
    /// gather is the cheaper way to four comparisons: on an RTX 2000 Ada under NVIDIA's driver the corner means took
    /// 0.8 ms more in the deck's frame (ARCHITECTURE.md, "The scenes and the cost of the shadows").
    /// </summary>
    internal static bool HeldValuesCost(DriverId driver) => driver == DriverId.Moltenvk;

    /// <summary>
    /// How the filter of a local shadow tile reads its texels on <paramref name="driver"/>, for an atlas that asks for
    /// <paramref name="asked"/>: the asked reads, or for <see cref="LocalShadowReads.ForDriver"/> the faster ones of that
    /// driver (<see cref="HeldValuesCost"/>).
    /// </summary>
    internal static LocalShadowReads TileReadsFor(LocalShadowReads asked, DriverId driver) =>
        asked switch
        {
            LocalShadowReads.ForDriver => HeldValuesCost(driver)
                ? LocalShadowReads.SixteenCornerMeans
                : LocalShadowReads.NineGathers,
            LocalShadowReads.NineGathers => LocalShadowReads.NineGathers,
            LocalShadowReads.SixteenCornerMeans => LocalShadowReads.SixteenCornerMeans,
        };

    internal ShadowMap ShadowMap { get; }
    internal RenderScaleController? ScaleController { get; }
    internal long DrawnFrames => history.DrawnFrames;
    internal (long DrawIndex, bool Reset) LastDraw => (context.History.DrawIndex, context.History.Reset);

    /// <summary>The BRDF tables on the GPU. A test hook.</summary>
    internal BrdfTableImages BrdfTableImages { get; }

    /// <summary>The impostor manager, or null when impostors are off. A test and tooling hook.</summary>
    internal ImpostorManager? Impostors { get; }

    /// <summary>
    /// The skin scatter's images under <see cref="SeparableSubsurface"/>, or null without the scatter. A test hook.
    /// </summary>
    internal ScatterImages? ScatterImages { get; }

    internal SceneBatcher Batcher { get; }

    /// <summary>The exposure buffer, its readbacks and the steps since the last reading. A test hook.</summary>
    internal ExposureResources Exposure { get; }

    internal FrameGraph Graph { get; } = new();

    internal PreparedScene LastScene { get; } = new();

    /// <summary>Textures from <see cref="CreateTexture"/> on the GPU, not counting the built-in defaults.</summary>
    internal int TextureCount => textures.UserCount;

    /// <summary>Materials from <see cref="CreateMaterial"/> on the GPU, not counting the default material.</summary>
    internal int MaterialCount => materials.UserCount;

    /// <summary>The CPU's facts of each material slot on the GPU, the default material's first.</summary>
    internal ReadOnlySpan<MaterialInfo> MaterialInfos => materials.Infos;

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
        Func<VulkanDevice, DeviceAllocator, Extent2D, IOutputTarget> createOutput,
        bool headless = true
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
            headless: headless
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
        CheckSizes(options);
        CheckCounts(options);
        if (!Enum.IsDefined(options.ToneCurve))
            throw new ArgumentOutOfRangeException(nameof(options), $"ToneCurve {options.ToneCurve} is not a curve.");
        if (SettingsProblem(options) is { } problem)
            throw new ArgumentOutOfRangeException(nameof(options), problem);
    }

    /// <summary>
    /// Refuses a size of <paramref name="options"/> outside its range: the frames in flight, the screen and the shadow map.
    /// </summary>
    private static void CheckSizes(RendererOptions options)
    {
        if (options.FramesInFlight is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(options), "FramesInFlight must be 1 to 4.");
        if (options.Width < 0 || options.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Width and Height cannot be negative.");
        if (options.ShadowMapSize < ShadowMap.MinimumSize)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"ShadowMapSize must be at least {ShadowMap.MinimumSize}."
            );
    }

    /// <summary>
    /// Refuses a count of <paramref name="options"/> outside its range: the cascaded lights, the impostor tiles and the
    /// warm-up draws.
    /// </summary>
    private static void CheckCounts(RendererOptions options)
    {
        if (options.CascadedLights is < 0 or > RendererOptions.MaxCascadedLights)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"CascadedLights must be 0 to {RendererOptions.MaxCascadedLights}, not {options.CascadedLights}."
            );
        if (options.Impostors && options.ImpostorTiles < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "ImpostorTiles must be at least 1.");
        if (options.WarmUpFrames is < 0 or > RendererOptions.MaxWarmUpFrames)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"WarmUpFrames must be 0 to {RendererOptions.MaxWarmUpFrames}, not {options.WarmUpFrames}."
            );
    }

    /// <summary>The first problem of the settings in <paramref name="options"/> that are unions, in their order there, or null.</summary>
    private static string? SettingsProblem(RendererOptions options) =>
        options.LocalShadows.Problem
        ?? options.Exposure.Problem
        ?? options.Bloom.Problem
        ?? options.EnvironmentMap.Problem
        ?? options.Subsurface.Problem
        ?? options.LightPairs.Problem
        ?? options.ScreenTrace.Problem
        ?? options.Upscaling.Problem
        ?? (options.ScreenTrace is ScreenTrace ? PyramidImages.ScreenProblem(options.Width, options.Height) : null);

    /// <summary>The checks of <paramref name="options"/> that need the device's limits, after <see cref="CheckOptions"/>.</summary>
    private static void CheckOptionsOnDevice(RendererOptions options, VulkanDevice device)
    {
        uint maxCube = device.Properties.Limits.MaxImageDimensionCube;
        bool mapTooLarge = options.EnvironmentMap.TryGetValue(out SceneEnvironmentMap map) && map.FaceSize > maxCube;
        if (mapTooLarge)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"SceneEnvironmentMap.FaceSize must be at most the device's maxImageDimensionCube, {maxCube}, not {map.FaceSize}."
            );
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
    /// metallic-roughness map that keeps the metallic of <see cref="StandardShading"/> and <see cref="MaterialDesc.Roughness"/>
    /// as they are.
    /// </summary>
    /// <exception cref="ArgumentException">A texture slot holds an id that this renderer did not create.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The cutoff of a <see cref="CutoutAlpha"/> or a <see cref="FringedAlpha"/> is not above 0 and at most 1.
    /// </exception>
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

    /// <summary>
    /// Waits for every frame in flight that holds a capture and hands its pixels to its sink, oldest first, on the calling
    /// thread. A renderer whose captures wait for their frames (<see cref="RendererOptions.CapturesInFlight"/> false)
    /// holds none, so it returns at once.
    /// </summary>
    public void FinishCaptures()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        for (long number = Math.Max(0, FrameNumber - frames.Length); number < FrameNumber; number++)
        {
            FrameResources frame = frames[number % frames.Length];
            if (slotCaptures[frame.Index] != null)
                FinishSlot(frame);
        }
    }

    public void Render(RenderSnapshot snapshot, float alpha)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(disposed, this);
        ApplyPendingResize();

        (FrameClock next, float step) = clock.Step(
            snapshotTime: snapshot.Time,
            wallSeconds: Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency
        );
        // A draw that continues the last frame is 1 draw, and a headless still adds its warm-up draws before the shown draw
        // (docs/plans/temporal-renderer.md, "The still rule"). Only the shown draw takes the capture.
        int draws = history.DrawsOf(step);
        for (int draw = 0; draw < draws; draw++)
            if (!DrawFrame(snapshot: snapshot, alpha: alpha, step: step, draw: draw, takeCapture: draw == draws - 1))
                return;
        clock = next;
    }

    /// <summary>
    /// Draws draw <paramref name="draw"/> of <paramref name="snapshot"/> after a clock step of <paramref name="step"/> s:
    /// waits for its slot, derives its pending state, records and submits it, and hands over the capture when
    /// <paramref name="takeCapture"/> and one is asked for. Returns false when the output skipped the frame.
    /// </summary>
    private bool DrawFrame(RenderSnapshot snapshot, float alpha, float step, int draw, bool takeCapture)
    {
        FrameResources frame = frames[FrameNumber % frames.Length];
        Fence fence = frame.Fence;
        FinishSlot(frame);

        GpuImage? image = output.Acquire(frame);
        if (image == null)
        {
            LastFrameStats = SkippedFrameStats(snapshot);
            return false;
        }

        MatchTargetsTo(image);

        // The draw's one choke point of the history: its reset, its draw index, its exposure step, its refresh of the map's
        // faces and the last frame's state. After the acquire, which can change the output size.
        FrameHistory.PendingFrame pending = history.Begin(
            snapshot: snapshot,
            step: step,
            draw: draw,
            outputSize: targets.OutputExtent,
            renderSize: upscaling.RenderExtent(
                targets.OutputExtent,
                ScaleController?.Next(FrameNumber) ?? upscaling.LargestScale
            )
        );
        ICaptureSink? sink = takeCapture ? Interlocked.Exchange(ref pendingCapture, null) : null;
        GpuBuffer? capture = sink != null ? CaptureBuffer(slot: frame.Index, extent: image.Extent) : null;
        // After the output gave the frame an image, so a skipped frame leaves the map's frame as it is, and before
        // batching, which prepares the faces' views.
        EnvironmentDraw environment = EnvironmentDrawFor(snapshot: snapshot, everyFace: pending.RefreshesEveryFace);
        RecordFrame(
            frame: frame,
            snapshot: snapshot,
            alpha: alpha,
            pending: pending,
            environment: environment,
            image: image,
            capture: capture
        );

        // Reset only now: if anything above threw, the fence stays signaled and the next frame does not hang.
        device.Vk.ResetFences(device.Device, 1, &fence).Check("vkResetFences");
        output.SubmitAndPresent(frame);
        CommitDrawnFrame(frame: frame, snapshot: snapshot, pending: pending);

        if (sink != null && capture != null)
        {
            slotCaptures[frame.Index] = new(Sink: sink, Buffer: capture, Extent: image.Extent);
            // A capture that waits for its frame hands it over now. Its frame has finished, so the stats below describe it
            // (docs/plans/auto-exposure.md, "Writers and readers").
            if (!capturesInFlight)
                FinishSlot(frame);
        }

        LastFrameStats = StatsOf(
            scene: LastScene,
            impostors: Impostors,
            indirectDrawCalls: context.IndirectDrawCalls,
            gpuTimings: timer.Latest,
            exposure: Exposure.Latest
        );
        return true;
    }

    /// <summary>Resizes the targets to the output's <paramref name="image"/> when the swapchain changed size on its own (out of date).</summary>
    private void MatchTargetsTo(GpuImage image)
    {
        if (image.Extent.Width == targets.OutputExtent.Width && image.Extent.Height == targets.OutputExtent.Height)
            return;
        // The targets are shared by frames in flight.
        device.WaitIdle();
        ResizeTargets(image.Extent);
    }

    /// <summary>
    /// Commits the frame that <paramref name="frame"/>'s slot just submitted to each owner of state that describes the last
    /// drawn frame. Only after the submission, so that a frame that threw before it leaves the maps' drawn chain, the frame
    /// history, the slot's readback, the tiles' records and the exposure's steps as they were.
    /// </summary>
    private void CommitDrawnFrame(FrameResources frame, RenderSnapshot snapshot, FrameHistory.PendingFrame pending)
    {
        long number = FrameNumber;
        environmentMap?.Drew(served: Served(snapshot), slot: frame.Index);
        keptPositions?.Drew(releases: releases, frame: number);
        localShadows?.Tiles.Drew();
        Exposure.Drew(frame: number, step: pending.ExposureStep);
        frame.Number = number;
        // Last, because it advances the frame number that the commits above name.
        history.Drew(pending);
    }

    /// <summary>
    /// The number of the frame that the renderer draws next: the count of drawn frames, which the history keeps. A frame
    /// that throws before its submission keeps its number, so its retry takes its frame slot, its releases and its parity.
    /// </summary>
    private long FrameNumber => history.DrawnFrames;

    /// <summary>The stats of a frame that the output skipped: what was submitted, and the readings that the wait found.</summary>
    private RenderStats SkippedFrameStats(RenderSnapshot snapshot) =>
        new()
        {
            SubmittedInstances = snapshot.Instances.Count,
            GpuTimings = timer.Latest,
            Exposure = Exposure.Latest,
            Rendered = false,
        };

    /// <summary>
    /// Waits for the last frame that <paramref name="frame"/>'s slot submitted, collects what it left behind: the
    /// releases it waited for, its GPU timings, its exposure and its irradiance, and hands over its capture.
    /// </summary>
    private void FinishSlot(FrameResources frame)
    {
        WaitFor(frame.Fence);
        Collect(frame);
        if (slotCaptures[frame.Index] is not { } capture)
            return;
        // Before the sink, which can throw: a capture is handed over once.
        slotCaptures[frame.Index] = null;
        HandOver(sink: capture.Sink, buffer: capture.Buffer, extent: capture.Extent);
    }

    /// <summary>
    /// Collects what the last frame of <paramref name="frame"/>'s slot left behind, once its fence has signaled: the
    /// releases it waited for, its GPU timings, its exposure and its irradiance. A slot collected twice changes nothing
    /// the second time.
    /// </summary>
    private void Collect(FrameResources frame)
    {
        // A fence also covers every earlier submission, so all frames up to this slot's last one are done.
        releases.Collect(frame.Number);
        timer.Collect(frame.Index, frame.Number);
        if (timer.Latest is { } timings)
            ScaleController?.Measure(timings);
        Exposure.Collect(frame.Index, frame.Number);
        environmentMap?.Collect(frame.Index, frame.Number);
        frame.Staging.Reset();
        // So that a sink that takes this slot's capture reads the readings of the captured frame.
        LastFrameStats = LastFrameStats with
        {
            GpuTimings = timer.Latest,
            Exposure = Exposure.Latest,
        };
    }

    /// <summary>
    /// What the batcher prepares for the maps in the frame that the renderer draws next: no map, or the faces of each map of
    /// the snapshot's served chain that <see cref="EnvironmentMap.FacesFor"/> chooses, with their latest irradiance
    /// readings. It writes the served chain, and changes no state of the maps but a growth, so a frame that throws before
    /// its submission leaves the maps' drawn chain as it was.
    /// </summary>
    private EnvironmentDraw EnvironmentDrawFor(RenderSnapshot snapshot, bool everyFace)
    {
        if (environmentMap is not { } map)
            return new(new WithoutMap());
        Span<long> served = servedChain.AsSpan(0, EnvironmentChain.Maps(snapshot.Chain.Count));
        EnvironmentChain.Serve(snapshot.Chain, served);
        ReadOnlyMemory<FaceMask> faces = map.FacesFor(frame: FrameNumber, served: served, longStep: everyFace);
        return new(new WithMap(Faces: faces, Irradiance: map.ReadingsOf(served.Length)));
    }

    /// <summary>The served chain of <paramref name="snapshot"/>, frame ids root first, as <see cref="EnvironmentDrawFor"/> wrote it.</summary>
    private ReadOnlySpan<long> Served(RenderSnapshot snapshot) =>
        servedChain.AsSpan(0, EnvironmentChain.Maps(snapshot.Chain.Count));

    /// <summary>
    /// Records the command buffer of a frame: the uploads, the batch, the exposure's readback copy and the passes.
    /// </summary>
    private void RecordFrame(
        FrameResources frame,
        RenderSnapshot snapshot,
        float alpha,
        FrameHistory.PendingFrame pending,
        EnvironmentDraw environment,
        GpuImage image,
        GpuBuffer? capture
    )
    {
        CommandBuffer commandBuffer = BeginCommands(frame);
        Upload(frame, commandBuffer);
        DeformRegion region = ReserveRegion(snapshot, commandBuffer);
        frame.UpdateSharedDescriptors(materials: materials, textures: textures, geometry: geometry, deforms: deforms);
        // After ReserveRegion, which can replace the kept buffers, so that the sets name this frame's buffers.
        if (keptPositions is { } kept)
            frame.UpdateKeptPositions(kept: kept, parity: pending.Parity);

        Batcher.Prepare(
            snapshot: snapshot,
            alpha: alpha,
            highestStops: Exposure.HighestStops(frame: FrameNumber, step: pending.ExposureStep),
            width: (int)pending.RenderSize.Width,
            height: (int)pending.RenderSize.Height,
            meshes: geometry.Meshes,
            materials: materials.Infos,
            environment: environment,
            region: region,
            frame: pending,
            scene: LastScene
        );
        ApplyRenderProjection(pending);
        SetContext(frame: frame, history: pending, snapshot: snapshot, alpha: alpha, image: image, capture: capture);
        Graph.Prepare(context);
        frame.Write(LastScene);

        // Before every pass, so the reading is the exposure that all of this frame's passes use.
        Exposure.RecordReadback(commandBuffer: commandBuffer, barriers: barriers, slot: frame.Index);
        Graph.Record(context);
        output.FinishRecording(barriers, context.CommandBuffer);
        device.Vk.EndCommandBuffer(context.CommandBuffer).Check("vkEndCommandBuffer");
    }

    /// <summary>
    /// Grows the deform region to the copies that <paramref name="snapshot"/> needs and gives it, or gives the empty region
    /// with the deformation off. After the uploads, because the need reads this frame's meshes, and before the batch,
    /// which fills the region (docs/plans/skinning-and-morphs.md, "Every new value, its writers and its readers").
    /// </summary>
    private DeformRegion ReserveRegion(RenderSnapshot snapshot, CommandBuffer commandBuffer)
    {
        if (!deformation)
            return DeformRegion.None;
        geometry.ReserveDeformed(
            vertices: SceneBatcher.DeformNeed(snapshot, geometry.Meshes),
            kept: keptPositions,
            commandBuffer: commandBuffer,
            barriers: barriers,
            releases: releases,
            frame: FrameNumber
        );
        return geometry.Region;
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
        FrameHistory.PendingFrame history,
        RenderSnapshot snapshot,
        float alpha,
        GpuImage image,
        GpuBuffer? capture
    )
    {
        context.Frame = frame;
        context.CommandBuffer = frame.CommandBuffer;
        context.History = history;
        context.Snapshot = snapshot;
        context.Alpha = alpha;
        context.Scene = LastScene;

        context.Output = image;
        context.Number = FrameNumber;
        context.EncodeSrgb = output.EncodeSrgbInShader;
        context.CaptureBuffer = capture;
        context.IndirectDrawCalls = 0;
    }

    /// <summary>Whether the renderer's draws jitter their projection: under FSR, or as <see cref="DebugJitter"/> says.</summary>
    private bool DrawsJitter =>
        debugJitter switch
        {
            DebugJitter.Upscaler or DebugJitter.Held => upscaling is Fsr3Upscaling,
            DebugJitter.None => false,
            DebugJitter.Always => true,
        };

    private void ApplyRenderProjection(FrameHistory.PendingFrame pending)
    {
        bool jitters = DrawsJitter;
        Vector2 jitter = jitters ? pending.Jitter : Vector2.Zero;
        float bias =
            upscaling is Fsr3Upscaling
                ? MathF.Log2(pending.RenderSize.Width / (float)targets.OutputExtent.Width) - 1
                : 0;
        LastScene.Constants.RenderParams = new(x: bias, y: jitter.X, z: jitter.Y, w: 0);
        Vector2 previousJitter = Vector2.Zero;
        if (jitters && !pending.Reset)
        {
            int phases = Fsr3Upscaler.JitterPhaseCount(pending.PreviousRenderSize.Width, targets.OutputExtent.Width);
            previousJitter =
                debugJitter == DebugJitter.Held
                    ? jitter
                    : Fsr3Upscaler.JitterOffset((int)((pending.DrawIndex - 1) % phases), phases);
        }
        LastScene.Constants.PreviousJitter = new(previousJitter, 0, 0);
        Extent2D outputSize = targets.OutputExtent;
        LastScene.Constants.OutputViewport = new(
            x: outputSize.Width,
            y: outputSize.Height,
            z: 1f / outputSize.Width,
            w: 1f / outputSize.Height
        );
        var shift = Matrix4x4.CreateTranslation(
            -2 * jitter.X / pending.RenderSize.Width,
            -2 * jitter.Y / pending.RenderSize.Height,
            0
        );
        for (int range = 0; range < DepthRanges.Count; range++)
            LastScene.RangeViewProjection[range] *= shift;
        LastScene.Constants.FarViewProj = LastScene.RangeViewProjection[DepthRanges.Far];
        LastScene.Constants.MiddleViewProj = LastScene.RangeViewProjection[DepthRanges.Middle];
        LastScene.Constants.NearViewProj = LastScene.RangeViewProjection[DepthRanges.Near];
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
                deforms: pending.Deforms,
                deformStore: deforms,
                staging: frame.Staging,
                barriers: barriers,
                releases: releases,
                frame: FrameNumber
            );
            deforms.Upload(
                commandBuffer: commandBuffer,
                staging: frame.Staging,
                barriers: barriers,
                releases: releases,
                frame: FrameNumber
            );
            materials.Upload(
                commandBuffer: commandBuffer,
                pending: pending.Materials,
                staging: frame.Staging,
                barriers: barriers,
                releases: releases,
                frame: FrameNumber
            );
            // The first frame always comes here, because it makes the built-in defaults, and the tables go with them.
            BrdfTableImages.Upload(commandBuffer: commandBuffer, staging: frame.Staging, barriers: barriers);
            ScatterImages?.Upload(commandBuffer: commandBuffer, staging: frame.Staging, barriers: barriers);
            // Frame 0 is the first drawn frame, because the frame number counts submitted frames.
            if (FrameNumber == 0)
                Exposure.RecordSeed(commandBuffer: commandBuffer, barriers: barriers);
            device.EndLabel(commandBuffer);
        }
    }

    /// <summary>
    /// What a frame drew, from its prepared scene, its impostors and the indirect draws that its passes recorded, with the
    /// GPU timings and the exposure of the last frame that finished.
    /// </summary>
    internal static RenderStats StatsOf(
        PreparedScene scene,
        ImpostorManager? impostors,
        int indirectDrawCalls,
        GpuTimings? gpuTimings,
        ExposureReading? exposure
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
            ShadowCommands = ShadowCommandsOf(scene),
            ShadowCascades = scene.CascadeSetCount * ShadowCascades.Count,
            CascadeSets = scene.CascadeSetCount,
            ShadowCasters = scene.ShadowCasters,
            ShadowInstances = scene.ShadowInstances,
            ShadowTilesInUse = scene.ShadowTileCount,
            ShadowTilesDrawn = scene.DrawnTiles.Count,
            Impostors = impostors?.Draws.Length ?? 0,
            ImpostorCaptures = impostors?.Captures.Length ?? 0,
            ImpostorsWaiting = impostors?.Waiting ?? 0,
            ImpostorTilesInUse = impostors?.TilesInUse ?? 0,
            IndirectDrawCalls = indirectDrawCalls,
            Lights = scene.LightCount,
            Outlines = scene.OutlineCount,
            Puffs = scene.PuffCount,
            DeformedInstances = scene.DeformedInstances,
            DeformedVertices = scene.DeformedVertices,
            UndeformedPosedInstances = scene.UndeformedPosedInstances,
            GpuTimings = gpuTimings,
            Exposure = exposure,
            Rendered = true,
        };

    /// <summary>The caster commands of every cascade of every set that the frame draws.</summary>
    private static int ShadowCommandsOf(PreparedScene scene)
    {
        int count = 0;
        for (int layer = 0; layer < scene.CascadeSetCount * ShadowCascades.Count; layer++)
            count += scene.ShadowCommands[layer].Count;
        return count;
    }

    private void ApplyPendingResize()
    {
        long packed = Interlocked.Exchange(ref pendingResize, -1);
        if (packed < 0)
            return;
        var extent = new Extent2D((uint)(packed >> 32), (uint)packed);
        device.WaitIdle();
        output.Resize(extent);
        if (output.Extent.Width > 0 && output.Extent.Height > 0)
            ResizeTargets(output.Extent);
    }

    /// <summary>
    /// Resizes the render targets, and the screen tracer's and the scatter's images, whose sets point at the targets, to
    /// <paramref name="extent"/>. The device must be idle. Internal for the tests of a size that no output takes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The screen tracer is on, and a side is above <see cref="PyramidImages.MaxSide"/>.
    /// </exception>
    internal void ResizeTargets(Extent2D extent)
    {
        if (extent.Width == targets.OutputExtent.Width && extent.Height == targets.OutputExtent.Height)
            return;
        targets.Resize(extent);
        surfaceHistory.Resize(targets);
        signalScratch.Resize();
        if (screenTrace is { } tracer)
        {
            tracer.Diffuse.Resize();
            tracer.Specular.Resize();
            tracer.Filter?.Resize();
            tracer.Resize();
        }

        ScatterImages?.Resize(targets);
        keyFillImages?.Resize(targets);
        compositePass.Resize(targets);
        upscalePass?.Resize();
    }

    /// <summary>
    /// Reads one cascade of one cascade set back to the CPU, row by row from the top: reversed depth, 1 nearest the light.
    /// A test and tooling hook: it waits for the device to be idle and submits its own copy.
    /// </summary>
    internal float[] ReadShadowCascade(int set, int cascade)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)set, (uint)ShadowMap.Sets);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)cascade, (uint)ShadowCascades.Count);
        return ReadDepthLayer(image: ShadowMap.Image, layer: ShadowMap.Layer(set, cascade), size: ShadowMap.Size);
    }

    /// <summary>
    /// Reads one layer of the local shadow atlas back to the CPU, row by row from the top: reversed depth, 1 at the tile's
    /// near plane, 0 at infinity. A test and tooling hook: it waits for the device to be idle and submits its own copy.
    /// </summary>
    /// <exception cref="InvalidOperationException">Local shadows are off.</exception>
    internal float[] ReadShadowTile(int layer)
    {
        ShadowAtlas atlas = shadowAtlas ?? throw new InvalidOperationException("Local shadows are off.");
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)layer, (uint)atlas.Tiles);
        return ReadDepthLayer(image: atlas.Image, layer: layer, size: atlas.TileSize);
    }

    /// <summary>
    /// Reads the bounds of one layer of the local shadow atlas back to the CPU, row by row from the top: per texel the
    /// least and the greatest reversed depth of its window of the tile (ARCHITECTURE.md, "Local shadows"). A test and
    /// tooling hook, like <see cref="ReadShadowTile"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Local shadows are off.</exception>
    internal float[] ReadShadowTileBounds(int layer)
    {
        ShadowAtlas atlas = shadowAtlas ?? throw new InvalidOperationException("Local shadows are off.");
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)layer, (uint)atlas.Tiles);
        return ReadLevel(image: atlas.Bounds, layer: layer, mip: 0).Values;
    }

    /// <summary>
    /// Copies one layer of a square depth image of D32 or D16 to the CPU, as floats from 0 to 1, row by row from the top.
    /// </summary>
    private float[] ReadDepthLayer(GpuImage image, int layer, int size)
    {
        int texelBytes = image.Format == Format.D32Sfloat ? 4 : 2;
        using var buffer = GpuBuffer.Create(
            device: device,
            allocator: Allocator,
            size: (ulong)(size * size * texelBytes),
            usage: BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.Readback,
            name: "shadow readback"
        );
        SubmitAndWait(commandBuffer =>
            CopyLayer(
                commandBuffer: commandBuffer,
                image: image,
                aspect: ImageAspectFlags.DepthBit,
                layer: layer,
                mip: 0,
                extent: new((uint)size, (uint)size),
                buffer: buffer
            )
        );

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

    /// <summary>
    /// The distance target of the last frame, in m from the eye and 0 for sky, row by row from the top. A test hook: it
    /// waits for the device to be idle and submits its own copy.
    /// </summary>
    internal float[] ReadDistance() => ReadLevel(image: targets.Distance, layer: 0, mip: 0).Values;

    /// <summary>
    /// The G-buffer's normal target of the last frame, row by row from the top: per pixel the octahedral form of the
    /// shading normal in RG and of the geometric normal in BA, in world axes. A test hook, like <see cref="ReadDistance"/>.
    /// </summary>
    internal float[] ReadNormals() => ReadLevel(image: targets.Normal, layer: 0, mip: 0).Values;

    /// <summary>
    /// The G-buffer's shading target of the last frame, RGBA per pixel, row by row from the top: a hair pixel's strand
    /// code in RG, 0 in B, the shading model's code in A (<see cref="ShadingCode"/>, <see cref="StrandCode"/>). A test
    /// hook, like <see cref="ReadDistance"/>.
    /// </summary>
    /// <summary>
    /// The G-buffer's specular target after the last frame, RGBA per pixel, row by row from the top: metallic, roughness,
    /// reflectance and the lobe's sky share, each as its 8-bit code. A test hook: it waits for the device to be idle.
    /// </summary>
    internal byte[] ReadSpecularTarget() =>
        [.. ReadLevel(image: targets.Specular, layer: 0, mip: 0).Values.Select(value => (byte)value)];

    internal byte[] ReadShading() =>
        [.. ReadLevel(image: targets.Shading, layer: 0, mip: 0).Values.Select(value => (byte)value)];

    /// <summary>
    /// The shading target's state after the last frame: the layout, and the stages and the accesses that its barriers
    /// made the G-buffer pass's writes visible to. A test hook.
    /// </summary>
    internal ResourceState ShadingTargetState => targets.Shading.State;

    /// <summary>The emissive target's state after the last frame, as <see cref="ShadingTargetState"/> gives the shading target's. A test hook.</summary>
    internal ResourceState EmissiveTargetState => targets.Emissive.State;

    /// <summary>
    /// The HDR image of the last frame, RGBA per pixel, row by row from the top: pre-exposed radiance. A test hook: it
    /// waits for the device to be idle and submits its own copy.
    /// </summary>
    internal float[] ReadHdr() => ReadLevel(image: targets.Hdr, layer: 0, mip: 0).Values;

    /// <summary>
    /// The HDR image after the upscale, RGBA per pixel, row by row from the top: FSR's output at the output size, or the
    /// HDR image without an upscale. Pre-exposed radiance before bloom and tone mapping. A test hook for the capture tool:
    /// it waits for the device to be idle and submits its own copy.
    /// </summary>
    internal float[] ReadPostHdr() => ReadLevel(image: targets.PostHdr, layer: 0, mip: 0).Values;

    /// <summary>
    /// The first level of the first layer of <paramref name="image"/>, row by row from the top, as floats: a test's own
    /// image. A test hook: it waits for the device to be idle and submits its own copy.
    /// </summary>
    internal float[] ReadImage(GpuImage image) => ReadLevel(image: image, layer: 0, mip: 0).Values;

    /// <summary>The first level of layer <paramref name="layer"/> of <paramref name="image"/>, as <see cref="ReadImage"/> reads the first layer. A test hook.</summary>
    internal float[] ReadImageLayer(GpuImage image, int layer) => ReadLevel(image: image, layer: layer, mip: 0).Values;

    /// <summary>
    /// A signal of <paramref name="parts"/> parts with <paramref name="parameters"/> at the size of the render targets, whose
    /// sets bind the targets and the surface history, and which the renderer owns. A test hook: the renderer resizes only
    /// the screen tracer's signals.
    /// </summary>
    internal TemporalSignal CreateSignal(SignalParameters parameters, string name, int parts)
    {
        // The pipelines keep no reference to the shader modules, so the library goes with this call.
        using var shaders = new ShaderLibrary(device, ShaderLibrary.EngineShaders);
        return CreateSignal(shaders: shaders, parameters: parameters, name: name, parts: parts);
    }

    /// <summary>A signal as <see cref="CreateSignal(SignalParameters, string, int)"/> makes it, from <paramref name="shaders"/>.</summary>
    private TemporalSignal CreateSignal(ShaderLibrary shaders, SignalParameters parameters, string name, int parts) =>
        Own(
            new TemporalSignal(
                device: device,
                allocator: Allocator,
                layouts: layouts,
                shaders: shaders,
                targets: targets,
                surfaces: surfaceHistory,
                scratch: signalScratch,
                parameters: parameters,
                name: name,
                parts: parts
            )
        );

    /// <summary>
    /// A filter of the diffuse light of <paramref name="signal"/>'s part 0, whose pass the caller adds after the signal's
    /// accumulation. A test hook: the filter goes with the renderer. The descriptor pool holds the sets of one filter, so
    /// the renderer must run under <see cref="NoScreenTrace"/>, whose tracer makes no filter.
    /// </summary>
    internal DiffuseFilter CreateDiffuseFilter(TemporalSignal signal, DiffuseFilterKernel kernel)
    {
        using var shaders = new ShaderLibrary(device, ShaderLibrary.EngineShaders);
        return CreateDiffuseFilter(shaders, signal, kernel);
    }

    private DiffuseFilter CreateDiffuseFilter(
        ShaderLibrary shaders,
        TemporalSignal signal,
        DiffuseFilterKernel kernel
    ) =>
        Own(
            new DiffuseFilter(
                device: device,
                allocator: Allocator,
                layouts: layouts,
                shaders: shaders,
                targets: targets,
                signal: signal,
                kernel: kernel
            )
        );

    /// <summary>The screen tracer's two signals. A test hook.</summary>
    /// <exception cref="InvalidOperationException">The setting is <see cref="NoScreenTrace"/>.</exception>
    internal (TemporalSignal Diffuse, TemporalSignal Specular) ScreenTraceSignals =>
        screenTrace is { } tracer
            ? (tracer.Diffuse, tracer.Specular)
            : throw new InvalidOperationException("The screen tracer is off.");

    /// <summary>The screen tracer's images. A test hook.</summary>
    /// <exception cref="InvalidOperationException">The setting is <see cref="NoScreenTrace"/>.</exception>
    internal ScreenTraceImages ScreenTraceImages =>
        screenTrace ?? throw new InvalidOperationException("The screen tracer is off.");

    /// <summary>
    /// The light-age record that the last drawn frame wrote (<see cref="ScreenTraceImages.LightAge"/>): per pixel the
    /// exact light and the age in frames, or <see cref="ScreenTraceImages.NoLightAge"/>. A test hook: it waits for the
    /// device to be idle and submits its own copy.
    /// </summary>
    /// <exception cref="InvalidOperationException">The original specular history keeps no light age.</exception>
    internal float[] ReadLightAge() =>
        ReadImage(
            ScreenTraceImages.LightAge((int)((history.DrawnFrames - 1) & 1))
                ?? throw new InvalidOperationException("The original specular history keeps no light age.")
        );

    /// <summary>
    /// The light-change mask that the last drawn frame wrote for FSR 3.1 (<see cref="ScreenTraceImages.LightChangeMask"/>),
    /// one value per pixel as a byte from 0 to 255, 0 where FSR keeps its history. A test hook: it waits for the device to
    /// be idle.
    /// </summary>
    /// <exception cref="InvalidOperationException">The original specular history keeps no light age.</exception>
    internal float[] ReadLightChangeMask() =>
        ReadImage(
            ScreenTraceImages.LightChangeMask
                ?? throw new InvalidOperationException("The original specular history keeps no light age.")
        );

    internal DescriptorSet CompositeLightSet => compositePass.LightSet;

    /// <summary>
    /// The counts of the last frame's own-ray lists of the screen tracer: the pixels that traced their own reflection ray
    /// and their own diffuse ray. A test hook: it waits for the device to be idle and submits its own copy.
    /// </summary>
    /// <exception cref="InvalidOperationException">The setting is <see cref="NoScreenTrace"/>.</exception>
    internal (int Reflection, int Diffuse) ReadOwnRayCounts()
    {
        GpuBuffer[] lists = ScreenTraceImages.OwnRays;
        return (
            OwnRayList.Pixels(ReadBufferWords(lists[0])).Length,
            OwnRayList.Pixels(ReadBufferWords(lists[1])).Length
        );
    }

    /// <summary>
    /// The exposure buffer: the stops that the next frame uses, and the stops that the frame before it used, which its
    /// histories hold. A test hook: it waits for the device to be idle and submits its own copy.
    /// </summary>
    internal (float Stops, float PreviousStops) ReadExposureStops()
    {
        GpuBuffer exposure = Exposure.Buffer;
        using var buffer = GpuBuffer.Create(
            device: device,
            allocator: Allocator,
            size: exposure.Size,
            usage: BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.Readback,
            name: "exposure readback"
        );
        SubmitAndWait(commandBuffer => CopyBuffer(commandBuffer: commandBuffer, source: exposure, buffer: buffer));
        ReadOnlySpan<float> stops = new(buffer.Mapped, 2);
        return (stops[0], stops[1]);
    }

    /// <summary>
    /// The motion target of the last frame, RGBA per pixel, row by row from the top: xy the last drawn frame's place of the
    /// pixel's point minus this frame's place, in units of the image's size, z the ratio of the view depths, (2, 2) without
    /// a last place, and alpha 1 where the pixel reads its history at its pixel place (<see cref="RenderTargets.Motion"/>). A test hook, like <see cref="ReadDistance"/>.
    /// </summary>
    internal float[] ReadMotion() => ReadLevel(image: targets.Motion, layer: 0, mip: 0).Values;

    /// <summary>The FSR exposure ratio recorded before adaptation, or an error without FSR. A test hook.</summary>
    internal float ReadFsrExposureRatio()
    {
        GpuBuffer constants = upscalePass?.Constants ?? throw new InvalidOperationException("FSR is off.");
        uint[] words = ReadBufferWords(constants);
        int word = (int)Marshal.OffsetOf<Fsr3Constants>(nameof(Fsr3Constants.DeltaPreExposure)) / sizeof(uint);
        return BitConverter.UInt32BitsToSingle(words[word]);
    }

    /// <summary>The reactive mask as bytes from 0 to 255, or an error without FSR. A test hook.</summary>
    internal byte[] ReadReactiveMask() =>
        [
            .. ReadLevel(
                image: targets.Upscale?.Reactive ?? throw new InvalidOperationException("FSR is off."),
                layer: 0,
                mip: 0
            )
                .Values.Select(value => (byte)value),
        ];

    /// <summary>
    /// The surface test image of the last frame, one byte per pixel, row by row from the top: bit i set where tap i passed
    /// the surface test, bit 4 + i where it passed the specular test (<see cref="SurfaceHistory.Test"/>). A test hook.
    /// </summary>
    internal byte[] ReadSurfaceTest() =>
        [.. ReadLevel(image: surfaceHistory.Test, layer: 0, mip: 0).Values.Select(value => (byte)value)];

    /// <summary>
    /// FSR's device depth of the last frame, row by row from the top: <see cref="SurfaceTestPass.DeviceDepthNear"/> over
    /// the view depth, 0 for sky. A test hook.
    /// </summary>
    internal float[] ReadDeviceDepth() =>
        ReadLevel(image: surfaceHistory.WrittenDepth(context.History.Parity), layer: 0, mip: 0).Values;

    /// <summary>
    /// The surface history that the last frame wrote, RGBA per pixel, row by row from the top: x unused, the geometric
    /// normal in octahedral code, and the roughness, all 0 for sky. A test hook.
    /// </summary>
    internal float[] ReadSurfaceHistory() =>
        ReadLevel(image: surfaceHistory.Written(context.History.Parity), layer: 0, mip: 0).Values;

    /// <summary>
    /// The diffuse image of the last frame, RGBA per pixel, row by row from the top, as <see cref="RenderTargets.Diffuse"/>
    /// says. A test hook: it waits for the device to be idle and submits its own copy.
    /// </summary>
    /// <exception cref="InvalidOperationException">The skin scatter is off.</exception>
    internal float[] ReadDiffuse() =>
        ReadLevel(
            image: targets.Diffuse ?? throw new InvalidOperationException("The skin scatter is off."),
            layer: 0,
            mip: 0
        ).Values;

    /// <summary>
    /// Level 1 to L of the last frame's depth pyramid: the near and the far bound of each texel of its mip, padding
    /// included, row by row from the top. A test hook: it waits for the device to be idle and submits its own copy.
    /// </summary>
    /// <exception cref="InvalidOperationException">The screen tracer is off.</exception>
    internal (float[] Bounds, int Width, int Height) ReadDepthPyramidLevel(int level)
    {
        PyramidImages pyramids = PyramidOrThrow();
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, pyramids.TopLevel);
        return ReadLevel(image: pyramids.Image, layer: 0, mip: level - 1);
    }

    /// <summary>
    /// The last frame's finest level of the depth pyramid: each pixel's center w in 1/m, 0 for sky, row by row from the
    /// top. A test hook, like <see cref="ReadDepthPyramidLevel"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The screen tracer is off.</exception>
    internal float[] ReadDepthFinest() => ReadLevel(image: PyramidOrThrow().Finest, layer: 0, mip: 0).Values;

    /// <summary>
    /// Level 0 to L of the last frame's light pyramid: the guide light of each pixel at level 0, and the sum of each texel of
    /// a level's mip above, padding included, row by row from the top. A test hook, like <see cref="ReadDepthPyramidLevel"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The screen tracer is off.</exception>
    internal (float[] Lights, int Width, int Height) ReadLightPyramidLevel(int level)
    {
        PyramidImages pyramid = (
            screenTrace ?? throw new InvalidOperationException("The screen tracer is off.")
        ).LightPyramidImages;
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, pyramid.TopLevel);
        return level == 0
            ? ReadLevel(image: pyramid.Finest, layer: 0, mip: 0)
            : ReadLevel(image: pyramid.Image, layer: 0, mip: level - 1);
    }

    private PyramidImages PyramidOrThrow() =>
        targets.Pyramid ?? throw new InvalidOperationException("The screen tracer is off.");

    /// <summary>The words of <paramref name="buffer"/>. A test hook: it waits for the device to be idle and submits its own copy.</summary>
    private uint[] ReadBufferWords(GpuBuffer buffer)
    {
        using var readback = GpuBuffer.Create(
            device: device,
            allocator: Allocator,
            size: buffer.Size,
            usage: BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.Readback,
            name: "buffer readback"
        );
        SubmitAndWait(commandBuffer =>
        {
            var local = new BarrierBatch();
            local.Buffer(buffer, BufferUse.TransferSource);
            local.Buffer(readback, BufferUse.TransferDestination);
            local.Flush(device.Vk, commandBuffer);
            var region = new BufferCopy(srcOffset: 0, dstOffset: 0, size: buffer.Size);
            device.Vk.CmdCopyBuffer(
                commandBuffer: commandBuffer,
                srcBuffer: buffer.Handle,
                dstBuffer: readback.Handle,
                regionCount: 1,
                pRegions: &region
            );
            local.Buffer(readback, BufferUse.HostRead);
            local.Flush(device.Vk, commandBuffer);
        });
        return new ReadOnlySpan<uint>(readback.Mapped, (int)(buffer.Size / sizeof(uint))).ToArray();
    }

    /// <summary>
    /// The deformed copies of the last frame, job after job, as the deform pass wrote them. A test hook: it waits for the
    /// device to be idle and submits its own copy.
    /// </summary>
    internal GpuVertex[] ReadDeformedVertices()
    {
        ReadOnlySpan<DeformJob> jobs = LastScene.Deforms;
        ReadOnlySpan<MeshInfo> meshes = geometry.Meshes;
        var copies = new BufferCopy[jobs.Length];
        ulong vertexSize = (ulong)sizeof(GpuVertex),
            read = 0;
        for (int i = 0; i < jobs.Length; i++)
        {
            ulong count = (ulong)(meshes[jobs[i].Mesh].Deform?.VertexCount ?? 0);
            ulong first = (ulong)(geometry.Region.Base + jobs[i].Target);
            copies[i] = new(srcOffset: first * vertexSize, dstOffset: read * vertexSize, size: count * vertexSize);
            read += count;
        }

        if (read == 0)
            return [];
        using var buffer = GpuBuffer.Create(
            device: device,
            allocator: Allocator,
            size: read * vertexSize,
            usage: BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.Readback,
            name: "deformed vertices readback"
        );
        SubmitAndWait(commandBuffer =>
        {
            var local = new BarrierBatch();
            local.Buffer(geometry.VertexBuffer, BufferUse.TransferSource);
            local.Buffer(buffer, BufferUse.TransferDestination);
            local.Flush(device.Vk, commandBuffer);
            fixed (BufferCopy* regions = copies)
                device.Vk.CmdCopyBuffer(
                    commandBuffer: commandBuffer,
                    srcBuffer: geometry.VertexBuffer.Handle,
                    dstBuffer: buffer.Handle,
                    regionCount: (uint)copies.Length,
                    pRegions: regions
                );
            local.Buffer(buffer, BufferUse.HostRead);
            local.Flush(device.Vk, commandBuffer);
        });
        return new ReadOnlySpan<GpuVertex>(buffer.Mapped, (int)read).ToArray();
    }

    /// <summary>
    /// The 256 counts of the exposure's histogram of the last metered frame, by bin. A test hook: it waits for the device
    /// to be idle and submits its own copy.
    /// </summary>
    internal uint[] ReadExposureHistogram()
    {
        GpuBuffer histogram = Exposure.Histogram;
        using var buffer = GpuBuffer.Create(
            device: device,
            allocator: Allocator,
            size: histogram.Size,
            usage: BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.Readback,
            name: "histogram readback"
        );
        SubmitAndWait(commandBuffer => CopyBuffer(commandBuffer: commandBuffer, source: histogram, buffer: buffer));
        return new ReadOnlySpan<uint>(buffer.Mapped, ExposureMeter.Bins).ToArray();
    }

    /// <summary>
    /// Records the copy of all of <paramref name="source"/> into the readback <paramref name="buffer"/>. The renderer's
    /// barriers continue from the source's tracked use, which this copy updates.
    /// </summary>
    private void CopyBuffer(CommandBuffer commandBuffer, GpuBuffer source, GpuBuffer buffer)
    {
        var local = new BarrierBatch();
        local.Buffer(source, BufferUse.TransferSource);
        local.Buffer(buffer, BufferUse.TransferDestination);
        local.Flush(device.Vk, commandBuffer);
        var region = new BufferCopy(srcOffset: 0, dstOffset: 0, size: source.Size);
        device.Vk.CmdCopyBuffer(
            commandBuffer: commandBuffer,
            srcBuffer: source.Handle,
            dstBuffer: buffer.Handle,
            regionCount: 1,
            pRegions: &region
        );
        local.Buffer(buffer, BufferUse.HostRead);
        local.Flush(device.Vk, commandBuffer);
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
        float[] rgba = ReadLevel(image: atlas.Color, layer: tile, mip: 0).Values;
        float[] depth = ReadLevel(image: atlas.DepthOffset, layer: tile, mip: 0).Values;
        return (rgba, depth);
    }

    /// <summary>
    /// Reads mip <paramref name="level"/> of one face of map <paramref name="map"/> of the environment maps'
    /// <paramref name="image"/> back to the CPU: row by row from the top, RGBA halves, or one half per texel of the
    /// distance cubes. A test hook: it waits for the device to be idle and submits its own copy.
    /// </summary>
    internal Half[] ReadEnvironmentFace(int map, CubeFace face, int level, EnvironmentImage image)
    {
        EnvironmentMap environment =
            environmentMap ?? throw new InvalidOperationException("The environment map is off.");
        (GpuImage cube, int channels) = image switch
        {
            EnvironmentImage.Radiance => (Cube: environment.Radiance, Channels: 4),
            EnvironmentImage.Specular => (Cube: environment.Specular, Channels: 4),
            EnvironmentImage.Irradiance => (Cube: environment.Irradiance, Channels: 4),
            EnvironmentImage.Distance => (Cube: environment.Distance, Channels: 1),
        };
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)level, cube.MipLevels);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)map, (uint)environment.Capacity);
        int size = Math.Max(1, (int)cube.Extent.Width >> level);
        int count = size * size * channels;
        using var buffer = GpuBuffer.Create(
            device: device,
            allocator: Allocator,
            size: (ulong)(count * 2),
            usage: BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.Readback,
            name: "environment readback"
        );
        SubmitAndWait(commandBuffer =>
            CopyLayer(
                commandBuffer: commandBuffer,
                image: cube,
                aspect: ImageAspectFlags.ColorBit,
                layer: CubeFaces.Count * map + (int)face,
                mip: level,
                extent: new((uint)size, (uint)size),
                buffer: buffer
            )
        );
        return new ReadOnlySpan<Half>(buffer.Mapped, count).ToArray();
    }

    /// <summary>
    /// The last level of map <paramref name="map"/>'s irradiance cube that the prefilter last wrote. A test hook: it waits
    /// for the device to be idle and submits its own copies.
    /// </summary>
    internal Irradiance ReadEnvironmentIrradiance(int map)
    {
        var texels = new List<Half>(Irradiance.Halves);
        for (int layer = 0; layer < CubeFaces.Count; layer++)
            texels.AddRange(
                ReadEnvironmentFace(
                    map: map,
                    face: (CubeFace)layer,
                    level: IrradianceCube.Levels - 1,
                    image: EnvironmentImage.Irradiance
                )
            );
        return Irradiance.Of([.. texels]);
    }

    /// <summary>
    /// One bloom level of the last frame, RGBA per pixel, row by row from the top. After a frame, level 0 holds every
    /// level summed. A test hook: it waits for the device to be idle and submits its own copy.
    /// </summary>
    internal float[] ReadGlowLevel(int level)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)level, (uint)targets.Glow.Length);
        return ReadLevel(image: targets.Glow[level], layer: 0, mip: 0).Values;
    }

    /// <summary>How <see cref="ReadLevel"/> finds each channel of an image in memory.</summary>
    private enum StoredChannel
    {
        /// <summary>A 16-bit float.</summary>
        Half,

        /// <summary>A 32-bit float.</summary>
        Float,

        /// <summary>A 16-bit signed normalized integer, for −1 to 1.</summary>
        Snorm16,

        /// <summary>An 8-bit unsigned integer, read as its value.</summary>
        Uint8,

        /// <summary>A 16-bit unsigned integer, read as its value.</summary>
        Uint16,
    }

    /// <summary>
    /// Copies one mip of one layer of a color image of floats, of 16-bit signed normalized integers or of 8-bit unsigned
    /// integers to the CPU, as floats, channel by channel, row by row from the top, with the mip's size.
    /// </summary>
    private (float[] Values, int Width, int Height) ReadLevel(GpuImage image, int layer, int mip)
    {
        (int channels, StoredChannel stored) = image.Format switch
        {
            Format.R16Sfloat => (1, StoredChannel.Half),
            Format.R16G16B16A16Sfloat => (4, StoredChannel.Half),
            Format.R32Sfloat => (1, StoredChannel.Float),
            Format.R32G32Sfloat => (2, StoredChannel.Float),
            Format.R32G32B32A32Sfloat => (4, StoredChannel.Float),
            // Each word comes back as the bits of a float, which its hook takes apart.
            Format.R32G32Uint => (2, StoredChannel.Float),
            Format.R16G16B16A16SNorm => (4, StoredChannel.Snorm16),
            Format.R8G8B8A8Uint or Format.R8G8B8A8Unorm => (4, StoredChannel.Uint8),
            Format.R8Uint or Format.R8Unorm => (1, StoredChannel.Uint8),
            Format.R16G16B16A16Uint => (4, StoredChannel.Uint16),
            Format.R16Uint => (1, StoredChannel.Uint16),
            _ => throw new ArgumentException(
                $"{image.Name} has the format {image.Format}, which no hook reads.",
                nameof(image)
            ),
        };
        var extent = new Extent2D(Math.Max(image.Extent.Width >> mip, 1), Math.Max(image.Extent.Height >> mip, 1));
        int count = (int)(extent.Width * extent.Height) * channels;
        using var buffer = GpuBuffer.Create(
            device: device,
            allocator: Allocator,
            size: (ulong)(
                count
                * stored switch
                {
                    StoredChannel.Float => 4,
                    StoredChannel.Half or StoredChannel.Snorm16 or StoredChannel.Uint16 => 2,
                    StoredChannel.Uint8 => 1,
                }
            ),
            usage: BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.Readback,
            name: $"{image.Name} readback"
        );
        SubmitAndWait(commandBuffer =>
            CopyLayer(
                commandBuffer: commandBuffer,
                image: image,
                aspect: ImageAspectFlags.ColorBit,
                layer: layer,
                mip: mip,
                extent: extent,
                buffer: buffer
            )
        );

        return (ValuesOf(buffer: buffer, stored: stored, count: count), (int)extent.Width, (int)extent.Height);
    }

    /// <summary>The <paramref name="count"/> channels that <paramref name="buffer"/> holds, each stored as <paramref name="stored"/> says, as floats.</summary>
    private static float[] ValuesOf(GpuBuffer buffer, StoredChannel stored, int count)
    {
        float[] values = new float[count];
        switch (stored)
        {
            case StoredChannel.Half:
                var halves = new ReadOnlySpan<Half>(buffer.Mapped, count);
                for (int i = 0; i < count; i++)
                    values[i] = (float)halves[i];
                break;
            case StoredChannel.Float:
                new ReadOnlySpan<float>(buffer.Mapped, count).CopyTo(values);
                break;
            case StoredChannel.Snorm16:
                var integers = new ReadOnlySpan<short>(buffer.Mapped, count);
                for (int i = 0; i < count; i++)
                    values[i] = SnormValue(integers[i]);
                break;
            case StoredChannel.Uint8:
                var bytes = new ReadOnlySpan<byte>(buffer.Mapped, count);
                for (int i = 0; i < count; i++)
                    values[i] = bytes[i];
                break;
            case StoredChannel.Uint16:
                var words = new ReadOnlySpan<ushort>(buffer.Mapped, count);
                for (int i = 0; i < count; i++)
                    values[i] = words[i];
                break;
            default:
                throw new UnreachableException();
        }

        return values;
    }

    /// <summary>Vulkan's conversion of a signed normalized 16-bit integer, which maps both −32,768 and −32,767 to −1.</summary>
    private static float SnormValue(short integer) => Math.Max(integer / 32767f, -1f);

    /// <summary>
    /// Records the copy of one mip of one layer of <paramref name="image"/>, of <paramref name="extent"/>, into
    /// <paramref name="buffer"/>, and leaves the image in the state that the renderer's own barriers continue from.
    /// </summary>
    private void CopyLayer(
        CommandBuffer commandBuffer,
        GpuImage image,
        ImageAspectFlags aspect,
        int layer,
        int mip,
        Extent2D extent,
        GpuBuffer buffer
    )
    {
        Vk vk = device.Vk;
        ResourceState state = image.State;
        var local = new BarrierBatch();
        local.Image(image, ImageUse.TransferSource);
        local.Buffer(buffer, BufferUse.TransferDestination);
        local.Flush(vk, commandBuffer);
        var region = new BufferImageCopy
        {
            ImageSubresource = new(aspectMask: aspect, mipLevel: (uint)mip, baseArrayLayer: (uint)layer, layerCount: 1),
            ImageExtent = new(extent.Width, extent.Height, 1),
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
    }

    /// <summary>
    /// Waits for the device to be idle, records <paramref name="record"/> into a command buffer of its own, submits it
    /// and waits for it. The read hooks use it.
    /// </summary>
    private void SubmitAndWait(Action<CommandBuffer> record)
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
            record(commandBuffer);
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
            WaitFor(fence);
        }
        finally
        {
            vk.DestroyFence(device.Device, fence, null);
            vk.DestroyCommandPool(device.Device, pool, null);
        }
    }

    private GpuBuffer CaptureBuffer(int slot, Extent2D extent)
    {
        ulong size = (ulong)extent.Width * extent.Height * 4;
        ref GpuBuffer? captureBuffer = ref captureBuffers[slot];
        if (captureBuffer == null || captureBuffer.Size < size)
        {
            // The slot's last frame finished and handed its capture over, so the old buffer is idle.
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

    /// <summary>The capture of a slot's last frame: its sink, its readback buffer and the size of the frame.</summary>
    private readonly record struct SlotCapture(ICaptureSink Sink, GpuBuffer Buffer, Extent2D Extent);

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

    /// <summary>The one context of the renderer's life, which holds what every frame shares (see <see cref="SetContext"/>).</summary>
    private FrameContext CreateContext() =>
        new()
        {
            Device = device,
            Layouts = layouts,
            Targets = targets,
            Geometry = geometry,
            Materials = materials,
            Barriers = barriers,
            Shadows = ShadowMap,
            ShadowAtlas = shadowAtlas,
            Timer = timer,
            Exposure = Exposure,
            Environment = environmentMap,
        };

    /// <summary>
    /// The render-sized images and output-sized post images of <paramref name="options"/>, owned by the renderer.
    /// </summary>
    private RenderTargets CreateTargets(RendererOptions options)
    {
        Extent2D extent = output.Extent.Width > 0 && output.Extent.Height > 0 ? output.Extent : new(1, 1);
        return Own(
            new RenderTargets(
                device: device,
                allocator: Allocator,
                layouts: layouts,
                extent: extent,
                glowLevels: GlowLevelsOf(options.Bloom),
                upscaling: upscaling,
                depthPyramid: options.ScreenTrace is ScreenTrace,
                subsurface: options.Subsurface is SeparableSubsurface
            )
        );
    }

    /// <summary>
    /// The images and tile records of key and fill that <paramref name="setting"/> asks for, which the renderer owns, or
    /// null for none.
    /// </summary>
    private KeyFillImages? CreateKeyFillImages(LightPairSetting setting) =>
        setting switch
        {
            KeyAndFill keyAndFill => Own(
                new KeyFillImages(
                    device: device,
                    allocator: Allocator,
                    layouts: layouts,
                    targets: targets,
                    setting: keyAndFill
                )
            ),
            EveryLightPair => null,
        };

    /// <summary>The scatter images that <paramref name="setting"/> asks for, which the renderer owns, or null for none.</summary>
    private ScatterImages? CreateScatterImages(SubsurfaceSetting setting) =>
        setting switch
        {
            SeparableSubsurface separable => Own(
                new ScatterImages(
                    device: device,
                    allocator: Allocator,
                    layouts: layouts,
                    targets: targets,
                    setting: separable
                )
            ),
            NoSubsurface => null,
        };

    /// <summary>
    /// Adds the scatter's two passes under <see cref="SeparableSubsurface"/>: the rows, then the columns, which read the row
    /// image that the rows pass writes.
    /// </summary>
    private void AddScatterPasses(ShaderLibrary shaders)
    {
        if (ScatterImages is not { } images)
            return;
        foreach (ScatterAxis axis in (ScatterAxis[])[ScatterAxis.Rows, ScatterAxis.Columns])
            Graph.Add(
                FrameGraph.Stage.SkinScatter,
                new SkinScatterPass(
                    device: device,
                    shaders: shaders,
                    layouts: layouts,
                    images: images,
                    constants: ShadingConstants,
                    axis: axis
                )
            );
    }

    /// <summary>The resources of each frame in flight, which the renderer owns.</summary>
    private FrameResources[] CreateFrames(int framesInFlight)
    {
        var made = new FrameResources[framesInFlight];
        for (int i = 0; i < made.Length; i++)
            made[i] = Own(
                new FrameResources(
                    device: device,
                    allocator: Allocator,
                    layouts: layouts,
                    textures: textures,
                    tables: BrdfTableImages,
                    exposure: Exposure.Buffer,
                    environment: environmentMap,
                    shadowAtlas: shadowAtlas,
                    index: i
                )
            );
        return made;
    }

    /// <summary>
    /// Adds the shadow and G-buffer passes to the graph, the local shadow pass when the renderer has an atlas, and the
    /// impostors when <paramref name="options"/> turn them on. Returns the impostor atlas and manager, or nulls when
    /// impostors are off, and the local shadow pass, or null without an atlas.
    /// </summary>
    private (ImpostorAtlas? Atlas, ImpostorManager? Manager, LocalShadowPass? LocalShadows) AddPassesThroughGeometry(
        ShaderLibrary shaders,
        RendererOptions options
    )
    {
        if (keptPositions is { } kept)
            Graph.Add(
                FrameGraph.Stage.Deformation,
                new DeformPass(device: device, shaders: shaders, layouts: layouts, deforms: deforms, kept: kept)
            );
        Graph.Add(
            FrameGraph.Stage.ShadowMaps,
            new ShadowPass(device: device, shaders: shaders, layouts: layouts, map: ShadowMap)
        );
        LocalShadowPass? localShadowPass = null;
        if (shadowAtlas is { } atlas)
        {
            // After the cascades, in the same stage: both only draw depth, and every shading pass reads both.
            localShadowPass = new LocalShadowPass(device: device, shaders: shaders, layouts: layouts, atlas: atlas);
            Graph.Add(FrameGraph.Stage.ShadowMaps, localShadowPass);
        }

        if (environmentMap is { } map)
        {
            Graph.Add(
                FrameGraph.Stage.EnvironmentFaces,
                new EnvironmentFacePass(
                    device: device,
                    shaders: shaders,
                    layouts: layouts,
                    map: map,
                    constants: FaceConstants
                )
            );
            Graph.Add(
                FrameGraph.Stage.EnvironmentPrefilter,
                new EnvironmentPrefilterPass(device: device, shaders: shaders, layouts: layouts, map: map)
            );
        }

        var geometryPass = new GeometryPass(
            device: device,
            shaders: shaders,
            layouts: layouts,
            constants: ShadingConstants,
            kept: keptPositions
        );
        Graph.Add(FrameGraph.Stage.GBuffer, geometryPass);
        Graph.Add(
            FrameGraph.Stage.SurfaceTest,
            new SurfaceTestPass(device: device, shaders: shaders, layouts: layouts, images: surfaceHistory)
        );
        if (!options.Impostors)
            return (null, null, localShadowPass);
        (ImpostorAtlas impostorAtlas, ImpostorManager manager) = AddImpostors(
            shaders: shaders,
            geometryPass: geometryPass,
            tiles: options.ImpostorTiles
        );
        return (impostorAtlas, manager, localShadowPass);
    }

    /// <summary>
    /// Makes the impostor atlas, which the renderer owns, and the manager, and adds their capture pass to the graph and
    /// their billboards to the ranges of <paramref name="geometryPass"/>.
    /// </summary>
    private (ImpostorAtlas Atlas, ImpostorManager Manager) AddImpostors(
        ShaderLibrary shaders,
        GeometryPass geometryPass,
        int tiles
    )
    {
        ImpostorAtlas atlas = Own(
            new ImpostorAtlas(device: device, allocator: Allocator, layouts: layouts, tiles: tiles)
        );
        var manager = new ImpostorManager(tiles);
        Batcher.Processors.Add(manager);
        var impostorPass = new ImpostorPass(
            device: device,
            shaders: shaders,
            layouts: layouts,
            atlas: atlas,
            manager: manager,
            constants: ShadingConstants
        );
        Graph.Add(FrameGraph.Stage.ImpostorCaptures, impostorPass);
        geometryPass.RangeDrawers.Add(impostorPass);
        return (atlas, manager);
    }

    /// <summary>The constants of the shading pipelines: the map is the ambient light exactly when the renderer has one.</summary>
    internal ShaderConstants ShadingConstants { get; }

    /// <summary>
    /// The shading pipelines' constants of a renderer with <paramref name="options"/> on <paramref name="driver"/>, with or
    /// without an environment map.
    /// </summary>
    private static ShaderConstants ShadingConstantsFor(RendererOptions options, DriverId driver, bool environmentMap) =>
        new(EnvironmentMap: environmentMap)
        {
            TileCornerMeans =
                options.LocalShadows is LocalShadowAtlas atlas
                && TileReadsFor(atlas.Reads, driver) == LocalShadowReads.SixteenCornerMeans,
            ScreenTraceOn = options.ScreenTrace is ScreenTrace,
            ReflectionSharingPrototype =
                options.ScreenTrace is ScreenTrace
                && Environment.GetEnvironmentVariable("TERTIUS_REFLECTION_SHARING") == "1",
        };

    /// <summary>
    /// The constants of the environment faces' pipelines. A face reads no ambient light and no signal of the screen, so it
    /// keeps those constants at their defaults. It reads a shadow tile as the shading pipelines do.
    /// </summary>
    internal ShaderConstants FaceConstants =>
        new(EnvironmentMap: false) { TileCornerMeans = ShadingConstants.TileCornerMeans };

    /// <summary>
    /// The cascades' shadow map and, under <see cref="LocalShadowAtlas"/>, the local shadow atlas with its bounds, which the
    /// renderer owns, written into the G-buffer set once: no image changes, and resizing rewrites only the other G-buffer
    /// bindings. Without the atlas its two bindings stay empty: they are partially bound.
    /// </summary>
    private (ShadowMap Map, ShadowAtlas? Atlas) CreateShadowImages(RendererOptions options)
    {
        ShadowMap map = Own(
            new ShadowMap(
                device: device,
                allocator: Allocator,
                size: options.ShadowMapSize,
                sets: options.CascadedLights
            )
        );
        ShadowAtlas? atlas = CreateShadowAtlas(options.LocalShadows);
        layouts.WriteImage(set: targets.GBufferSet, binding: 5, arrayElement: 0, image: map.Image);
        if (atlas is not null)
        {
            layouts.WriteImage(
                set: targets.GBufferSet,
                binding: ShadowAtlas.AtlasBinding,
                arrayElement: 0,
                image: atlas.Image
            );
            layouts.WriteImage(
                set: targets.GBufferSet,
                binding: ShadowAtlas.BoundsBinding,
                arrayElement: 0,
                image: atlas.Bounds
            );
        }

        return (map, atlas);
    }

    /// <summary>The local shadow atlas that <paramref name="setting"/> asks for, which the renderer owns, or null for none.</summary>
    private ShadowAtlas? CreateShadowAtlas(LocalShadowSetting setting) =>
        setting switch
        {
            NoLocalShadows => null,
            LocalShadowAtlas atlas => Own(
                new ShadowAtlas(device: device, allocator: Allocator, layouts: layouts, setting: atlas)
            ),
        };

    /// <summary>The queue of the uploads, within the device's largest texture and storage buffer.</summary>
    private UploadQueue CreateUploads() =>
        new(
            maxTextureSize: (int)device.Properties.Limits.MaxImageDimension2D,
            deformBytesLimit: device.Properties.Limits.MaxStorageBufferRange
        );

    /// <summary>The store of the deformed copies, which the renderer owns, at most one storage buffer's range in size.</summary>
    private DeformStore CreateDeformStore() =>
        Own(
            new DeformStore(
                device: device,
                allocator: Allocator,
                maxBytes: device.Properties.Limits.MaxStorageBufferRange
            )
        );

    /// <summary>The exposure buffer, the histogram and the readbacks of the frame slots of <paramref name="options"/>, which the renderer owns.</summary>
    private ExposureResources CreateExposure(RendererOptions options) =>
        Own(
            new ExposureResources(
                device: device,
                allocator: Allocator,
                layouts: layouts,
                setting: options.Exposure,
                framesInFlight: options.FramesInFlight
            )
        );

    /// <summary>The batcher of <paramref name="options"/>, which fits the cascade boxes of <see cref="ShadowMap"/>.</summary>
    private SceneBatcher CreateBatcher(RendererOptions options) =>
        new(
            cascadedLights: options.CascadedLights,
            shadowMapSize: ShadowMap.Size,
            depthRangeScale: ShadowMap.DepthRangeScale
        );

    /// <summary>The environment map that <paramref name="options"/> ask for, which the renderer owns, or null for none.</summary>
    private EnvironmentMap? CreateEnvironmentMap(RendererOptions options) =>
        options.EnvironmentMap switch
        {
            SceneEnvironmentMap setting => Own(
                new EnvironmentMap(
                    device: device,
                    allocator: Allocator,
                    layouts: layouts,
                    setting: setting,
                    framesInFlight: options.FramesInFlight
                )
            ),
            NoEnvironmentMap => null,
        };

    /// <summary>
    /// FSR 3.1's pass in <see cref="FrameGraph.Stage.Upscale"/> under <see cref="Fsr3Upscaling"/>, which reads the screen
    /// tracer's light-change mask, so the tracer's images come first. Null under any other upscaling.
    /// </summary>
    private UpscalePass? AddUpscalePass(ShaderLibrary shaders, int framesInFlight)
    {
        if (upscaling is not Fsr3Upscaling fsr)
            return null;
        UpscalePass pass = new(
            device: device,
            allocator: Allocator,
            shaders: shaders,
            targets: targets,
            surfaces: surfaceHistory,
            screenTrace: screenTrace,
            framesInFlight: framesInFlight,
            sharpness: fsr.Sharpness
        );
        Graph.Add(FrameGraph.Stage.Upscale, pass);
        return pass;
    }

    /// <summary>
    /// The screen tracer's images under <see cref="ScreenTrace"/>, with its two signals; the depth pyramid's pass after the
    /// G-buffer; and the tracer's four passes and the two accumulations in <see cref="FrameGraph.Stage.ScreenTrace"/>. Null
    /// under <see cref="NoScreenTrace"/>. The renderer owns them.
    /// </summary>
    private ScreenTraceImages? AddScreenTracePasses(ShaderLibrary shaders, ScreenTraceSetting setting)
    {
        if (setting is not ScreenTrace trace)
            return null;
        PyramidImages pyramid = targets.Pyramid ?? throw new UnreachableException("The targets make the pyramid.");
        Graph.Add(
            FrameGraph.Stage.DepthPyramid,
            new DepthPyramidPass(device: device, shaders: shaders, layouts: layouts, pyramid: pyramid)
        );
        TemporalSignal diffuse = CreateSignal(
            shaders: shaders,
            parameters: SignalParameters.Diffuse,
            name: "indirect diffuse",
            parts: 1
        );
        TemporalSignal specular = CreateSignal(
            shaders: shaders,
            parameters: SignalParameters.Specular(
                DebugReflectedPlace.Selected() == DebugReflectedPlace.Off ? 0 : trace.HandOverStart
            ),
            name: "indirect specular",
            parts: 1
        );
        DiffuseFilter? filter =
            DiffuseFilter.Selected() is { } kernel && diffuse.KeepsFastHistory
                ? CreateDiffuseFilter(shaders, diffuse, kernel)
                : null;
        ScreenTraceImages images = Own(
            new ScreenTraceImages(
                device: device,
                allocator: Allocator,
                layouts: layouts,
                targets: targets,
                surfaces: surfaceHistory,
                pyramid: pyramid,
                diffuse: diffuse,
                specular: specular,
                filter: filter,
                guided: LightPyramidPass.SwitchedOn()
            )
        );
        if (images.Guided)
            Graph.Add(
                FrameGraph.Stage.ScreenTrace,
                new LightPyramidPass(
                    device: device,
                    shaders: shaders,
                    layouts: layouts,
                    pyramid: images.LightPyramidImages
                )
            );
        AddRayPasses(shaders: shaders, trace: trace, images: images);
        Graph.Add(FrameGraph.Stage.ScreenTrace, new AccumulatePass(diffuse));
        if (filter is not null)
            Graph.Add(FrameGraph.Stage.ScreenTrace, new DiffuseFilterPass(filter));
        Graph.Add(FrameGraph.Stage.ScreenTrace, new AccumulatePass(specular));
        return images;
    }

    /// <summary>
    /// The tracer's four passes in <see cref="FrameGraph.Stage.ScreenTrace"/>: the ray plan, the reflection rays, the
    /// diffuse rays and the resolve.
    /// </summary>
    private void AddRayPasses(ShaderLibrary shaders, ScreenTrace trace, ScreenTraceImages images)
    {
        ScreenTraceKernel Kernel(string name, string shader, uint kind = 0) =>
            new(
                device: device,
                shaders: shaders,
                layouts: layouts,
                images: images,
                setting: trace,
                constants: ShadingConstants with
                {
                    RayKind = kind,
                },
                name: name,
                shader: shader
            );
        Graph.Add(
            FrameGraph.Stage.ScreenTrace,
            new ScreenTracePlanPass(Kernel("ray plan", "screen_trace_plan.comp"), images)
        );
        foreach (int kind in (int[])[0, 1])
        {
            ScreenTraceKernel kernel = Kernel(
                kind == 0 ? "reflection rays" : "diffuse rays",
                "screen_trace_block_rays.comp",
                (uint)kind
            );
            Graph.Add(
                FrameGraph.Stage.ScreenTrace,
                new ScreenTraceRaysPass(
                    kernel: kernel,
                    images: images,
                    ownRays: kernel.CreateOwnRays(shaders),
                    kind: kind
                )
            );
        }
        Graph.Add(
            FrameGraph.Stage.ScreenTrace,
            new ScreenTraceResolvePass(Kernel("screen trace resolve", "screen_trace_resolve.comp"), images)
        );
    }

    /// <summary>The bloom levels that <paramref name="bloom"/> builds: 0 for <see cref="NoBloom"/>.</summary>
    private static int GlowLevelsOf(BloomSetting bloom) =>
        bloom switch
        {
            Bloom on => on.Levels,
            NoBloom => 0,
        };

    /// <summary>
    /// Adds the direct pass and the composite, which writes the diffuse image too under <see cref="SeparableSubsurface"/>.
    /// </summary>
    private CompositePass AddLightingPasses(ShaderLibrary shaders)
    {
        // Before the direct pass, in its stage: it reads the tile records of this frame.
        if (keyFillImages is { } keyFill)
            Graph.Add(
                FrameGraph.Stage.DirectLight,
                new KeyFillPass(
                    device: device,
                    shaders: shaders,
                    layouts: layouts,
                    constants: ShadingConstants,
                    images: keyFill
                )
            );
        Graph.Add(
            FrameGraph.Stage.DirectLight,
            new DirectLightPass(
                device: device,
                shaders: shaders,
                layouts: layouts,
                constants: ShadingConstants,
                keyFill: keyFillImages
            )
        );
        var composite = new CompositePass(
            device: device,
            shaders: shaders,
            layouts: layouts,
            constants: ShadingConstants,
            scatter: ScatterImages is not null,
            tracer: screenTrace,
            targets: targets
        );
        Graph.Add(FrameGraph.Stage.Composite, composite);
        return composite;
    }

    /// <summary>
    /// Adds the passes from the direct pass and the composite to the readback to the graph, each in its own stage, the exposure pass only under
    /// <see cref="AutoExposure"/>, the bloom pass only with a <see cref="Bloom"/>, and FSR only under <see cref="Fsr3Upscaling"/>.
    /// </summary>
    private CompositePass AddPassesAfterGeometry(ShaderLibrary shaders, RendererOptions options)
    {
        CompositePass composite = AddLightingPasses(shaders);
        Graph.Add(FrameGraph.Stage.Haze, new HazePass(device: device, shaders: shaders, layouts: layouts));
        Graph.Add(
            FrameGraph.Stage.Transparent,
            new TransparentPass(device: device, shaders: shaders, layouts: layouts, constants: ShadingConstants)
        );
        if (options.Exposure is AutoExposure auto)
        {
            var exposure = new ExposurePass(device: device, shaders: shaders, layouts: layouts, setting: auto);
            Graph.Add(FrameGraph.Stage.Exposure, exposure);
            if (upscaling is Fsr3Upscaling)
                Graph.Add(FrameGraph.Stage.ExposureAdapt, new ExposureAdaptPass(exposure));
        }
        if (options.Bloom is Bloom on)
            Graph.Add(
                FrameGraph.Stage.Bloom,
                new BloomPass(device: device, shaders: shaders, layouts: layouts, bloom: on)
            );
        Graph.Add(
            FrameGraph.Stage.ToneMap,
            new ToneMapPass(
                device: device,
                shaders: shaders,
                layouts: layouts,
                outputFormat: output.Format,
                curve: options.ToneCurve,
                bloom: options.Bloom
            )
        );
        Graph.Add(
            FrameGraph.Stage.Overlay,
            new OutlinePass(device: device, shaders: shaders, layouts: layouts, outputFormat: output.Format)
        );
        Graph.Add(FrameGraph.Stage.Readback, new CapturePass());
        return composite;
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        owned.Push(resource);
        return resource;
    }

    private void ReleaseOwned()
    {
        // A capture still in flight is dropped with its buffer.
        for (int slot = 0; slot < captureBuffers.Length; slot++)
        {
            captureBuffers[slot]?.Dispose();
            captureBuffers[slot] = null;
            slotCaptures[slot] = null;
        }

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
