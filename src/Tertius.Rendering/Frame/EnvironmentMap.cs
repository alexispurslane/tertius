using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>Which cube array of the environment maps a test hook reads.</summary>
internal enum EnvironmentImage
{
    /// <summary>The radiance maps: the drawn faces and their mips, weighted by solid angle.</summary>
    Radiance,

    /// <summary>The specular maps: the GGX-prefiltered levels.</summary>
    Specular,
}

/// <summary>
/// The scene environment maps, one for each served frame of the camera's chain (docs/plans/environment-map.md, "Images and
/// buffers", and docs/plans/environment-maps-per-frame.md, "Images, buffers, growth and descriptors"): the radiance maps
/// that the face pass draws, the specular maps and the irradiance that the prefilter derives from them, one irradiance
/// readback buffer per frame slot, and the CPU state of which faces a frame draws. Map i, root first, is cube i of each
/// cube array, layers 6i to 6i + 5, and coefficients 9i to 9i + 8 of the irradiance. One set of maps serves every frame in
/// flight.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Each face of map i holds the frame that last drew it, in the axes of map i's frame, for the served chain of
/// <see cref="DrawnFor"/>.</item>
/// <item>Map i's mips, specular levels and irradiance are a pure function of its six faces' level 0, rebuilt in every drawn
/// frame that draws one of its faces.</item>
/// <item>The images hold <see cref="Capacity"/> maps, at least the served maps of every frame since the last growth.</item>
/// <item>A slot's readback buffer holds the irradiance of the served maps of the slot's last submitted frame, once its
/// fence signals.</item>
/// <item><see cref="DrawnFor"/> and each slot's count of maps change only in <see cref="Drew"/>, after a frame's
/// submission, so a frame that throws before it leaves them as they were.</item>
/// </list>
/// </remarks>
internal sealed unsafe class EnvironmentMap : IDisposable
{
    public const Format RadianceFormat = Format.R16G16B16A16Sfloat;
    public const Format FaceDepthFormat = Format.D32Sfloat;

    /// <summary>The irradiance of one map: one vec4 per coefficient, xyz E_lm and w 0.</summary>
    private const ulong MapIrradianceBytes = Rendering.Irradiance.Coefficients * 16;

    /// <summary>The irradiance buffer and each readback buffer: room for every map that the renderer serves (decision 22).</summary>
    private const ulong IrradianceBytes = EnvironmentChain.MaxMaps * MapIrradianceBytes;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly DescriptorSet[] specularLevelSets;
    private readonly DescriptorSet[] radianceLevelSets;
    private readonly GpuBuffer[] readbacks;

