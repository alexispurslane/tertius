using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Tertius.Rendering;

/// <summary>
/// A swapchain on the window surface. It is recreated when the size changes or the surface reports it out of
/// date or suboptimal. Each image has its own render-finished semaphore, so a semaphore is only reused after its
/// image comes back from the presentation engine.
/// </summary>
internal sealed unsafe class SwapchainTarget : IOutputTarget
{
    private readonly VulkanDevice device;
    private readonly KhrSwapchain api;
    private readonly KhrSurface surfaceApi;
    private readonly bool vsync;
    private readonly List<Semaphore> renderFinished = [];
    private readonly SurfaceFormatKHR surfaceFormat;
    private SwapchainKHR swapchain;
    private GpuImage[] images = [];
    private Extent2D requested;
    private uint imageIndex;
    private bool stale;

    public SwapchainTarget(VulkanDevice device, Extent2D extent, bool vsync)
    {
        this.device = device;
        api = device.SwapchainApi ?? throw new InvalidOperationException("The device has no swapchain support.");
        surfaceApi = device.SurfaceApi ?? throw new InvalidOperationException("The device has no surface.");
        this.vsync = vsync;
        requested = extent;
        surfaceFormat = ChooseFormat();
        Create();
    }

    public Format Format => surfaceFormat.Format;
    public Extent2D Extent { get; private set; }

    public bool EncodeSrgbInShader =>
        Format is Format.B8G8R8A8Unorm or Format.R8G8B8A8Unorm or Format.A2B10G10R10UnormPack32;

    public bool BgraOrder => Format is Format.B8G8R8A8Srgb or Format.B8G8R8A8Unorm;

    /// <summary>The number of times the swapchain was created, including the first. A test hook.</summary>
    public int Generation { get; private set; }

    /// <summary>The swapchain images can be captured, so <see cref="VulkanRenderer.CaptureNextFrame"/> works.</summary>
    public bool CanCapture { get; private set; }

    public GpuImage? Acquire(FrameResources frame)
    {
        if (stale)
            Recreate();
        if (swapchain.Handle == 0)
            return null;
        uint index;
        Result result = api.AcquireNextImage(device: device.Device, swapchain: swapchain, timeout: ulong.MaxValue,
            semaphore: frame.ImageAvailable, fence: default,
            pImageIndex: &index);
        if (result == Result.ErrorOutOfDateKhr)
        {
            // The semaphore was not signaled, so it can be used again next frame.
            stale = true;
            return null;
        }

        if (result == Result.SuboptimalKhr)
            stale = true;
        else
            result.Check("vkAcquireNextImageKHR");
        imageIndex = index;
        GpuImage image = images[index];
        // The previous contents do not matter. The barrier out of this state waits for the acquire semaphore,
        // which the submit waits on at the color attachment output stage.
        image.State = new(ImageLayout.Undefined, PipelineStageFlags2.ColorAttachmentOutputBit, AccessFlags2.None);
        return image;
    }

    public void FinishRecording(BarrierBatch barriers, CommandBuffer commandBuffer)
    {
        barriers.Image(images[imageIndex], ImageUse.Present);
        barriers.Flush(device.Vk, commandBuffer);
    }

