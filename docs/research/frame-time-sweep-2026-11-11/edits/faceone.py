# The surfaces of an environment face in one rendering: the three depth ranges share one begin and one end, and a
# range after the first clears the depth to its own boundary with vkCmdClearAttachments (t2-zcr4, after take 17).
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]
P = root + '/src/Tertius.Rendering/Passes/EnvironmentFacePass.cs'
common.sub(P,
"""        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            map.FaceSurface.DrawnBy(barriers: context.Barriers, attachments: record, anew: range == DepthRanges.Far);
            context.Barriers.Image(map.FaceDepth, ImageUse.DepthAttachment, discard: true);
            context.Barriers.Flush(vk, commandBuffer);
            BeginRange(context: context, range: range, record: record);
            SidedRuns runs = draw[range];
            if (runs.Count > 0)
            {
                GeometryPass.PushDraw(context: context, viewProjection: CubeFaces.ViewProjection(draw.Basis, range));
                GeometryPass.DrawRuns(context: context, runs: runs, solid: surfaces, cutout: cutoutSurfaces);
            }

            vk.CmdEndRendering(commandBuffer);
        }
    }
""",
"""        map.FaceSurface.DrawnBy(barriers: context.Barriers, attachments: record, anew: true);
        context.Barriers.Image(map.FaceDepth, ImageUse.DepthAttachment, discard: true);
        context.Barriers.Flush(vk, commandBuffer);
        BeginRange(context: context, range: DepthRanges.Far, record: record);
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            SidedRuns runs = draw[range];
            if (runs.Count == 0)
                continue;
            if (range != DepthRanges.Far)
                ClearDepth(context: context, range: range);
            GeometryPass.PushDraw(context: context, viewProjection: CubeFaces.ViewProjection(draw.Basis, range));
            GeometryPass.DrawRuns(context: context, runs: runs, solid: surfaces, cutout: cutoutSurfaces);
        }

        vk.CmdEndRendering(commandBuffer);
    }

    private void ClearDepth(FrameContext context, int range)
    {
        var attachment = new ClearAttachment
        {
            AspectMask = ImageAspectFlags.DepthBit,
            ClearValue = new(depthStencil: new ClearDepthStencilValue(DepthRanges.ClearDepth(range), 0)),
        };
        var rect = new ClearRect { Rect = new(new Offset2D(0, 0), FaceExtent), BaseArrayLayer = 0, LayerCount = 1 };
        context.Vk.CmdClearAttachments(
            commandBuffer: context.CommandBuffer,
            attachmentCount: 1,
            pAttachments: &attachment,
            rectCount: 1,
            pRects: &rect
        );
    }
""")
