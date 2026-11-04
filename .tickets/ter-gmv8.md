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

2026-11-03 (review fixes): The review found five low findings, and all are resolved on the branch.

- The claim that RADV and ANV report one memory type on integrated GPUs was wrong. The pinned Mesa 26.2.4 source shows it: lavapipe reports one type (`lvp_device.c`), RADV puts a type that is only device-local first on every GPU (`radv_physical_device.c`), and ANV does the same except on Atom GPUs without a shared last-level cache under i915 (`i915/anv_device.c`). ARCHITECTURE.md, "GPU memory", now states the exact condition, and the comments point there instead of repeating it.
- `DeviceAllocatorTests` now also runs RADV's discrete-GPU table of seven types, with the buffer type bits that RADV gives (no 32-bit types). Its upload type, 3, is device-local, host-visible and coherent, but not cached. With `IsHostVisible` reading HOST_CACHED instead of HOST_VISIBLE, the new upload test failed with a null pointer, and it passes on the real code.
- The settled behavior stays: a `GpuOnly` allocation of a host-visible type gets a pointer. ARCHITECTURE.md now records its cost: `GpuBuffer.Write` on a `GpuOnly` buffer succeeds on KosmicKrisp and lavapipe, but throws on MoltenVK, RADV and most ANV devices.
- ter-wwu7 now lists the 18 passes that read zero on KosmicKrisp, with the MoltenVK comparison.

2026-11-03 (orchestrator): A `GpuOnly` allocation now gets no address on any driver. `Allocate` hides the block's address from it with `MemoryAllocation.WithoutAddress`, so the block keeps its one rule (mapped exactly when its type is host-visible), and a CPU access through `GpuBuffer.Write` or `AsSpan` on a `GpuOnly` buffer throws on every driver. This settles review finding 1 by the project rule that the game must not depend on behavior that only some drivers give. The new test `AGpuOnlyAllocationGetsNoPointerOnADriverWithOneMemoryTypeAlthoughItsMemoryIsMapped` was red first (a pointer of 491014586368 where null was expected). ARCHITECTURE.md, "GPU memory", now states the rule instead of the driver-dependent cost.
