using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>Which cube of the environment map a test hook reads.</summary>
internal enum EnvironmentImage
{
    /// <summary>The radiance map: the drawn faces and their mips, weighted by solid angle.</summary>
    Radiance,

    /// <summary>The specular map: the GGX-prefiltered levels.</summary>
    Specular,
}

/// <summary>
/// The scene environment map (docs/plans/environment-map.md, "Images and buffers"): the radiance map that the face pass
/// draws, the specular map and the irradiance that the prefilter derives from it, one irradiance readback buffer per frame
/// slot, and the CPU state of which faces a frame draws. One map serves every frame in flight.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Each face of the radiance map holds the frame that last drew it, in the axes of <see cref="FrameId"/>.</item>
/// <item>The radiance map's mips, the specular map and the irradiance are a pure function of the six faces' level 0,
/// rebuilt in every drawn frame.</item>
/// <item>A slot's readback buffer holds the irradiance of the slot's last drawn frame, once its fence signals.</item>
/// </list>
/// </remarks>
internal sealed unsafe class EnvironmentMap : IDisposable
{
    public const Format RadianceFormat = Format.R16G16B16A16Sfloat;
    public const Format FaceDepthFormat = Format.D32Sfloat;

    /// <summary>The irradiance buffer: one vec4 per coefficient, xyz E_lm and w 0.</summary>
    private const ulong IrradianceBytes = Rendering.Irradiance.Coefficients * 16;

    private readonly VulkanDevice device;
    private readonly ImageView[] faceViews = new ImageView[CubeFaces.Count];
    private readonly ImageView[] levelViews;
    private readonly ImageView[] radianceLevelViews;
    private readonly GpuBuffer[] readbacks;

    public EnvironmentMap(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        SceneEnvironmentMap setting,
        int framesInFlight
    )
    {
        this.device = device;
        Setting = setting;
        Levels = PrefilterLevels.Count(setting.FaceSize);
        levelViews = new ImageView[Levels];
        radianceLevelViews = new ImageView[PrefilterLevels.RadianceLevels(setting.FaceSize)];
        readbacks = new GpuBuffer[framesInFlight];
        try
        {
            (Radiance, Specular, FaceDepth) = CreateImages(device: device, allocator: allocator, setting: setting);
            Irradiance = CreateBuffers(device: device, allocator: allocator, readbacks: readbacks);
            for (int layer = 0; layer < CubeFaces.Count; layer++)
                faceViews[layer] = Radiance.CreateLayerView((uint)layer);
            for (int level = 0; level < Levels; level++)
                levelViews[level] = Specular.CreateLevelView((uint)level);
            for (int level = 0; level < radianceLevelViews.Length; level++)
                radianceLevelViews[level] = Radiance.CreateLevelView((uint)level);
        }
        catch
        {
            Dispose();
            throw;
        }

        // The pool frees the set when the descriptor layouts are disposed.
        PrefilterSet = layouts.AllocateEnvironmentPrefilterSet();
        WritePrefilterSet(layouts);
    }

    public SceneEnvironmentMap Setting { get; }

    /// <summary>L, the levels of the specular map: <see cref="PrefilterLevels.Count"/> of the face size.</summary>
    public int Levels { get; }

    /// <summary>The radiance map: an RGBA16F cube of plain radiance and the open-sky share, with its full mip chain.</summary>
    public GpuImage Radiance { get; }

    /// <summary>The specular map: an RGBA16F cube whose level k holds the lobe of roughness k / (L − 1).</summary>
    public GpuImage Specular { get; }

    /// <summary>The depth target of a face: D32, reversed, cleared for each depth range.</summary>
    public GpuImage FaceDepth { get; }

    /// <summary>The irradiance: nine vec4, coefficient i in vec4 i, at binding 14 of every frame set.</summary>
    public GpuBuffer Irradiance { get; }

    /// <summary>Set 0 of <see cref="DescriptorLayouts.EnvironmentPrefilterLayout"/>, written once.</summary>
    public DescriptorSet PrefilterSet { get; }

    /// <summary>The map's frame: the camera frame whose axes the faces were drawn in, or null before the first drawn frame.</summary>
    public long? FrameId { get; private set; }

    /// <summary>The irradiance of the last frame whose GPU work finished, or null before one has.</summary>
    public IrradianceReading? Latest { get; private set; }

    /// <summary>The 2D view of level 0 of <paramref name="face"/>, the face pass's color attachment.</summary>
    public ImageView FaceView(CubeFace face) => faceViews[(int)face];

    /// <summary>
    /// The one choke point of the map's automatic machinery: the faces that drawn frame <paramref name="frame"/> draws, and
    /// the map's frame after it (docs/plans/environment-map.md, "Which faces a frame draws").
    /// </summary>
    public FaceMask FacesFor(long frame, long originFrameId, bool longStep)
    {
        FaceMask faces = EnvironmentFaces.RefreshesAll(FrameId, originFrameId, longStep)
            ? FaceMask.All
            : EnvironmentFaces.InTurn(frame, Setting.FacesPerFrame);
        FrameId = originFrameId;
        return faces;
    }

