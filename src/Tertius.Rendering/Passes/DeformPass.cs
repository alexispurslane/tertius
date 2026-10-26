using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The deform pass (docs/plans/skinning-and-morphs.md, "The pass"): one compute dispatch per job of the frame writes the
/// job's deformed copy into the deform region, before every pass that draws a mesh. A frame without a job records
/// nothing.
/// </summary>
internal sealed unsafe class DeformPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly DeformStore deforms;
    private readonly Pipeline pipeline;

    public DeformPass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, DeformStore deforms)
    {
        this.device = device;
        this.layouts = layouts;
        this.deforms = deforms;
        pipeline = new ComputePipelineDesc
        {
            Name = "deform",
            Shader = "deform.comp",
            Layout = layouts.DeformLayout,
        }.Create(device, shaders);
    }

    public override string Name => "deformation";

    public override void Record(FrameContext context)
    {
        ReadOnlySpan<DeformJob> jobs = context.Scene.Deforms;
        if (jobs.IsEmpty)
            return;
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        GpuBuffer vertices = context.Geometry.VertexBuffer;
        // A write after the vertex reads of every earlier pass, the frame before's included: the region serves every
        // frame in flight (docs/plans/skinning-and-morphs.md, decision 18).
        barriers.Buffer(vertices, BufferUse.ComputeWrite);
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
        ReadOnlySpan<MeshInfo> meshes = context.Geometry.Meshes;
        // In job order. Each invocation writes only its own vertex, so the order changes no bits.
        for (int i = 0; i < jobs.Length; i++)
        {
            // The copies never overlap, but every dispatch writes through the one window of the region, so the
            // synchronization validation sees two writes to one range. A write after write orders each dispatch after
            // the one before.
            if (i > 0)
            {
                barriers.Buffer(vertices, BufferUse.ComputeWrite);
                barriers.Flush(vk, commandBuffer);
            }

            DeformPushConstants push = PushConstants(jobs[i], meshes);
            vk.CmdPushConstants(
                commandBuffer: commandBuffer,
                layout: layouts.DeformLayout,
                stageFlags: ShaderStageFlags.ComputeBit,
                offset: 0,
                size: (uint)sizeof(DeformPushConstants),
                pValues: &push
            );
            vk.CmdDispatch(
                commandBuffer: commandBuffer,
                groupCountX: Groups(push.Count),
                groupCountY: 1,
                groupCountZ: 1
            );
        }

        // Before the first draw of the frame, which reads the copies as vertices.
        barriers.Buffer(vertices, BufferUse.VertexInput);
        barriers.Flush(vk, commandBuffer);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);

    /// <summary>The groups of one job of <paramref name="count"/> vertices, one invocation per vertex.</summary>
    internal static uint Groups(uint count) =>
        (count + MeshDeform.InvocationsPerGroup - 1) / MeshDeform.InvocationsPerGroup;

    /// <summary>The push constants of <paramref name="job"/>, whose static mesh is one of <paramref name="meshes"/>.</summary>
    internal static DeformPushConstants PushConstants(DeformJob job, ReadOnlySpan<MeshInfo> meshes)
    {
        MeshDeform deform =
            meshes[job.Mesh].Deform ?? throw new UnreachableException("Only a deforming mesh has a job.");
        return new()
        {
            Source = (uint)deform.Source,
            Deltas = (uint)deform.Deltas,
            Target = (uint)job.Target,
            Count = (uint)deform.VertexCount,
            Palette = (uint)job.Pose,
            Weights = (uint)(job.Pose + 3 * deform.PaletteJoints),
        };
    }
}
