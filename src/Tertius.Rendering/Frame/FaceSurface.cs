using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The surface record of an environment face (Shaders/face_surface.glsl): for each texel, the nearest opaque surface as
/// the values that the light loop reads. The face pass writes it range by range, then lights each texel once from it
/// (<see cref="EnvironmentFacePass"/>). The faces draw one after another, so they share one record.
/// </summary>
/// <remarks>
/// <para>The record is one RGBA32F image of <see cref="Count"/> layers, so it holds the full floats that a surface's
/// fragment computed. The fourth channel of layer 0 marks a texel with a surface: 0 means none. So a face starts its
/// record with layer 0 cleared to 0, and the light pass reads the other layers only where layer 0 names a surface.</para>
/// <para>The layers are one image, not six, so that the light pass binds one sampled image. Its fragment stage then
/// sees 16 sampled images, the least that the Vulkan specification promises
/// (ARCHITECTURE.md, "Required device features").</para>
/// </remarks>
internal sealed unsafe class FaceSurface : IDisposable
{
    /// <summary>The layers of the record, which are the outputs of a face's surface shader, in their order.</summary>
    public const int Count = 6;

    /// <summary>Four full floats per texel. Vulkan requires the format as a color attachment and a sampled image.</summary>
    public const Format ImageFormat = Format.R32G32B32A32Sfloat;

    private readonly VulkanDevice device;
    private readonly ImageView[] layerViews = new ImageView[Count];
    private readonly GpuImage image;

    /// <exception cref="NotSupportedException">The device cannot draw or sample <see cref="ImageFormat"/>.</exception>
    public FaceSurface(VulkanDevice device, DeviceAllocator allocator, DescriptorLayouts layouts, int faceSize)
    {
        if (FormatProblem(device.FormatProperties(ImageFormat).OptimalTilingFeatures) is { } problem)
            throw new NotSupportedException($"{problem} ({device.DeviceName})");
        this.device = device;
        try
        {
            image = GpuImage.Create(
                device: device,
                allocator: allocator,
                extent: new((uint)faceSize, (uint)faceSize),
                format: ImageFormat,
                usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
                mipLevels: 1,
                name: "environment face surface",
                arrayLayers: Count
            );
            for (int layer = 0; layer < Count; layer++)
                layerViews[layer] = image.CreateLayerView((uint)layer);
            // The pool frees the set when the descriptor layouts are disposed.
            Set = layouts.AllocateFaceSurfaceSet();
            layouts.WriteImage(set: Set, binding: 0, arrayElement: 0, image: image);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The formats of the record's layers, in the order of the surface shader's outputs.</summary>
    public static Format[] Formats => [.. Enumerable.Repeat(ImageFormat, Count)];

    /// <summary>Set 3 of <see cref="DescriptorLayouts.FaceLightLayout"/>: the record, as one array of its layers.</summary>
    public DescriptorSet Set { get; }

    /// <summary>
    /// Why a device whose optimal tiling of <see cref="ImageFormat"/> offers <paramref name="features"/> cannot hold the
    /// record, or null when it can: the surfaces draw it, and the light pass fetches it.
    /// </summary>
    internal static string? FormatProblem(FormatFeatureFlags features)
    {
        const FormatFeatureFlags needed = FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit;
        FormatFeatureFlags missing = needed & ~features;
        return missing == 0 ? null : $"{ImageFormat} lacks {missing} in optimal tiling.";
    }

    /// <summary>
    /// Prepares the record for the surfaces of one depth range of a face: declares its use to
    /// <paramref name="barriers"/>, and fills <paramref name="attachments"/>, one per layer, for the range's rendering.
    /// With <paramref name="anew"/>, the range is the face's first. The record of the face before is thrown away and the
    /// new one starts without a surface: layer 0 clears to 0, and the other layers start undefined, because the light
    /// pass reads them only where a surface of this face wrote them. Without it, the range draws over the record of the
    /// ranges behind it.
    /// </summary>
    public void DrawnBy(BarrierBatch barriers, Span<RenderingAttachmentInfo> attachments, bool anew)
    {
        barriers.Image(image, ImageUse.ColorAttachment, discard: anew);
        for (int i = 0; i < Count; i++)
            attachments[i] = new()
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = layerViews[i],
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = LoadOf(layer: i, anew: anew),
                StoreOp = AttachmentStoreOp.Store,
                // The zero that marks a texel without a surface, where the load op clears.
                ClearValue = default,
            };
    }

    /// <summary>Prepares the finished record for the fetches of the face's light pass.</summary>
    public void ReadBy(BarrierBatch barriers) => barriers.Image(image, ImageUse.SampledFragment);

    public void Dispose()
    {
        // The views go before their image. A constructor that failed left the views that it did not make at 0, and
        // possibly no image.
        foreach (ImageView view in layerViews)
            if (view.Handle != 0)
                device.Vk.DestroyImageView(device.Device, view, null);
        image?.Dispose();
    }

    /// <summary>How layer <paramref name="layer"/> starts in the rendering of a range, which starts the record <paramref name="anew"/> or not.</summary>
    private static AttachmentLoadOp LoadOf(int layer, bool anew)
    {
        if (!anew)
            return AttachmentLoadOp.Load;
        return layer == 0 ? AttachmentLoadOp.Clear : AttachmentLoadOp.DontCare;
    }
}
