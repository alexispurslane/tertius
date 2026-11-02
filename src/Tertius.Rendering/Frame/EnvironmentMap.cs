using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>Which cube array of the environment maps a test hook reads.</summary>
internal enum EnvironmentImage
{
    /// <summary>The radiance maps: the drawn faces and their mips, weighted by solid angle.</summary>
    Radiance,

    /// <summary>The specular maps: the GGX-prefiltered levels.</summary>
    Specular,

    /// <summary>The irradiance cubes: D(n), the maps' ambient diffuse radiance, and its mips.</summary>
    Irradiance,
}

/// <summary>
/// The scene environment maps, one for each served frame of the camera's chain (docs/plans/environment-map.md, "Images and
/// buffers", and docs/plans/environment-maps-per-frame.md, "Images, buffers, growth and descriptors"): the radiance maps
/// that the face pass draws, the specular maps and the irradiance cubes that the prefilter derives from them, one readback
/// buffer per frame slot for the cubes' last level, and the CPU state of which faces a frame draws. Map i, root first, is
/// cube i of each cube array, layers 6i to 6i + 5. One set of maps serves every frame in flight.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Each face of map i holds the frame that last drew it, in the axes of map i's frame, for the served chain of
/// <see cref="DrawnFor"/>.</item>
/// <item>Map i's mips, specular levels and irradiance cube are a pure function of its six faces' level 0, rebuilt in every
/// drawn frame that draws one of its faces.</item>
/// <item>The images hold <see cref="Capacity"/> maps, at least the served maps of every frame since the last growth.</item>
/// <item>A slot's readback buffer holds the last level of the irradiance cubes of the served maps of the slot's last
/// submitted frame, once its fence signals.</item>
/// <item><see cref="DrawnFor"/> and each slot's count of maps change only in <see cref="Drew"/>, after a frame's
/// submission, so a frame that throws before it leaves them as they were.</item>
/// </list>
/// </remarks>
internal sealed unsafe class EnvironmentMap : IDisposable
{
    public const Format RadianceFormat = Format.R16G16B16A16Sfloat;
    public const Format FaceDepthFormat = Format.D32Sfloat;

    /// <summary>The last level of one map's irradiance cube: <see cref="Irradiance.Texels"/> RGBA16F texels.</summary>
    private const ulong MapReadbackBytes = Rendering.Irradiance.Texels * 4 * sizeof(ushort);

    /// <summary>Each readback buffer: room for every map that the renderer serves (decision 22).</summary>
    private const ulong ReadbackBytes = EnvironmentChain.MaxMaps * MapReadbackBytes;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly DescriptorSet[] specularDispatchSets;
    private readonly DescriptorSet[] downsampleSets;
    private readonly GpuBuffer[] readbacks;

    /// <summary>The served maps of each slot's last submitted frame, whose cubes' last level its readback buffer holds.</summary>
    private readonly int[] readbackMaps;

    private readonly IrradianceReading?[] readings = new IrradianceReading?[EnvironmentChain.MaxMaps];
    private readonly long[] drawnFor = new long[EnvironmentChain.MaxMaps];
    private readonly FaceMask[] facesOfMaps = new FaceMask[EnvironmentChain.MaxMaps];
    private int drawnForLength;
    private MapImages images;

