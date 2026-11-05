using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Tertius.Rendering;

/// <summary>
/// Where the frame ends: an offscreen image or a swapchain, written by the tone map.
/// </summary>
internal interface IOutputTarget : IDisposable
{
    Format Format { get; }
    Extent2D Extent { get; }

    /// <summary>The format is UNORM, so the pass that writes the output must apply the sRGB curve itself.</summary>
    bool EncodeSrgbInShader { get; }

    /// <summary>Pixels are stored as BGRA, so a capture must swap red and blue.</summary>
    bool BgraOrder { get; }

    /// <summary>The images allow transfer reads, so the capture pass can copy them.</summary>
    bool CanCapture { get; }

    /// <summary>The image for this frame, or null when the frame must be skipped (out of date or minimized).</summary>
    GpuImage? Acquire(FrameResources frame);

    /// <summary>Records the output's last transition before the command buffer ends.</summary>
    void FinishRecording(BarrierBatch barriers, CommandBuffer commandBuffer);

    /// <summary>Submits the frame's command buffer, signaling its fence, and presents if there is anything to present to.</summary>
    void SubmitAndPresent(FrameResources frame);

    /// <summary>Changes the size. The device must be idle.</summary>
    void Resize(Extent2D extent);
}

internal static unsafe class QueueSubmission
{
    /// <summary>Two submissions in one vkQueueSubmit2. Only the output commands wait for the acquired image.</summary>
    public static void Submit(
        VulkanDevice device,
        FrameResources frame,
        Semaphore wait,
        PipelineStageFlags2 waitStages,
        Semaphore signal
    )
    {
        var commandBuffer = new CommandBufferSubmitInfo
        {
            SType = StructureType.CommandBufferSubmitInfo,
            CommandBuffer = frame.OutputCommandBuffer,
        };
        var waitInfo = new SemaphoreSubmitInfo
        {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = wait,
            StageMask = waitStages,
        };
        // The signal waits for everything, including the final layout transition to PRESENT_SRC.
        var signalInfo = new SemaphoreSubmitInfo
        {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = signal,
            StageMask = PipelineStageFlags2.AllCommandsBit,
        };
        var submit = new SubmitInfo2
        {
            SType = StructureType.SubmitInfo2,
            WaitSemaphoreInfoCount = wait.Handle != 0 ? 1u : 0u,
            PWaitSemaphoreInfos = &waitInfo,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = &commandBuffer,
            SignalSemaphoreInfoCount = signal.Handle != 0 ? 1u : 0u,
            PSignalSemaphoreInfos = &signalInfo,
        };
        var firstCommand = new CommandBufferSubmitInfo
        {
            SType = StructureType.CommandBufferSubmitInfo,
            CommandBuffer = frame.CommandBuffer,
        };
        SubmitInfo2* submissions = stackalloc SubmitInfo2[2];
        submissions[0] = new SubmitInfo2
        {
            SType = StructureType.SubmitInfo2,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = &firstCommand,
        };
        submissions[1] = submit;
        device
            .Vk.QueueSubmit2(queue: device.Queue, submitCount: 2, pSubmits: submissions, fence: frame.Fence)
            .Check("vkQueueSubmit2");
    }
}