    public void SubmitAndPresent(FrameResources frame)
    {
        Semaphore finished = renderFinished[(int)imageIndex];
        QueueSubmission.Submit(device: device, frame: frame, wait: frame.ImageAvailable,
            waitStages: PipelineStageFlags2.ColorAttachmentOutputBit,
            signal: finished);
        SwapchainKHR target = swapchain;
        uint index = imageIndex;
        var present = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &finished,
            SwapchainCount = 1,
            PSwapchains = &target,
            PImageIndices = &index
        };
        Result result = api.QueuePresent(device.Queue, &present);
        if (result is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr)
            stale = true;
        else
            result.Check("vkQueuePresentKHR");
    }

    public void Resize(Extent2D extent)
    {
        requested = extent;
        Recreate();
    }

    private void Recreate()
    {
        device.WaitIdle();
        Create();
    }

    private void Create()
    {
        stale = false;
        surfaceApi.GetPhysicalDeviceSurfaceCapabilities(device.PhysicalDevice, device.Surface,
                out SurfaceCapabilitiesKHR capabilities)
            .Check("vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        Extent2D extent = capabilities.CurrentExtent.Width != uint.MaxValue
            ? capabilities.CurrentExtent
            : new(
                Math.Clamp(requested.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Math.Clamp(requested.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height));

        SwapchainKHR old = swapchain;
        DestroyViews();
        if (extent.Width == 0 || extent.Height == 0)
        {
            // Minimized: no swapchain until the window has an area again.
            if (old.Handle != 0)
                api.DestroySwapchain(device.Device, old, null);
            swapchain = default;
            Extent = extent;
            stale = true;
            return;
        }

        uint imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0)
            imageCount = Math.Min(imageCount, capabilities.MaxImageCount);
        var usage = ImageUsageFlags.ColorAttachmentBit;
        // Captures copy out of the swapchain image.
        if ((capabilities.SupportedUsageFlags & ImageUsageFlags.TransferSrcBit) != 0)
            usage |= ImageUsageFlags.TransferSrcBit;
        CanCapture = CanCaptureImages(usage, surfaceFormat.Format);
        CompositeAlphaFlagsKHR alpha = (capabilities.SupportedCompositeAlpha & CompositeAlphaFlagsKHR.OpaqueBitKhr) != 0
            ? CompositeAlphaFlagsKHR.OpaqueBitKhr
            : (CompositeAlphaFlagsKHR)((uint)capabilities.SupportedCompositeAlpha &
                                       (uint)-(int)capabilities.SupportedCompositeAlpha);

        var info = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = device.Surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = usage,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = alpha,
            PresentMode = ChoosePresentMode(),
            Clipped = true,
            OldSwapchain = old
        };
        api.CreateSwapchain(device: device.Device, pCreateInfo: &info, pAllocator: null,
            pSwapchain: out SwapchainKHR created).Check("vkCreateSwapchainKHR");
        if (old.Handle != 0)
            api.DestroySwapchain(device.Device, old, null);
        swapchain = created;
        Extent = extent;
        Generation++;

        uint count = 0;
        api.GetSwapchainImages(device: device.Device, swapchain: swapchain, pSwapchainImageCount: &count,
            pSwapchainImages: null).Check("vkGetSwapchainImagesKHR");
        var handles = new Image[count];
        fixed (Image* p = handles)
        {
            api.GetSwapchainImages(device: device.Device, swapchain: swapchain, pSwapchainImageCount: &count,
                pSwapchainImages: p).Check("vkGetSwapchainImagesKHR");
        }

        images = new GpuImage[count];
        for (int i = 0; i < count; i++)
            images[i] = GpuImage.Wrap(device: device, image: handles[i], format: surfaceFormat.Format, extent: extent,
                name: $"swapchain image {i}");
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        while (renderFinished.Count < count)
        {
            device.Vk.CreateSemaphore(device: device.Device, pCreateInfo: &semaphoreInfo, pAllocator: null,
                    pSemaphore: out Semaphore semaphore)
                .Check("vkCreateSemaphore");
            device.SetName(ObjectType.Semaphore, semaphore.Handle, $"render finished {renderFinished.Count}");
            renderFinished.Add(semaphore);
        }
    }

    /// <summary>
    /// Whether swapchain images of <paramref name="usage"/> and <paramref name="format"/> can be captured. The capture
    /// pass copies them, so they must allow transfer reads. A capture hands its sink 4 bytes per pixel, red, green and
    /// blue with sRGB encoding, so the format must be one of the four 8-bit formats whose bytes are that, in RGBA or
    /// BGRA order. A surface that offers none of them (a 10-bit or a float format) cannot be captured.
    /// </summary>
    internal static bool CanCaptureImages(ImageUsageFlags usage, Format format) =>
        (usage & ImageUsageFlags.TransferSrcBit) != 0
        && format is Format.B8G8R8A8Srgb or Format.R8G8B8A8Srgb or Format.B8G8R8A8Unorm or Format.R8G8B8A8Unorm;

    private SurfaceFormatKHR ChooseFormat()
    {
        uint count = 0;
        surfaceApi.GetPhysicalDeviceSurfaceFormats(physicalDevice: device.PhysicalDevice, surface: device.Surface,
                pSurfaceFormatCount: &count, pSurfaceFormats: null)
            .Check("vkGetPhysicalDeviceSurfaceFormatsKHR");
        var formats = new SurfaceFormatKHR[count];
        fixed (SurfaceFormatKHR* p = formats)
        {
            surfaceApi.GetPhysicalDeviceSurfaceFormats(physicalDevice: device.PhysicalDevice, surface: device.Surface,
                    pSurfaceFormatCount: &count, pSurfaceFormats: p)
                .Check("vkGetPhysicalDeviceSurfaceFormatsKHR");
        }

        if (count == 0)
            throw new NotSupportedException("The surface reports no formats.");
        // An sRGB format lets the hardware apply the transfer curve; UNORM falls back to the shader.
        foreach (Format preferred in (Format[])
                 [Format.B8G8R8A8Srgb, Format.R8G8B8A8Srgb, Format.B8G8R8A8Unorm, Format.R8G8B8A8Unorm])
        foreach (SurfaceFormatKHR format in formats)
            if (format.Format == preferred && format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return format;

        return formats[0];
    }

    private PresentModeKHR ChoosePresentMode()
    {
        if (vsync)
            return PresentModeKHR.FifoKhr;
        uint count = 0;
        surfaceApi.GetPhysicalDeviceSurfacePresentModes(physicalDevice: device.PhysicalDevice, surface: device.Surface,
                pPresentModeCount: &count, pPresentModes: null)
            .Check("vkGetPhysicalDeviceSurfacePresentModesKHR");
        var modes = new PresentModeKHR[count];
        fixed (PresentModeKHR* p = modes)
        {
            surfaceApi.GetPhysicalDeviceSurfacePresentModes(physicalDevice: device.PhysicalDevice,
                    surface: device.Surface, pPresentModeCount: &count, pPresentModes: p)
                .Check("vkGetPhysicalDeviceSurfacePresentModesKHR");
        }

        if (modes.Contains(PresentModeKHR.MailboxKhr))
            return PresentModeKHR.MailboxKhr;
        if (modes.Contains(PresentModeKHR.ImmediateKhr))
            return PresentModeKHR.ImmediateKhr;
        return PresentModeKHR.FifoKhr;
    }

    private void DestroyViews()
    {
        foreach (GpuImage image in images)
            image.Dispose();
        images = [];
    }

    public void Dispose()
    {
        DestroyViews();
        if (swapchain.Handle != 0)
            api.DestroySwapchain(device.Device, swapchain, null);
        swapchain = default;
        foreach (Semaphore semaphore in renderFinished)
            device.Vk.DestroySemaphore(device.Device, semaphore, null);
        renderFinished.Clear();
    }
}
