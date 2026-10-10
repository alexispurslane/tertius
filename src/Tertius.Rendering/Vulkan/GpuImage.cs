using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// A 2D image, or a 2D array image, with one view over all its mip levels and layers (a 2D array view when it has
/// more than one layer). Swapchain images are wrapped without owning memory.
/// </summary>
internal sealed unsafe class GpuImage : IDisposable
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator? allocator;
    private readonly MemoryAllocation memory;
    private readonly bool ownsImage;
    private bool disposed;

    private GpuImage(VulkanDevice device, DeviceAllocator? allocator, MemoryAllocation memory, bool ownsImage, Image image,
        ImageView view, Format format, Extent2D extent, uint mipLevels, uint arrayLayers, ImageAspectFlags aspect, string name)
    {
        this.device = device;
        this.allocator = allocator;
        this.memory = memory;
        this.ownsImage = ownsImage;
        Handle = image;
        View = view;
        Format = format;
        Extent = extent;
        MipLevels = mipLevels;
        ArrayLayers = arrayLayers;
        Aspect = aspect;
        Name = name;
        State = ResourceState.Undefined;
    }

    public Image Handle { get; }
    public ImageView View { get; }
    public Format Format { get; }
    public Extent2D Extent { get; }
    public uint MipLevels { get; }
    public uint ArrayLayers { get; }
    public ImageAspectFlags Aspect { get; }
    public string Name { get; }
    /// <summary>The layout and last synchronized use of every mip level and layer. See <see cref="BarrierBatch"/>.</summary>
    public ResourceState State;

    /// <summary>
    /// Creates an image with its own memory. Its view is a 2D array view when it has more than one layer or when
    /// <paramref name="arrayView"/> is set, so shaders that declare an array can read a single-layer image too.
    /// </summary>
    public static GpuImage Create(VulkanDevice device, DeviceAllocator allocator, Extent2D extent, Format format,
        ImageUsageFlags usage, uint mipLevels, string name, uint arrayLayers = 1, bool arrayView = false)
    {
        if (extent.Width == 0 || extent.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(extent), "Images must be at least 1x1.");
        ArgumentOutOfRangeException.ThrowIfZero(arrayLayers);
        ImageAspectFlags aspect = IsDepth(format) ? ImageAspectFlags.DepthBit : ImageAspectFlags.ColorBit;
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(extent.Width, extent.Height, 1),
            MipLevels = mipLevels,
            ArrayLayers = arrayLayers,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        device.Vk.CreateImage(device.Device, &info, null, out Image image).Check("vkCreateImage");
        MemoryAllocation memory = default;
        bool allocated = false;
        try
        {
            device.Vk.GetImageMemoryRequirements(device.Device, image, out MemoryRequirements requirements);
            memory = allocator.Allocate(requirements, MemoryUsage.GpuOnly, linear: false, name);
            allocated = true;
            device.Vk.BindImageMemory(device.Device, image, memory.Memory, memory.Offset).Check("vkBindImageMemory");
            ImageViewType viewType = arrayLayers > 1 || arrayView ? ImageViewType.Type2DArray : ImageViewType.Type2D;
            ImageView view = CreateView(device, image, format, aspect, viewType, 0, mipLevels, 0, arrayLayers);
            device.SetName(ObjectType.Image, image.Handle, name);
            device.SetName(ObjectType.ImageView, view.Handle, name);
            return new GpuImage(device, allocator, memory, true, image, view, format, extent, mipLevels, arrayLayers, aspect, name);
        }
        catch
        {
            if (allocated)
                allocator.Free(memory);
            device.Vk.DestroyImage(device.Device, image, null);
            throw;
        }
    }

    /// <summary>Wraps an image owned by someone else, such as a swapchain image. Only the view is destroyed on dispose.</summary>
    public static GpuImage Wrap(VulkanDevice device, Image image, Format format, Extent2D extent, string name)
    {
        ImageView view = CreateView(device, image, format, ImageAspectFlags.ColorBit, ImageViewType.Type2D, 0, 1, 0, 1);
        device.SetName(ObjectType.ImageView, view.Handle, name);
        return new GpuImage(device, null, default, false, image, view, format, extent, 1, 1, ImageAspectFlags.ColorBit, name);
    }

    /// <summary>
    /// A 2D view of one layer and mip level, for example to render into one layer of an array. The caller destroys it
    /// before the image.
    /// </summary>
    public ImageView CreateLayerView(uint layer, uint mipLevel = 0)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(layer, ArrayLayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mipLevel, MipLevels);
        ImageView view = CreateView(device, Handle, Format, Aspect, ImageViewType.Type2D, mipLevel, 1, layer, 1);
        device.SetName(ObjectType.ImageView, view.Handle, $"{Name} layer {layer}");
        return view;
    }

    public static bool IsDepth(Format format) =>
        format is Format.D32Sfloat or Format.D16Unorm or Format.D24UnormS8Uint or Format.D32SfloatS8Uint or Format.X8D24UnormPack32;

    private static ImageView CreateView(VulkanDevice device, Image image, Format format, ImageAspectFlags aspect, ImageViewType type,
        uint baseMip, uint mipLevels, uint baseLayer, uint layers)
    {
        var info = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = type,
            Format = format,
            SubresourceRange = new ImageSubresourceRange(aspect, baseMip, mipLevels, baseLayer, layers),
        };
        device.Vk.CreateImageView(device.Device, &info, null, out ImageView view).Check("vkCreateImageView");
        return view;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        device.Vk.DestroyImageView(device.Device, View, null);
        if (ownsImage)
        {
            device.Vk.DestroyImage(device.Device, Handle, null);
            allocator!.Free(memory);
        }
    }
}
