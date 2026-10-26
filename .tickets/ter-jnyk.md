---
id: ter-jnyk
status: open
deps: []
links: []
created: 2026-10-26T18:17:52Z
type: bug
priority: 4
assignee: Alexis Purslane
parent: ter-kge6
tags: [assets, blender]
---
# A Metal bake can abort Blender in Cycles' kernel compile

Found by the review of ter-1qce on 2026-10-26. One of three builds of the astronaut with the default device (Metal) aborted about 15 s in, at the start of the bake, when Cycles creates the Metal device: blender.log ends with "Terminating app due to uncaught exception 'NSInvalidArgumentException', reason: '*** -[NSURL initFileURLWithPath:]: nil string parameter'", thrown in ccl::MetalKernelPipeline::compile from ccl::ShaderCache::compile_thread_func, and the build tool reports exit code 3 with no build.json. The same build passed twice right after, and the crew member passed twice. A probe render on Metal with the same BLENDER_USER_RESOURCES setting passed. The cause is not known: Cycles keeps its Metal kernel archives in ~/.cache/cycles/kernels, which every Blender on the machine shares, so a race with another Blender is one guess. The importer bakes on the CPU (--device cpu), so imports never meet it; agents who build with the default device can. After the crash, the output folder holds blender.log and textures/ but no build.json, so the next build into the same folder stops with exit code 2 until the folder is deleted. Find the cause (a debug build of Cycles, or CYCLES_METAL_DISABLE_BINARY_ARCHIVES=1 as a test), or let the build tool retry once on this abort.