    /// <summary>The served maps of each slot's last submitted frame, whose irradiance its readback buffer holds.</summary>
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
        specularLevelSets = new DescriptorSet[Levels];
        radianceLevelSets = new DescriptorSet[PrefilterLevels.RadianceLevels(setting.FaceSize)];
        readbacks = new GpuBuffer[framesInFlight];
        readbackMaps = new int[framesInFlight];
        try
        {
            FaceDepth = CreateFaceDepth(device: device, allocator: allocator, setting: setting);
            Irradiance = CreateBuffers(device: device, allocator: allocator, readbacks: readbacks);
            images = new MapImages(device: device, allocator: allocator, setting: setting, levels: Levels, maps: 1);
            // The pool frees the sets when the descriptor layouts are disposed.
            PrefilterSet = layouts.AllocateEnvironmentPrefilterSet();
            for (int level = 0; level < specularLevelSets.Length; level++)
                specularLevelSets[level] = layouts.AllocateEnvironmentLevelSet();
            for (int level = 0; level < radianceLevelSets.Length; level++)
                radianceLevelSets[level] = layouts.AllocateEnvironmentLevelSet();
            layouts.WriteBuffer(set: PrefilterSet, binding: 2, type: DescriptorType.StorageBuffer, buffer: Irradiance);
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
    /// Counts the growths of the images, which make <see cref="Specular"/> anew. A frame slot rewrites its binding of the
    /// specular maps when its count differs.
    /// </summary>
    public int ImagesVersion { get; private set; }

    /// <summary>
    /// The radiance maps: an RGBA16F cube array of plain radiance and the open-sky share, with its full mip chain, one cube
    /// per map.
    /// </summary>
    public GpuImage Radiance => images.Radiance;

    /// <summary>The specular maps: an RGBA16F cube array whose level k holds the lobe of roughness k / (L − 1), one cube per map.</summary>
    public GpuImage Specular => images.Specular;

    /// <summary>The depth target of a face: D32, reversed, cleared for each depth range. The faces draw one after another.</summary>
    public GpuImage FaceDepth { get; }

    /// <summary>The irradiance: nine vec4 per map, coefficient i of map m in vec4 9m + i, at binding 14 of every frame set.</summary>
    public GpuBuffer Irradiance { get; }

    /// <summary>Set 0 of <see cref="DescriptorLayouts.EnvironmentPrefilterLayout"/>, written again at each growth.</summary>
    public DescriptorSet PrefilterSet { get; }

    /// <summary>
    /// The served chain, frame ids root first, that the stored faces were drawn for: the chain of the last submitted frame.
    /// Empty before the first submitted frame and after a growth.
    /// </summary>
    public ReadOnlySpan<long> DrawnFor => drawnFor.AsSpan(0, drawnForLength);

    /// <summary>The level set of <paramref name="level"/> of the specular maps, every layer.</summary>
    public DescriptorSet SpecularLevelSet(int level) => specularLevelSets[level];

    /// <summary>The level set of <paramref name="level"/> of the radiance maps, every layer.</summary>
    public DescriptorSet RadianceLevelSet(int level) => radianceLevelSets[level];

    /// <summary>The 2D view of level 0 of <paramref name="face"/> of map <paramref name="map"/>, the face pass's color attachment.</summary>
    public ImageView FaceView(int map, CubeFace face) => images.FaceViews[CubeFaces.Count * map + (int)face];

    /// <summary>
    /// The irradiance of the first <paramref name="maps"/> maps from the last frame whose GPU work finished, each null
    /// before a frame that served it finished.
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
    /// Copies the irradiance of the first <paramref name="maps"/> maps, which this frame's prefilter left in the buffer,
    /// into the readback buffer of <paramref name="slot"/>, right after the prefilter, so that a reading is the irradiance
    /// that this frame's passes use. <see cref="Drew"/> records the count once the frame is submitted.
    /// </summary>
    public void RecordReadback(CommandBuffer commandBuffer, BarrierBatch barriers, int slot, int maps)
    {
        GpuBuffer readback = readbacks[slot];
        barriers.Buffer(Irradiance, BufferUse.TransferSource);
        barriers.Buffer(readback, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var region = new BufferCopy(srcOffset: 0, dstOffset: 0, size: (ulong)maps * MapIrradianceBytes);
        device.Vk.CmdCopyBuffer(
            commandBuffer: commandBuffer,
            srcBuffer: Irradiance.Handle,
            dstBuffer: readback.Handle,
            regionCount: 1,
            pRegions: &region
        );

        // Left in the batch: the next flush records it, before the frame's fence.
        barriers.Buffer(readback, BufferUse.HostRead);
    }

    /// <summary>
    /// Reads the irradiance of the maps that <paramref name="frame"/>, the last frame that <paramref name="slot"/>
    /// submitted, served, once its fence has signaled. A slot that has drawn no frame leaves the readings as they are, and so does a
    /// frame for the maps past the ones that it served.
    /// </summary>
    public void Collect(int slot, long frame)
    {
        if (frame < 0)
            return;
        ReadOnlySpan<Vector4> coefficients = readbacks[slot]
            .AsSpan<Vector4>(EnvironmentChain.MaxMaps * Rendering.Irradiance.Coefficients);
        for (int map = 0; map < readbackMaps[slot]; map++)
            readings[map] = new(
                Frame: frame,
                Irradiance: IrradianceOf(
                    coefficients.Slice(map * Rendering.Irradiance.Coefficients, Rendering.Irradiance.Coefficients)
                )
            );
    }

    /// <summary>The irradiance in nine vec4 as the irradiance buffer lays them out, xyz E_lm.</summary>
    internal static Irradiance IrradianceOf(ReadOnlySpan<Vector4> coefficients)
    {
        Span<Vector3> values = stackalloc Vector3[Rendering.Irradiance.Coefficients];
        for (int i = 0; i < values.Length; i++)
            values[i] = new(coefficients[i].X, coefficients[i].Y, coefficients[i].Z);
        return new(values);
    }

    /// <summary>
    /// Makes the images hold <paramref name="maps"/> maps (decision 23): waits for the device, as a resize does, makes the
    /// cube arrays and their views anew, then disposes the old ones, points the map's own sets at the new ones, and empties
    /// <see cref="DrawnFor"/>, because the new images hold no face. A growth that throws leaves the old images whole. Each
    /// frame slot rewrites its binding of the specular maps by <see cref="ImagesVersion"/>.
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
    /// Fills <paramref name="readbacks"/> with one readback buffer per frame slot, then makes the irradiance buffer. The
    /// map's own disposal frees the buffers made before a failure.
    /// </summary>
    private static GpuBuffer CreateBuffers(VulkanDevice device, DeviceAllocator allocator, GpuBuffer[] readbacks)
    {
        for (int slot = 0; slot < readbacks.Length; slot++)
            readbacks[slot] = GpuBuffer.Create(
                device: device,
                allocator: allocator,
                size: IrradianceBytes,
                usage: BufferUsageFlags.TransferDstBit,
                memoryUsage: MemoryUsage.Readback,
                name: $"frame {slot} irradiance readback"
            );
        return GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: IrradianceBytes,
            usage: BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "environment irradiance"
        );
    }

    /// <summary>Points the sets at the current images: the radiance maps in the prefilter set, and each level view in its level set.</summary>
    private void WriteImageSets()
    {
        layouts.WriteImage(set: PrefilterSet, binding: 0, arrayElement: 0, image: Radiance);
        for (int level = 0; level < specularLevelSets.Length; level++)
            layouts.WriteStorageImage(set: specularLevelSets[level], binding: 0, view: images.LevelViews[level]);
        for (int level = 0; level < radianceLevelSets.Length; level++)
            layouts.WriteStorageImage(
                set: radianceLevelSets[level],
                binding: 0,
                view: images.RadianceLevelViews[level]
            );
    }

    public void Dispose()
    {
        images?.Dispose();
        foreach (GpuBuffer? readback in readbacks)
            readback?.Dispose();
        Irradiance?.Dispose();
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
                // Transfer source for the test hook too.
                Specular = GpuImage.Create(
                    device: device,
                    allocator: allocator,
                    extent: extent,
                    format: RadianceFormat,
                    usage: ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit,
                    mipLevels: (uint)levels,
                    name: "environment specular",
                    arrayLayers: layers,
                    cubeArray: true
                );
                for (int layer = 0; layer < FaceViews.Length; layer++)
                    FaceViews[layer] = Radiance.CreateLayerView((uint)layer);
                for (int level = 0; level < LevelViews.Length; level++)
                    LevelViews[level] = Specular.CreateLevelView((uint)level);
                for (int level = 0; level < RadianceLevelViews.Length; level++)
                    RadianceLevelViews[level] = Radiance.CreateLevelView((uint)level);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>The maps that the cube arrays hold.</summary>
        public int Capacity { get; }

        public GpuImage Radiance { get; }
        public GpuImage Specular { get; }

        /// <summary>The 2D view of level 0 of each layer of <see cref="Radiance"/>, in layer order.</summary>
        public ImageView[] FaceViews { get; }

        /// <summary>The view of each level of <see cref="Specular"/>, every layer.</summary>
        public ImageView[] LevelViews { get; }

        /// <summary>The view of each level of <see cref="Radiance"/>, every layer.</summary>
        public ImageView[] RadianceLevelViews { get; }

        public void Dispose()
        {
            DestroyViews(LevelViews);
            DestroyViews(RadianceLevelViews);
            DestroyViews(FaceViews);
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
