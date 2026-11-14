# An environment face renders only the depth ranges that hold surfaces (t2-zcr4, after take 18). The first rendered
# range starts the record anew. A face without any surface still renders its near range, which clears the record.
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
""",
"""        bool anew = true;
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            SidedRuns runs = draw[range];
            if (runs.Count == 0 && !(anew && range == DepthRanges.Near))
                continue;
            map.FaceSurface.DrawnBy(barriers: context.Barriers, attachments: record, anew: anew);
            anew = false;
            context.Barriers.Image(map.FaceDepth, ImageUse.DepthAttachment, discard: true);
            context.Barriers.Flush(vk, commandBuffer);
            BeginRange(context: context, range: range, record: record);
""")
