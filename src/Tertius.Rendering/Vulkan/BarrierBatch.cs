using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Collects synchronization2 barriers for the resources a pass is about to use and records them as one
/// vkCmdPipelineBarrier2. Each resource carries its last synchronized use (<see cref="ResourceState"/>), so a pass
/// only states what it needs next and the barrier comes from the difference.
/// </summary>
/// <remarks>
/// Rules: a write after anything, or any layout change, gets a full barrier from the previous stages and writes.
/// A read after a write gets a barrier that makes the write visible. A read after reads in the same layout needs
/// no barrier when its stages were already covered; otherwise it chains from the earlier read stages, which carries
/// the earlier write's visibility forward. Barriers in a command buffer also order against earlier submissions on
/// the same queue, so render targets shared by frames in flight are safe.
/// </remarks>
internal sealed unsafe class BarrierBatch
{
    private ImageMemoryBarrier2[] images = new ImageMemoryBarrier2[16];
    private BufferMemoryBarrier2[] buffers = new BufferMemoryBarrier2[8];
    private int imageCount;
    private int bufferCount;

    /// <summary>The number of vkCmdPipelineBarrier2 calls recorded. A test hook.</summary>
    public int FlushCount { get; private set; }

    /// <summary>
    /// Prepares every mip level and layer of <paramref name="image"/> for <paramref name="use"/>. With
    /// <paramref name="discard"/> the old contents may be thrown away.
    /// </summary>
    public void Image(GpuImage image, ImageUse use, bool discard = false)
    {
        ResourceState next = ResourceUses.Describe(use);
        ResourceState current = image.State;
        bool sameLayout = current.Layout == next.Layout && !discard;
        bool currentWrites = ResourceUses.Writes(current.Access);
        bool nextWrites = ResourceUses.Writes(next.Access);
        if (sameLayout && !currentWrites && !nextWrites)
        {
            if ((next.Stages & ~current.Stages) == 0)
                return;
            AddImage(image, current.Stages, AccessFlags2.None, next.Stages, next.Access, current.Layout, next.Layout, 0,
                image.MipLevels);
            image.State = new(current.Layout, current.Stages | next.Stages, current.Access | next.Access);
            return;
        }

        AddImage(image, current.Stages, current.Access & ResourceUses.WriteMask, next.Stages, next.Access,
            discard ? ImageLayout.Undefined : current.Layout, next.Layout, 0, image.MipLevels);
        image.State = next;
    }

    /// <summary>
    /// An explicit barrier on some mip levels of some layers (a <paramref name="layerCount"/> of 0 means every layer
    /// from <paramref name="baseLayer"/> on), for sequences the use model does not describe, such as mip generation.
    /// The caller sets <see cref="GpuImage.State"/> once the whole image is in one state again.
    /// </summary>
    public void ImageLevels(GpuImage image, uint baseMip, uint mipCount, ImageLayout oldLayout, ImageLayout newLayout,
        PipelineStageFlags2 srcStages, AccessFlags2 srcAccess, PipelineStageFlags2 dstStages, AccessFlags2 dstAccess,
        uint baseLayer = 0, uint layerCount = 0)
    {
        AddImage(image, srcStages, srcAccess, dstStages, dstAccess, oldLayout, newLayout, baseMip, mipCount, baseLayer,
            layerCount);
    }

    /// <summary>Prepares <paramref name="buffer"/> for <paramref name="use"/>.</summary>
    public void Buffer(GpuBuffer buffer, BufferUse use)
    {
        ResourceState next = ResourceUses.Describe(use);
        ResourceState current = buffer.State;
        bool currentWrites = ResourceUses.Writes(current.Access);
        bool nextWrites = ResourceUses.Writes(next.Access);
        if (current.Stages == PipelineStageFlags2.None && current.Access == AccessFlags2.None)
        {
            // Never used by the GPU: nothing to wait for.
            buffer.State = next;
            return;
        }

        if (!currentWrites && !nextWrites)
        {
            if ((next.Stages & ~current.Stages) == 0)
                return;
            AddBuffer(buffer, current.Stages, AccessFlags2.None, next.Stages, next.Access);
            buffer.State = new(ImageLayout.Undefined, current.Stages | next.Stages, current.Access | next.Access);
            return;
        }

        AddBuffer(buffer, current.Stages, current.Access & ResourceUses.WriteMask, next.Stages, next.Access);
        buffer.State = next;
    }

    /// <summary>Records every collected barrier.</summary>
    public void Flush(Vk vk, CommandBuffer commandBuffer)
    {
        if (imageCount == 0 && bufferCount == 0)
            return;
        fixed (ImageMemoryBarrier2* imageBarriers = images)
        fixed (BufferMemoryBarrier2* bufferBarriers = buffers)
        {
            var dependency = new DependencyInfo
            {
                SType = StructureType.DependencyInfo,
                ImageMemoryBarrierCount = (uint)imageCount,
                PImageMemoryBarriers = imageBarriers,
                BufferMemoryBarrierCount = (uint)bufferCount,
                PBufferMemoryBarriers = bufferBarriers
            };
            vk.CmdPipelineBarrier2(commandBuffer, &dependency);
        }

        imageCount = 0;
        bufferCount = 0;
        FlushCount++;
    }

    private void AddImage(GpuImage image, PipelineStageFlags2 srcStages, AccessFlags2 srcAccess,
        PipelineStageFlags2 dstStages,
        AccessFlags2 dstAccess, ImageLayout oldLayout, ImageLayout newLayout, uint baseMip, uint mipCount,
        uint baseLayer = 0, uint layerCount = 0)
    {
        if (imageCount == images.Length)
            Array.Resize(ref images, images.Length * 2);
        images[imageCount++] = new()
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = srcStages,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStages,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image.Handle,
            // A layer count of 0 means every layer from the base layer on.
            SubresourceRange = new(image.Aspect, baseMip, mipCount, baseLayer,
                layerCount == 0 ? image.ArrayLayers - baseLayer : layerCount)
        };
    }

    private void AddBuffer(GpuBuffer buffer, PipelineStageFlags2 srcStages, AccessFlags2 srcAccess,
        PipelineStageFlags2 dstStages, AccessFlags2 dstAccess)
    {
        if (bufferCount == buffers.Length)
            Array.Resize(ref buffers, buffers.Length * 2);
        buffers[bufferCount++] = new()
        {
            SType = StructureType.BufferMemoryBarrier2,
            SrcStageMask = srcStages,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStages,
            DstAccessMask = dstAccess,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = buffer.Handle,
            Offset = 0,
            Size = Vk.WholeSize
        };
    }
}