    /// <summary>
    /// Copies the irradiance that this frame's prefilter wrote into the readback buffer of <paramref name="slot"/>, right
    /// after the prefilter, so that a reading is the irradiance that this frame's passes use.
    /// </summary>
    public void RecordReadback(CommandBuffer commandBuffer, BarrierBatch barriers, int slot)
    {
        GpuBuffer readback = readbacks[slot];
        barriers.Buffer(Irradiance, BufferUse.TransferSource);
        barriers.Buffer(readback, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var region = new BufferCopy(srcOffset: 0, dstOffset: 0, size: IrradianceBytes);
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
    /// Reads the irradiance of <paramref name="frame"/>, the last frame that <paramref name="slot"/> drew, once its fence
    /// has signaled. A slot that has drawn no frame leaves <see cref="Latest"/> as it is.
    /// </summary>
    public void Collect(int slot, long frame)
    {
        if (frame < 0)
            return;
        Latest = new(
            Frame: frame,
            Irradiance: IrradianceOf(readbacks[slot].AsSpan<Vector4>(Rendering.Irradiance.Coefficients))
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

    /// <summary>The radiance map, the specular map and the face depth of <paramref name="setting"/>'s face size.</summary>
    private static (GpuImage Radiance, GpuImage Specular, GpuImage FaceDepth) CreateImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        SceneEnvironmentMap setting
    )
    {
        var extent = new Extent2D((uint)setting.FaceSize, (uint)setting.FaceSize);
        // Storage for its mips, and transfer source for the test hook that reads a level back.
        var radiance = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: RadianceFormat,
            usage: ImageUsageFlags.ColorAttachmentBit
                | ImageUsageFlags.SampledBit
                | ImageUsageFlags.StorageBit
                | ImageUsageFlags.TransferSrcBit,
            mipLevels: (uint)PrefilterLevels.RadianceLevels(setting.FaceSize),
            name: "environment radiance",
            arrayLayers: CubeFaces.Count,
            cube: true
        );
        GpuImage? specular = null;
        try
        {
            // Transfer source for the test hook too.
            specular = GpuImage.Create(
                device: device,
                allocator: allocator,
                extent: extent,
                format: RadianceFormat,
                usage: ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit,
                mipLevels: (uint)PrefilterLevels.Count(setting.FaceSize),
                name: "environment specular",
                arrayLayers: CubeFaces.Count,
                cube: true
            );
            var faceDepth = GpuImage.Create(
                device: device,
                allocator: allocator,
                extent: extent,
                format: FaceDepthFormat,
                usage: ImageUsageFlags.DepthStencilAttachmentBit,
                mipLevels: 1,
                name: "environment face depth"
            );
            return (radiance, specular, faceDepth);
        }
        catch
        {
            specular?.Dispose();
            radiance.Dispose();
            throw;
        }
    }

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

    /// <summary>Writes the prefilter set once: the radiance map, the level views of both cubes and the irradiance.</summary>
    private void WritePrefilterSet(DescriptorLayouts layouts)
    {
        layouts.WriteImage(set: PrefilterSet, binding: 0, arrayElement: 0, image: Radiance);
        for (int level = 0; level < levelViews.Length; level++)
            layouts.WriteView(
                set: PrefilterSet,
                binding: 2,
                arrayElement: (uint)level,
                view: levelViews[level],
                type: DescriptorType.StorageImage
            );
        layouts.WriteBuffer(set: PrefilterSet, binding: 3, type: DescriptorType.StorageBuffer, buffer: Irradiance);
        for (int level = 0; level < radianceLevelViews.Length; level++)
            layouts.WriteView(
                set: PrefilterSet,
                binding: 4,
                arrayElement: (uint)level,
                view: radianceLevelViews[level],
                type: DescriptorType.StorageImage
            );
    }

    public void Dispose()
    {
        foreach (ImageView view in levelViews)
            if (view.Handle != 0)
                device.Vk.DestroyImageView(device.Device, view, null);
        foreach (ImageView view in radianceLevelViews)
            if (view.Handle != 0)
                device.Vk.DestroyImageView(device.Device, view, null);
        foreach (ImageView view in faceViews)
            if (view.Handle != 0)
                device.Vk.DestroyImageView(device.Device, view, null);
        Array.Clear(levelViews);
        Array.Clear(radianceLevelViews);
        Array.Clear(faceViews);
        foreach (GpuBuffer? readback in readbacks)
            readback?.Dispose();
        Irradiance?.Dispose();
        FaceDepth?.Dispose();
        Specular?.Dispose();
        Radiance?.Dispose();
    }
}
