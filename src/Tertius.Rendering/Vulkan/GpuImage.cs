using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// A 2D image, a 2D array image or a cube image, with one view over all its mip levels and layers (a 2D array view when
/// it has more than one layer, a cube view for a cube). Swapchain images are wrapped without owning memory.
/// </summary>
internal sealed unsafe class GpuImage : IDisposable
{
    private readonly VulkanDevice device;

    // The allocator of the image's memory, or null for an image that the GpuImage does not own, such as a swapchain image.
    private readonly DeviceAllocator? allocator;
    private readonly MemoryAllocation memory;
    private bool disposed;

    /// <summary>The layout and last synchronized use of every mip level and layer. See <see cref="BarrierBatch"/>.</summary>
    public ResourceState State;

    private GpuImage(
        VulkanDevice device,
        DeviceAllocator? allocator,
        MemoryAllocation memory,
        Image image,
        ImageView view,
        Format format,
        Extent2D extent,
        uint mipLevels,
        uint arrayLayers,
        ImageAspectFlags aspect,
        string name
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.memory = memory;
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

    /// <summary>
    /// Creates an image with its own memory. Its view is a 2D array view when it has more than one layer or when
    /// <paramref name="arrayView"/> is set, so shaders that declare an array can read a single-layer image too. With
    /// <paramref name="cube"/>, the image is cube-compatible with six square layers, and its view is a cube.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="cube"/> is set for an image that is not six square layers.</exception>
    public static GpuImage Create(
        VulkanDevice device,
        DeviceAllocator allocator,
        Extent2D extent,
        Format format,
        ImageUsageFlags usage,
        uint mipLevels,
        string name,
        uint arrayLayers = 1,
        bool arrayView = false,
        bool cube = false
    )
    {
        if (extent.Width == 0 || extent.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(extent), "Images must be at least 1x1.");
        ArgumentOutOfRangeException.ThrowIfZero(arrayLayers);
        bool cubeShaped = arrayLayers == 6 && extent.Width == extent.Height;
        if (cube && !cubeShaped)
            throw new ArgumentException(
                $"A cube has six square layers, not {arrayLayers} of {extent.Width}×{extent.Height}."
            );
        ImageAspectFlags aspect = IsDepth(format) ? ImageAspectFlags.DepthBit : ImageAspectFlags.ColorBit;
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            Flags = cube ? ImageCreateFlags.CreateCubeCompatibleBit : 0,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new(extent.Width, extent.Height, 1),
            MipLevels = mipLevels,
            ArrayLayers = arrayLayers,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        device
            .Vk.CreateImage(device: device.Device, pCreateInfo: &info, pAllocator: null, pImage: out Image image)
            .Check("vkCreateImage");
        MemoryAllocation memory = default;
        bool allocated = false;
        try
        {
            device.Vk.GetImageMemoryRequirements(device.Device, image, out MemoryRequirements requirements);
            memory = allocator.Allocate(
                requirements: requirements,
                usage: MemoryUsage.GpuOnly,
                linear: false,
                name: name
            );
            allocated = true;
            device
                .Vk.BindImageMemory(
                    device: device.Device,
                    image: image,
                    memory: memory.Memory,
                    memoryOffset: memory.Offset
                )
                .Check("vkBindImageMemory");
            ImageViewType viewType = ViewTypeOf(cube: cube, arrayed: arrayLayers > 1 || arrayView);
            ImageView view = CreateView(
                device: device,
                image: image,
                format: format,
                aspect: aspect,
                type: viewType,
                baseMip: 0,
                mipLevels: mipLevels,
                baseLayer: 0,
                layers: arrayLayers
            );
            device.SetName(ObjectType.Image, image.Handle, name);
            device.SetName(ObjectType.ImageView, view.Handle, name);
            return new(
                device: device,
                allocator: allocator,
                memory: memory,
                image: image,
                view: view,
                format: format,
                extent: extent,
                mipLevels: mipLevels,
                arrayLayers: arrayLayers,
                aspect: aspect,
                name: name
            );
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
        ImageView view = CreateView(
            device: device,
            image: image,
            format: format,
            aspect: ImageAspectFlags.ColorBit,
            type: ImageViewType.Type2D,
            baseMip: 0,
            mipLevels: 1,
            baseLayer: 0,
            layers: 1
        );
        device.SetName(ObjectType.ImageView, view.Handle, name);
        return new(
            device: device,
            allocator: null,
            memory: default,
            image: image,
            view: view,
            format: format,
            extent: extent,
            mipLevels: 1,
            arrayLayers: 1,
            aspect: ImageAspectFlags.ColorBit,
            name: name
        );
    }

    /// <summary>
    /// A 2D view of one layer and mip level, for example to render into one layer of an array. The caller destroys it
    /// before the image.
    /// </summary>
    public ImageView CreateLayerView(uint layer, uint mipLevel = 0)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(layer, ArrayLayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mipLevel, MipLevels);
        ImageView view = CreateView(
            device: device,
            image: Handle,
            format: Format,
            aspect: Aspect,
            type: ImageViewType.Type2D,
            baseMip: mipLevel,
            mipLevels: 1,
            baseLayer: layer,
            layers: 1
        );
        device.SetName(ObjectType.ImageView, view.Handle, $"{Name} layer {layer}");
        return view;
    }

    /// <summary>
    /// A 2D array view of every layer at one mip level, for example to write one level of a cube as a storage image. The
    /// caller destroys it before the image.
    /// </summary>
    public ImageView CreateLevelView(uint mipLevel)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mipLevel, MipLevels);
        ImageView view = CreateView(
            device: device,
            image: Handle,
            format: Format,
            aspect: Aspect,
            type: ImageViewType.Type2DArray,
            baseMip: mipLevel,
            mipLevels: 1,
            baseLayer: 0,
            layers: ArrayLayers
        );
        device.SetName(ObjectType.ImageView, view.Handle, $"{Name} level {mipLevel}");
        return view;
    }

    /// <summary>The type of an image's own view: a cube, a 2D array, or a plain 2D view.</summary>
    private static ImageViewType ViewTypeOf(bool cube, bool arrayed)
    {
        if (cube)
            return ImageViewType.TypeCube;
        return arrayed ? ImageViewType.Type2DArray : ImageViewType.Type2D;
    }

    public static bool IsDepth(Format format) =>
        format
            is Format.D32Sfloat
                or Format.D16Unorm
                or Format.D24UnormS8Uint
                or Format.D32SfloatS8Uint
                or Format.X8D24UnormPack32;

    private static ImageView CreateView(
        VulkanDevice device,
        Image image,
        Format format,
        ImageAspectFlags aspect,
        ImageViewType type,
        uint baseMip,
        uint mipLevels,
        uint baseLayer,
        uint layers
    )
    {
        var info = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = type,
            Format = format,
            SubresourceRange = new(
                aspectMask: aspect,
                baseMipLevel: baseMip,
                levelCount: mipLevels,
                baseArrayLayer: baseLayer,
                layerCount: layers
            ),
        };
        device
            .Vk.CreateImageView(device: device.Device, pCreateInfo: &info, pAllocator: null, pView: out ImageView view)
            .Check("vkCreateImageView");
        return view;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        device.Vk.DestroyImageView(device.Device, View, null);
        if (allocator is not null)
        {
            device.Vk.DestroyImage(device.Device, Handle, null);
            allocator.Free(memory);
        }
    }
}