    public EnvironmentMap(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        SceneEnvironmentMap setting,
        int framesInFlight
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        Setting = setting;
        Levels = PrefilterLevels.Count(setting.FaceSize);
        specularDispatchSets = new DescriptorSet[PrefilterLevels.Dispatches(Levels)];
        downsampleSets = new DescriptorSet[
            PrefilterLevels.DownsampleDispatches(PrefilterLevels.RadianceLevels(setting.FaceSize))
        ];
        readbacks = new GpuBuffer[framesInFlight];
        readbackMaps = new int[framesInFlight];
        try
        {
            FaceDepth = CreateFaceDepth(device: device, allocator: allocator, setting: setting);
            CreateReadbacks(device: device, allocator: allocator, readbacks: readbacks);
            images = new MapImages(device: device, allocator: allocator, setting: setting, levels: Levels, maps: 1);
            // The pool frees the sets when the descriptor layouts are disposed.
            PrefilterSet = layouts.AllocateEnvironmentPrefilterSet();
            for (int dispatch = 0; dispatch < specularDispatchSets.Length; dispatch++)
                specularDispatchSets[dispatch] = layouts.AllocateEnvironmentLevelsSet();
            for (int dispatch = 0; dispatch < downsampleSets.Length; dispatch++)
                downsampleSets[dispatch] = layouts.AllocateEnvironmentLevelsSet();
            IrradianceSet = layouts.AllocateEnvironmentLevelsSet();
            WriteImageSets();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public SceneEnvironmentMap Setting { get; }

    /// <summary>L, the levels of the specular maps: <see cref="PrefilterLevels.Count"/> of the face size.</summary>
    public int Levels { get; }

    /// <summary>The maps that the images hold: 1 at first, then the most served maps of any frame. It never shrinks.</summary>
    public int Capacity => images.Capacity;

    /// <summary>
    /// Counts the growths of the images, which make <see cref="Specular"/> and <see cref="Irradiance"/> anew. A frame slot
    /// rewrites its bindings of them when its count differs.
    /// </summary>
    public int ImagesVersion { get; private set; }

    /// <summary>
    /// The radiance maps: an RGBA16F cube array of plain radiance and the open-sky share, with its full mip chain, one cube
    /// per map.
    /// </summary>
    public GpuImage Radiance => images.Radiance;

    /// <summary>The specular maps: an RGBA16F cube array whose level k holds the lobe of roughness k / (L − 1), one cube per map.</summary>
    public GpuImage Specular => images.Specular;

    /// <summary>
    /// The irradiance cubes: an RGBA16F cube array of <see cref="IrradianceCube.Levels"/> levels, one cube per map, at
    /// binding 14 of every frame set. Level 0 holds D at each texel's direction, alpha the open-sky share that a Lambert
    /// surface sees, and each later level the mean of its children over their solid angles.
    /// </summary>
    public GpuImage Irradiance => images.Irradiance;

    /// <summary>The depth target of a face: D32, reversed, cleared for each depth range. The faces draw one after another.</summary>
    public GpuImage FaceDepth { get; }

    /// <summary>Set 0 of <see cref="DescriptorLayouts.EnvironmentPrefilterLayout"/>, written again at each growth.</summary>
    public DescriptorSet PrefilterSet { get; }

    /// <summary>
    /// The served chain, frame ids root first, that the stored faces were drawn for: the chain of the last submitted frame.
    /// Empty before the first submitted frame and after a growth.
    /// </summary>
    public ReadOnlySpan<long> DrawnFor => drawnFor.AsSpan(0, drawnForLength);

    /// <summary>
    /// The levels set of dispatch <paramref name="dispatch"/> of the specular maps, every layer, written again at each
    /// growth: binding b holds level first + b of <see cref="PrefilterLevels.LevelsOfDispatch"/>, and a binding past the
    /// dispatch's last level holds that last level again, a valid view that the dispatch never writes.
    /// </summary>
    public DescriptorSet SpecularDispatchSet(int dispatch) => specularDispatchSets[dispatch];

    /// <summary>
    /// The levels set of dispatch <paramref name="dispatch"/> of the radiance maps' downsample, every layer, written again
    /// at each growth: binding 0 holds the level that it reads, and binding b ≥ 1 level source + b of
    /// <see cref="PrefilterLevels.LevelsOfDownsample"/>. A binding past the dispatch's last level holds that last level
    /// again, a valid view that the dispatch never writes.
    /// </summary>
    public DescriptorSet DownsampleSet(int dispatch) => downsampleSets[dispatch];

    /// <summary>
    /// The levels set of the irradiance cubes, every layer, written again at each growth: binding b holds level b. The
    /// convolution writes binding 0, and the downsample reads binding 0 and writes the others.
    /// </summary>
    public DescriptorSet IrradianceSet { get; }

    /// <summary>The 2D view of level 0 of <paramref name="face"/> of map <paramref name="map"/>, the face pass's color attachment.</summary>
    public ImageView FaceView(int map, CubeFace face) => images.FaceViews[CubeFaces.Count * map + (int)face];

    /// <summary>
    /// The last level of the irradiance cubes of the first <paramref name="maps"/> maps from the last frame whose GPU work
    /// finished, each null before a frame that served it finished.
    /// </summary>
    public ReadOnlyMemory<IrradianceReading?> ReadingsOf(int maps) => readings.AsMemory(0, maps);

    /// <summary>
    /// The one choke point of the maps' automatic machinery (docs/plans/environment-maps-per-frame.md, "Which faces a frame
    /// draws", decision 26): grows the images when the served chain is longer than ever before, then returns the faces that
    /// drawn frame <paramref name="frame"/> draws of each served map. A map draws every face when it is not kept from
    /// <see cref="DrawnFor"/> or the draw takes a long step, and its slots in turn otherwise. Apart from a growth, which
    /// empties <see cref="DrawnFor"/> because the new images hold no face, it changes no state: <see cref="Drew"/> stores
    /// the served chain once the frame is submitted.
    /// </summary>
    /// <param name="served">The served chain, frame ids root first: from 1 to <see cref="EnvironmentChain.MaxMaps"/> of them.</param>
    /// <exception cref="ArgumentOutOfRangeException">The served chain has no frame or more than the renderer serves.</exception>
    public ReadOnlyMemory<FaceMask> FacesFor(long frame, ReadOnlySpan<long> served, bool longStep)
    {
        ArgumentOutOfRangeException.ThrowIfZero(served.Length, nameof(served));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(served.Length, EnvironmentChain.MaxMaps, nameof(served));
        if (served.Length > Capacity)
            Grow(served.Length);
        Span<FaceMask> faces = facesOfMaps.AsSpan(0, served.Length);
        EnvironmentFaces.InTurn(frame, Setting.FacesPerFrame, faces);
        for (int map = 0; map < faces.Length; map++)
            if (EnvironmentFaces.RefreshesAll(drawnFor: DrawnFor, served: served, map: map, longStep: longStep))
                faces[map] = FaceMask.All;
        return facesOfMaps.AsMemory(0, served.Length);
    }

    /// <summary>
    /// Records that the drawn frame in frame slot <paramref name="slot"/> was submitted for the served chain
    /// <paramref name="served"/>, with the faces that <see cref="FacesFor"/> chose, so that <see cref="DrawnFor"/> and the
    /// slot's count of maps describe the last finished step. A frame that throws before its submission leaves them as they
    /// were: the next frame draws again every face that it would have refreshed, because a face that no submitted frame
    /// drew holds the image's undefined content, which can be a NaN or an infinity on a Linux driver (ARCHITECTURE.md,
    /// "Portable Vulkan"), and the slot's next <see cref="Collect"/> reads only the maps that its last submitted frame
    /// copied.
    /// </summary>
    public void Drew(ReadOnlySpan<long> served, int slot)
    {
        served.CopyTo(drawnFor);
        drawnForLength = served.Length;
        readbackMaps[slot] = served.Length;
    }

    /// <summary>
    /// Leaves every level of the irradiance cubes read-only for this frame's readers, in
    /// <see cref="ImageUse.SampledVertexFragment"/>, and copies the last level of the first <paramref name="maps"/> maps'
    /// cubes into the readback buffer of <paramref name="slot"/>, right after the prefilter, so that a reading is the
    /// irradiance that this frame's passes use. It starts from the cubes' recorded state: the storage writes of this
    /// frame's prefilter, or the reads of an earlier frame. <see cref="Drew"/> records the count once the frame is
    /// submitted.
    /// </summary>
    public void RecordReadback(CommandBuffer commandBuffer, BarrierBatch barriers, int slot, int maps)
    {
        GpuImage cube = Irradiance;
        GpuBuffer readback = readbacks[slot];
        ResourceState before = cube.State;
        ResourceState sampled = ResourceUses.Describe(ImageUse.SampledVertexFragment);
        AccessFlags2 written = before.Access & ResourceUses.WriteMask;
        const uint last = IrradianceCube.Levels - 1;
        barriers.ImageLevels(
            image: cube,
            baseMip: last,
            mipCount: 1,
            oldLayout: before.Layout,
            newLayout: ImageLayout.TransferSrcOptimal,
            srcStages: before.Stages,
            srcAccess: written,
            dstStages: PipelineStageFlags2.AllTransferBit,
            dstAccess: AccessFlags2.TransferReadBit
        );
        barriers.ImageLevels(
            image: cube,
            baseMip: 0,
            mipCount: last,
            oldLayout: before.Layout,
            newLayout: sampled.Layout,
            srcStages: before.Stages,
            srcAccess: written,
            dstStages: sampled.Stages,
            dstAccess: sampled.Access
        );
        barriers.Buffer(readback, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var region = new BufferImageCopy
        {
            ImageSubresource = new(
                aspectMask: ImageAspectFlags.ColorBit,
                mipLevel: last,
                baseArrayLayer: 0,
                layerCount: (uint)(CubeFaces.Count * maps)
            ),
            ImageExtent = new(IrradianceCube.CoarsestSize, IrradianceCube.CoarsestSize, 1),
        };
        device.Vk.CmdCopyImageToBuffer(
            commandBuffer: commandBuffer,
            srcImage: cube.Handle,
            srcImageLayout: ImageLayout.TransferSrcOptimal,
            dstBuffer: readback.Handle,
            regionCount: 1,
            pRegions: &region
        );

        barriers.Buffer(readback, BufferUse.HostRead);
        // A copy only reads, so the layout's change waits for it with no access to make visible.
        barriers.ImageLevels(
            image: cube,
            baseMip: last,
            mipCount: 1,
            oldLayout: ImageLayout.TransferSrcOptimal,
            newLayout: sampled.Layout,
            srcStages: PipelineStageFlags2.AllTransferBit,
            srcAccess: AccessFlags2.None,
            dstStages: sampled.Stages,
            dstAccess: sampled.Access
        );
        barriers.Flush(device.Vk, commandBuffer);
        cube.State = sampled;
    }

    /// <summary>
    /// Reads the irradiance of the maps that <paramref name="frame"/>, the last frame that <paramref name="slot"/>
    /// submitted, served, once its fence has signaled. A slot that has drawn no frame leaves the readings as they are, and
    /// so does a frame for the maps past the ones that it served.
    /// </summary>
    public void Collect(int slot, long frame)
    {
        if (frame < 0)
            return;
        ReadOnlySpan<Half> texels = readbacks[slot]
            .AsSpan<Half>(EnvironmentChain.MaxMaps * Rendering.Irradiance.Halves);
        for (int map = 0; map < readbackMaps[slot]; map++)
            readings[map] = new(
                Frame: frame,
                Irradiance: Rendering.Irradiance.Of(
                    texels.Slice(map * Rendering.Irradiance.Halves, Rendering.Irradiance.Halves)
                )
            );
    }

    /// <summary>
    /// Makes the images hold <paramref name="maps"/> maps (decision 23): waits for the device, as a resize does, makes the
    /// cube arrays and their views anew, then disposes the old ones, points the map's own sets at the new ones, and empties
    /// <see cref="DrawnFor"/>, because the new images hold no face. A growth that throws leaves the old images whole. Each
    /// frame slot rewrites its bindings of the specular maps and the irradiance cubes by <see cref="ImagesVersion"/>.
    /// </summary>
    private void Grow(int maps)
    {
        device.WaitIdle();
        var grown = new MapImages(device: device, allocator: allocator, setting: Setting, levels: Levels, maps: maps);
        images.Dispose();
        images = grown;
        WriteImageSets();
        drawnForLength = 0;
        ImagesVersion++;
    }

    /// <summary>The depth target of a face of <paramref name="setting"/>'s face size.</summary>
    private static GpuImage CreateFaceDepth(
        VulkanDevice device,
        DeviceAllocator allocator,
        SceneEnvironmentMap setting
    ) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: new((uint)setting.FaceSize, (uint)setting.FaceSize),
            format: FaceDepthFormat,
            usage: ImageUsageFlags.DepthStencilAttachmentBit,
            mipLevels: 1,
            name: "environment face depth"
        );

    /// <summary>
    /// Fills <paramref name="readbacks"/> with one readback buffer per frame slot. The map's own disposal frees the buffers
    /// made before a failure.
    /// </summary>
    private static void CreateReadbacks(VulkanDevice device, DeviceAllocator allocator, GpuBuffer[] readbacks)
    {
        for (int slot = 0; slot < readbacks.Length; slot++)
            readbacks[slot] = GpuBuffer.Create(
                device: device,
                allocator: allocator,
                size: ReadbackBytes,
                usage: BufferUsageFlags.TransferDstBit,
                memoryUsage: MemoryUsage.Readback,
                name: $"frame {slot} irradiance readback"
            );
    }

    /// <summary>
    /// Points the sets at the current images: the radiance maps in the prefilter set, the level views of the radiance and
    /// specular maps in the levels sets of their dispatches, and those of the irradiance cubes in theirs.
    /// </summary>
    private void WriteImageSets()
    {
        layouts.WriteImage(set: PrefilterSet, binding: 0, arrayElement: 0, image: Radiance);
        for (int dispatch = 0; dispatch < specularDispatchSets.Length; dispatch++)
        {
            (int first, int count) = PrefilterLevels.LevelsOfDispatch(dispatch: dispatch, levels: Levels);
            WriteLevelsSet(set: specularDispatchSets[dispatch], views: images.LevelViews.AsSpan(first, count));
        }

        int radianceLevels = images.RadianceLevelViews.Length;
        for (int dispatch = 0; dispatch < downsampleSets.Length; dispatch++)
        {
            (int source, int count) = PrefilterLevels.LevelsOfDownsample(dispatch: dispatch, levels: radianceLevels);
            WriteLevelsSet(set: downsampleSets[dispatch], views: images.RadianceLevelViews.AsSpan(source, count + 1));
        }

        WriteLevelsSet(set: IrradianceSet, views: images.IrradianceLevelViews);
    }

    /// <summary>
    /// Writes binding b of the levels set <paramref name="set"/> with view b of <paramref name="views"/>, or with the last
    /// view for a binding past them.
    /// </summary>
    private void WriteLevelsSet(DescriptorSet set, ReadOnlySpan<ImageView> views)
    {
        for (int binding = 0; binding < PrefilterLevels.LevelsPerDispatch; binding++)
            layouts.WriteStorageImage(
                set: set,
                binding: (uint)binding,
                view: views[Math.Min(binding, views.Length - 1)]
            );
    }

    public void Dispose()
    {
        images?.Dispose();
        foreach (GpuBuffer? readback in readbacks)
            readback?.Dispose();
        FaceDepth?.Dispose();
    }

    /// <summary>
    /// The cube arrays of <see cref="Capacity"/> maps and their views, which a growth makes anew as one. Map i is layers 6i
    /// to 6i + 5 of each.
    /// </summary>
    private sealed class MapImages : IDisposable
    {
        private readonly VulkanDevice device;

        /// <summary>Makes the cube arrays and their views, and releases what it made when one of them fails.</summary>
        public MapImages(
            VulkanDevice device,
            DeviceAllocator allocator,
            SceneEnvironmentMap setting,
            int levels,
            int maps
        )
        {
            this.device = device;
            Capacity = maps;
            var extent = new Extent2D((uint)setting.FaceSize, (uint)setting.FaceSize);
            uint layers = (uint)(CubeFaces.Count * maps);
            int radianceLevels = PrefilterLevels.RadianceLevels(setting.FaceSize);
            FaceViews = new ImageView[layers];
            LevelViews = new ImageView[levels];
            RadianceLevelViews = new ImageView[radianceLevels];
            IrradianceLevelViews = new ImageView[IrradianceCube.Levels];
            try
            {
                // Storage for its mips, and transfer source for the test hook that reads a level back.
                Radiance = GpuImage.Create(
                    device: device,
                    allocator: allocator,
                    extent: extent,
                    format: RadianceFormat,
                    usage: ImageUsageFlags.ColorAttachmentBit
                        | ImageUsageFlags.SampledBit
                        | ImageUsageFlags.StorageBit
                        | ImageUsageFlags.TransferSrcBit,
                    mipLevels: (uint)radianceLevels,
                    name: "environment radiance",
                    arrayLayers: layers,
                    cubeArray: true
                );
                Specular = WrittenCubes(
                    device: device,
                    allocator: allocator,
                    extent: extent,
                    levels: levels,
                    layers: layers,
                    name: "environment specular"
                );
                Irradiance = WrittenCubes(
                    device: device,
                    allocator: allocator,
                    extent: new(IrradianceCube.FaceSize, IrradianceCube.FaceSize),
                    levels: IrradianceCube.Levels,
                    layers: layers,
                    name: "environment irradiance"
                );
                for (int layer = 0; layer < FaceViews.Length; layer++)
                    FaceViews[layer] = Radiance.CreateLayerView((uint)layer);
                CreateLevelViews(Specular, LevelViews);
                CreateLevelViews(Radiance, RadianceLevelViews);
                CreateLevelViews(Irradiance, IrradianceLevelViews);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// A cube array that compute shaders write level by level and the readers sample: storage and sampled, and a
        /// transfer source for the test hook that reads a level back and for the irradiance's readback.
        /// </summary>
        private static GpuImage WrittenCubes(
            VulkanDevice device,
            DeviceAllocator allocator,
            Extent2D extent,
            int levels,
            uint layers,
            string name
        ) =>
            GpuImage.Create(
                device: device,
                allocator: allocator,
                extent: extent,
                format: RadianceFormat,
                usage: ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit,
                mipLevels: (uint)levels,
                name: name,
                arrayLayers: layers,
                cubeArray: true
            );

        /// <summary>Fills <paramref name="views"/> with a view of each level of <paramref name="image"/>, from 0.</summary>
        private static void CreateLevelViews(GpuImage image, ImageView[] views)
        {
            for (int level = 0; level < views.Length; level++)
                views[level] = image.CreateLevelView((uint)level);
        }

        /// <summary>The maps that the cube arrays hold.</summary>
        public int Capacity { get; }

        public GpuImage Radiance { get; }
        public GpuImage Specular { get; }
        public GpuImage Irradiance { get; }

        /// <summary>The 2D view of level 0 of each layer of <see cref="Radiance"/>, in layer order.</summary>
        public ImageView[] FaceViews { get; }

        /// <summary>The view of each level of <see cref="Specular"/>, every layer.</summary>
        public ImageView[] LevelViews { get; }

        /// <summary>The view of each level of <see cref="Radiance"/>, every layer.</summary>
        public ImageView[] RadianceLevelViews { get; }

        /// <summary>The view of each level of <see cref="Irradiance"/>, every layer.</summary>
        public ImageView[] IrradianceLevelViews { get; }

        public void Dispose()
        {
            DestroyViews(LevelViews);
            DestroyViews(RadianceLevelViews);
            DestroyViews(IrradianceLevelViews);
            DestroyViews(FaceViews);
            Irradiance?.Dispose();
            Specular?.Dispose();
            Radiance?.Dispose();
        }

        /// <summary>Destroys the views that were made. A view that was not made is a null handle.</summary>
        private void DestroyViews(ImageView[] views)
        {
            foreach (ImageView view in views)
                if (view.Handle != 0)
                    device.Vk.DestroyImageView(device.Device, view, null);
            Array.Clear(views);
        }
    }
}
