using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The deform pass (docs/plans/skinning-and-morphs.md, "The pass"): one compute dispatch over every job of the frame
/// writes each job's deformed copy into the deform region, before every pass that draws a mesh, and each deformed vertex's
/// position into the kept positions of the frame's parity (docs/plans/temporal-renderer.md, "Kept deformed positions"). A
/// frame without a job records nothing.
/// </summary>
internal sealed unsafe class DeformPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly DeformStore deforms;
    private readonly KeptPositions kept;
    private readonly Pipeline pipeline;

    public DeformPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        DeformStore deforms,
        KeptPositions kept
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.deforms = deforms;
        this.kept = kept;
        pipeline = new ComputePipelineDesc
        {
            Name = "deform",
            Shader = "deform.comp",
            Layout = layouts.DeformLayout,
        }.Create(device, shaders);
    }

    public override string Name => "deformation";

    /// <summary>The dispatches that the last <see cref="Record"/> recorded: 1, or 0 for a frame without a job. A test hook.</summary>
    internal int Dispatches { get; private set; }

    /// <summary>The barrier batches that the last <see cref="Record"/> recorded: 2, or 0 for a frame without a job. A test hook.</summary>
    internal int Barriers { get; private set; }

    public override void Record(FrameContext context)
    {
        int flushesBefore = context.Barriers.FlushCount;
        RecordJobs(context);
        Barriers = context.Barriers.FlushCount - flushesBefore;
    }

    /// <summary>Records the dispatch of the frame's jobs between its two barrier batches, or nothing without a job.</summary>
    private void RecordJobs(FrameContext context)
    {
        ReadOnlySpan<DeformJob> jobs = context.Scene.Deforms;
        Dispatches = 0;
        if (jobs.IsEmpty)
            return;
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        GpuBuffer vertices = context.Geometry.VertexBuffer;
        // deform.comp writes a kept position for each vertex of a job's target, with no bound of its own.
        Contract.Requires(kept.Capacity >= context.Geometry.Region.Capacity);
        // A write after the vertex reads of every earlier pass, the frame before's included: the region serves every
        // frame in flight (docs/plans/skinning-and-morphs.md, decision 18).
        barriers.Buffer(vertices, BufferUse.ComputeWrite);
        // After the vertex reads of the frame before the last, the last one that read this parity's buffer. A frame with a
        // job has a region, which the kept positions hold.
        barriers.Buffer(
            kept.Written(context.History.Parity)
                ?? throw new UnreachableException("The kept positions hold the deform region of a frame with a job."),
            BufferUse.ComputeWrite
        );
        barriers.Buffer(deforms.Vertices.Buffer, BufferUse.ComputeRead);
        barriers.Buffer(deforms.Deltas.Buffer, BufferUse.ComputeRead);
        barriers.Flush(vk, commandBuffer);

        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipeline);
        DescriptorSet set = context.Frame.DeformSet;
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.DeformLayout,
            firstSet: 0,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        // Each invocation writes only its own vertex of the region, so one dispatch needs no order between its jobs.
        var push = new DeformPushConstants { JobCount = (uint)jobs.Length, Groups = context.Frame.DeformGroups };
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.DeformLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(DeformPushConstants),
            pValues: &push
        );
        (uint x, uint y) = ComputeLimits.Grid(push.Groups);
        vk.CmdDispatch(commandBuffer: commandBuffer, groupCountX: x, groupCountY: y, groupCountZ: 1);
        Dispatches = 1;

        // Before the first draw of the frame, which reads the copies as vertices.
        barriers.Buffer(vertices, BufferUse.VertexInput);
        barriers.Flush(vk, commandBuffer);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);

    /// <summary>The groups of one job of <paramref name="count"/> vertices, one invocation per vertex.</summary>
    internal static uint Groups(uint count) =>
        (count + MeshDeform.InvocationsPerGroup - 1) / MeshDeform.InvocationsPerGroup;

    /// <summary>
    /// The job of workgroup <paramref name="group"/> of the frame's dispatch: the last of <paramref name="records"/> whose
    /// first workgroup is at most <paramref name="group"/>. The C# copy of deform.comp's binary search.
    /// </summary>
    /// <param name="records">At least one record, in job order, so their first workgroups never fall.</param>
    internal static int JobOf(ReadOnlySpan<GpuDeformJob> records, uint group)
    {
        int low = 0,
            high = records.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (records[middle].FirstGroup <= group)
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }
}
