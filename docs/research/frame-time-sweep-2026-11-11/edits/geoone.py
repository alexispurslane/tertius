# The main view's opaque surfaces in one rendering: the three depth ranges share one begin and one end, and a range
# after the first clears the depth to its own boundary with vkCmdClearAttachments. A spike after take 17 (t2-zcr4).
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]
P = root + '/src/Tertius.Rendering/Passes/GeometryPass.cs'
common.sub(P,
"""        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            bool first = range == DepthRanges.Far;
            BeginRange(context: context, range: range, colors: colors);
            // Sets and dynamic state stay bound across the ranges, and the runs bind their own pipelines, as a range
            // drawer may.
            if (first)
            {
                context.BindSceneSets();
                context.SetFullViewport();
                BindGeometry(context);
            }

            SidedRuns runs = scene.Opaque[range];
""",
"""        BeginRange(context: context, range: DepthRanges.Far, colors: colors);
        context.BindSceneSets();
        context.SetFullViewport();
        BindGeometry(context);
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            if (range != DepthRanges.Far)
            {
                var attachment = new ClearAttachment
                {
                    AspectMask = ImageAspectFlags.DepthBit,
                    ClearValue = new(depthStencil: new ClearDepthStencilValue(DepthRanges.ClearDepth(range), 0)),
                };
                var rect = new ClearRect { Rect = new(new Offset2D(0, 0), targets.Extent), BaseArrayLayer = 0, LayerCount = 1 };
                vk.CmdClearAttachments(
                    commandBuffer: commandBuffer,
                    attachmentCount: 1,
                    pAttachments: &attachment,
                    rectCount: 1,
                    pRects: &rect
                );
            }

            SidedRuns runs = scene.Opaque[range];
""")
common.sub(P,
"""            foreach (IRangeDrawer drawer in RangeDrawers)
                drawer.DrawRange(context, range);
            vk.CmdEndRendering(commandBuffer);
        }
    }
""",
"""            foreach (IRangeDrawer drawer in RangeDrawers)
                drawer.DrawRange(context, range);
        }

        vk.CmdEndRendering(commandBuffer);
    }
""")
