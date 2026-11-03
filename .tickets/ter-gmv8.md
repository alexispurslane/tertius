---
id: ter-gmv8
status: in_progress
deps: []
links: []
created: 2026-11-03T23:31:32Z
type: bug
priority: 1
assignee: Alexis Purslane
tags: [rendering, portability]
---
# The allocator puts an upload into an unmapped block when a driver has one memory type

Found on 2026-11-03 by running the demo on KosmicKrisp (Mesa 26.2.4), the Vulkan driver that Khronos now recommends on Apple Silicon. The 16-lamp hall crashed at the first texture upload: NullReferenceException in Memmove at TextureStore.Add (TextureStore.cs:157), because the staging slice's mapped pointer was null.

The cause: DeviceAllocator.Allocate maps memory when usage != GpuOnly, but AllocateInBlock reuses any block of the same memory type and linearity (DeviceAllocator.cs, block.Type == type && block.Linear == linear), whatever the usage that created the block. KosmicKrisp reports one memory type, DEVICE_LOCAL | HOST_VISIBLE | HOST_COHERENT | HOST_CACHED. So a GpuOnly allocation creates an unmapped block of type 0, and a later Upload or Readback allocation of type 0 lands in it with a null pointer. MoltenVK reports a separate DEVICE_LOCAL-only type 0, which hides the bug. lavapipe, and integrated GPUs on RADV and ANV, also report a single host-visible device-local type, so the Linux run (ter-kzj3, ter-cgif) would hit it too.

The class of bug: a block's mapped state is a second store beside its memory type's HOST_VISIBLE flag, set by whichever usage came first. Fix the class, not the instance: derive the mapping from the memory type, so that every block of a host-visible type is mapped when it is allocated, and no block can disagree with its type. A test with a memory-type table of one type (and of MoltenVK's three types) makes sure that an Upload after a GpuOnly allocation gets a mapped pointer.


## Notes

**2026-11-03T23:32:33Z**

2026-11-03: launched as workflow run 81e4d289-17d5-48f0-9c6c-82d39806a1b2, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (Opus high builder, Opus xhigh reviewer), branch ter-gmv8-mapped-blocks from 116a0e50a.
