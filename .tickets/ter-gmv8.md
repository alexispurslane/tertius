---
id: ter-gmv8
status: open
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

2026-11-03: Fixed on branch ter-gmv8-mapped-blocks. `DeviceAllocator` now maps a block or a dedicated allocation exactly when its memory type has HOST_VISIBLE, which it reads from the type's property flags (`IsHostVisible`). The usage of the allocation that made the block no longer matters, and the allocator stores no flag of its own. `AssertInvariants` checks that every block and dedicated allocation of a host-visible type has a pointer, and that no other one has. On a host-visible type, a GpuOnly allocation gets a pointer that nothing reads. The cost is CPU address space of 64 MiB for each block and one `vkMapMemory` for each block.

The test seam is `IDeviceMemory`: the table of memory types, and the calls that allocate, map, name and free one `VkDeviceMemory`. `VulkanDeviceMemory` is a real device's. The allocator could not take a table without a real device, and a fake table on the real MoltenVK device would map memory of a type that is not host-visible. So `DeviceAllocatorTests` runs the allocator on a test double, which hands out host memory and throws on each `vkMapMemory` that the spec forbids. `FindMemoryType` moved from `VulkanDevice` into the allocator, its one caller, and reads the table through the seam.

The tests use KosmicKrisp's table of one type (DEVICE_LOCAL, HOST_VISIBLE, HOST_COHERENT, HOST_CACHED) and MoltenVK's table of three. Four tests on the one-type table were red for the right reason before the fix: an Upload or a Readback after a GpuOnly allocation, and an Upload into a block that a freed GpuOnly allocation left empty, got a null pointer, and no block was mapped. The two MoltenVK tests passed before and after.

The demo runs on KosmicKrisp (Mesa 26.2.4) to the end of `scripts/lamp-hall-16.json` with exit code 0. Its GPU timings of four passes are impossible, which ter-wwu7 records.

The full Checked suite passed on MoltenVK (10,153 passed, 15 skipped). The full Release suite failed one test, the known flaky GpuTimingTests test of ter-abun, which fails the same way on the base commit.
