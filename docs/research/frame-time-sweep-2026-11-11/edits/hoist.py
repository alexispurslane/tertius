# Timing proxy for ter-n8bk: the two own-ray lists' header resets move from the ray plan to the frame's start, where
# they share the barrier flush and the blit encoder of the exposure's readback copy. The plan then follows the light
# pyramid with no transfer command between them.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]
common.sub(root + '/src/Tertius.Rendering/Passes/ScreenTracePass.cs',
    "        foreach (GpuBuffer list in images.OwnRays)\n            OwnRayDispatch.Clear(context, list);\n        ScreenTraceKernel.DeclareSurfaces(context);\n",
    "        ScreenTraceKernel.DeclareSurfaces(context);\n")
common.sub(root + '/src/Tertius.Rendering/Passes/OwnRayDispatch.cs',
    "    /// <summary>\n    /// Traces the own rays listed in",
    "    public static void ClearOnly(FrameContext context, GpuBuffer list)\n    {\n        fixed (uint* empty = OwnRayList.EmptyHeader)\n"
    "            context.Vk.CmdUpdateBuffer(\n                commandBuffer: context.CommandBuffer,\n                dstBuffer: list.Handle,\n                dstOffset: 0,\n"
    "                dataSize: OwnRayList.HeaderBytes,\n                pData: empty\n            );\n    }\n\n"
    "    /// <summary>\n    /// Traces the own rays listed in")
common.sub(root + '/src/Tertius.Rendering/VulkanRenderer.cs',
    "        Exposure.RecordReadback(commandBuffer: commandBuffer, barriers: barriers, slot: frame.Index);\n        Graph.Record(context);\n",
    "        if (screenTrace is { } hoistedDeclare)\n            foreach (GpuBuffer list in hoistedDeclare.OwnRays)\n                barriers.Buffer(list, BufferUse.TransferDestination);\n"
    "        Exposure.RecordReadback(commandBuffer: commandBuffer, barriers: barriers, slot: frame.Index);\n"
    "        if (screenTrace is { } hoisted)\n            foreach (GpuBuffer list in hoisted.OwnRays)\n                OwnRayDispatch.ClearOnly(context, list);\n"
    "        Graph.Record(context);\n")
