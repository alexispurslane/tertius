# The main view's G-buffer pass renders only the depth ranges that hold something to draw: opaque runs or impostors.
# The first rendered range clears the G-buffer. A frame without anything still renders its near range, which clears
# the G-buffer. A spike after take 18.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]
P = root + '/src/Tertius.Rendering/Passes/GeometryPass.cs'
common.sub(P,
"""        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            bool first = range == DepthRanges.Far;
            BeginRange(context: context, range: range, colors: colors);
""",
"""        bool first = true;
        bool bound = false;
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            bool empty = scene.Opaque[range].Count == 0 && scene.Impostors[range].Count == 0;
            if (empty && !(first && range == DepthRanges.Near))
                continue;
            BeginRange(context: context, range: range, colors: colors, first: first);
            first = false;
""")
common.sub(P,
"""            if (first)
            {
                context.BindSceneSets();
                context.SetFullViewport();
                BindGeometry(context);
            }
""",
"""            if (!bound)
            {
                context.BindSceneSets();
                context.SetFullViewport();
                BindGeometry(context);
                bound = true;
            }
""")
common.sub(P,
"""    private static void BeginRange(FrameContext context, int range, RenderingAttachmentInfo* colors)
""",
"""    private static void BeginRange(FrameContext context, int range, RenderingAttachmentInfo* colors, bool first)
""")
common.sub(P,
"""        GpuImage[] gbuffer = targets.GBuffer;
        bool first = range == DepthRanges.Far;
""",
"""        GpuImage[] gbuffer = targets.GBuffer;
""")
