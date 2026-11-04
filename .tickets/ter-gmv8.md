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

2026-11-03 (hardening): Coverage of the changed lines (tests/Tertius.Rendering.Tests in Debug and Release, before the newest tests) gave 82 of 89 lines and 14 of 18 branches. The uncovered lines of DeviceAllocator.cs were 156 and 158 (the GpuOnly fallback and the refusal when no type fits), 278 (Free of a foreign allocation) and 299 to 302 (a failed mapping frees its memory). The tests of 9dd94a4ee cover each of them, and the mutants C04, C13 and C07 on those lines are killed. The partial branch at line 330 is unchanged code. A second Dispose finds empty lists, so its early return has no observable effect.

The tests added after the coverage run use AnvNonLlcTypes, the table of ANV on an Intel GPU without a shared last-level cache (Mesa 26.2.4, `i915/anv_device.c`). The new test AReadbackOnAnIntelGpuWithoutASharedCacheTakesTheCoherentTypeAndNotTheCachedOne runs on it, and the lock-in theory has an ANV case and a large Readback that stays live.

Mutants: /tmp/ter-gmv8-mutants.json, 22 rows, run with tools/mutate.py. The first run took every row (logs in /tmp/ter-gmv8-mutate). The second run took C04, C07, C08, C10, C15, C16, C17, C21, C22 and C23 (logs in /tmp/ter-gmv8-mutate2), and its rows replace the rows of the first run below. Between the runs, C04, C07 and C15 got new versions, because the analyzers refused the first versions (CS0162, IDE0060, IDE0059). C21 to C23 moved from GeometryStoreRegionTests, which never reads memory back, to SceneTests and ExposureReadbackTests. The row C20 (delete the SetName of a dedicated allocation) is dropped. The new ANV tests killed C08 and C10.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| C01 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:134 | GpuOnly keeps the block's address: usage == MemoryUsage.GpuOnly ? granted.WithoutAddress() : granted -> granted | killed | DeviceAllocatorTests.AGpuOnlyAllocationGetsNoPointerOnADriverWithOneMemoryTypeAlthoughItsMemoryIsMapped, DeviceAllocatorTests.AGpuOnlyResourceThatNoDeviceLocalTypeAllowsTakesTheFirstTypeThatItAllows | 4.8 |
| C02 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:134 | address hidden from the wrong usages: usage == MemoryUsage.GpuOnly -> usage != MemoryUsage.GpuOnly | killed | DeviceAllocatorTests.AnUploadAfterAGpuOnlyAllocationOnMoltenVkTakesTheHostVisibleTypeAndGetsAWritablePointer, DeviceAllocatorTests.AnUploadIntoTheBlockThatAFreedGpuOnlyAllocationLeftEmptyGetsAWritablePointer, DeviceAllocatorTests.AGpuOnlyAllocationGetsNoPointerOnADriverWithOneMemoryTypeAlthoughItsMemoryIsMapped | 5.1 |
| C03 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:43 | WithoutAddress keeps the address: mapped: null -> mapped: Mapped | killed | DeviceAllocatorTests.AGpuOnlyAllocationGetsNoPointerOnADriverWithOneMemoryTypeAlthoughItsMemoryIsMapped, DeviceAllocatorTests.AGpuOnlyResourceThatNoDeviceLocalTypeAllowsTakesTheFirstTypeThatItAllows | 4.8 |
| C04 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:155 | no GpuOnly fallback to any allowed type: usage == MemoryUsage.GpuOnly) -> usage == MemoryUsage.Readback) | killed | DeviceAllocatorTests.AGpuOnlyResourceThatNoDeviceLocalTypeAllowsTakesTheFirstTypeThatItAllows | 4.5 |
| C05 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:169 | a type with no preferred flag never wins: int bestScore = -1; -> int bestScore = 0; | killed | DeviceAllocatorTests.AnUploadAfterAGpuOnlyAllocationOnMoltenVkTakesTheHostVisibleTypeAndGetsAWritablePointer, DeviceAllocatorTests.DisposingTheAllocatorReleasesEveryBlockAndDedicatedAllocation, DeviceAllocatorTests.TheMappedStateOfEveryBlockFollowsItsTypeAfterAllocationsOfEveryUsageAndFrees(driver: "RADV discrete") | 4.7 |
| C06 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:173 | type bits inverted: (typeBits & (1u << i)) == 0 -> (typeBits & (1u << i)) != 0 | killed | DeviceAllocatorTests.AnUploadAfterAGpuOnlyAllocationOnMoltenVkTakesTheHostVisibleTypeAndGetsAWritablePointer, DeviceAllocatorTests.AnAllocationWhoseMappingFailsFreesItsMemoryBeforeItThrows, DeviceAllocatorTests.DisposingTheAllocatorReleasesEveryBlockAndDedicatedAllocation | 4.9 |
| C07 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:173 | type bits ignored: if ((typeBits & (1u << i)) == 0) -> if (typeBits == 0) | killed | DeviceAllocatorTests.AnUploadThatNoHostVisibleTypeAllowsIsRefused, DeviceAllocatorTests.AGpuOnlyResourceThatNoDeviceLocalTypeAllowsTakesTheFirstTypeThatItAllows | 4.5 |
| C08 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:176 | any required flag is enough: (flags & required) != required -> (flags & required) == 0 && required != 0 | killed | DeviceAllocatorTests.AReadbackOnAnIntelGpuWithoutASharedCacheTakesTheCoherentTypeAndNotTheCachedOne | 4.8 |
| C09 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:179 | a tie goes to the last type: score > bestScore -> score >= bestScore | killed | DeviceAllocatorTests.AnUploadAfterAGpuOnlyAllocationOnMoltenVkTakesTheHostVisibleTypeAndGetsAWritablePointer, DeviceAllocatorTests.AGpuOnlyAllocationOnADiscreteRadeonTakesTheDeviceLocalOnlyTypeAndStaysUnmapped, DeviceAllocatorTests.AGpuOnlyAllocationOnMoltenVkTakesTheDeviceLocalOnlyTypeAndStaysUnmapped | 10.3 |
| C10 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:208 | a dedicated allocation records type 0: type: type -> type: 0 | killed | DeviceAllocatorTests.TheMappedStateOfEveryBlockFollowsItsTypeAfterAllocationsOfEveryUsageAndFrees(driver: "RADV discrete"), DeviceAllocatorTests.TheMappedStateOfEveryBlockFollowsItsTypeAfterAllocationsOfEveryUsageAndFrees(driver: "MoltenVK") | 4.6 |
| C11 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:272 | a freed dedicated allocation leaks: memory.Free(single.Memory); -> (deleted) | killed | DeviceAllocatorTests.FreeingADedicatedAllocationReleasesItsMemoryAtOnce | 17.6 |
| C12 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:293 | maps exactly the wrong types: if (IsHostVisible(type)) -> if (!IsHostVisible(type)) | killed | DeviceAllocatorTests.AnUploadAfterAGpuOnlyAllocationOnMoltenVkTakesTheHostVisibleTypeAndGetsAWritablePointer, DeviceAllocatorTests.AnAllocationWhoseMappingFailsFreesItsMemoryBeforeItThrows, DeviceAllocatorTests.DisposingTheAllocatorReleasesEveryBlockAndDedicatedAllocation | 6.6 |
| C13 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:301 | a failed map leaks the memory: memory.Free(allocated); -> (deleted) | killed | DeviceAllocatorTests.AnAllocationWhoseMappingFailsFreesItsMemoryBeforeItThrows | 5.9 |
| C14 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:311 | host-visible read as host-cached: MemoryPropertyFlags.HostVisibleBit -> MemoryPropertyFlags.HostCachedBit | killed | DeviceAllocatorTests.AnUploadOnADiscreteRadeonTakesTheHostVisibleVramTypeThatIsNotCachedAndGetsAWritablePointer, DeviceAllocatorTests.AGpuOnlyResourceThatNoDeviceLocalTypeAllowsTakesTheFirstTypeThatItAllows | 5.9 |
| C15 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:319 | block invariant drops 'no other one has': block.Mapped != null == IsHostVisible(block.Type) -> block.Mapped != null \|\| !IsHostVisible(block.Type) | survived |  | 4.5 |
| C16 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:321 | dedicated invariant never fails: single.Mapped != null == IsHostVisible(single.Type) -> true | survived |  | 4.6 |
| C17 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:322 | count invariant never fails: liveAllocations >= dedicated.Count -> true | survived |  | 4.6 |
| C18 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:335 | Dispose leaks the blocks: memory.Free(block.Memory); -> (deleted) | killed | DeviceAllocatorTests.DisposingTheAllocatorReleasesEveryBlockAndDedicatedAllocation, DeviceAllocatorTests.TheMappedStateOfEveryBlockFollowsItsTypeAfterAllocationsOfEveryUsageAndFrees(driver: "RADV discrete"), DeviceAllocatorTests.TheMappedStateOfEveryBlockFollowsItsTypeAfterAllocationsOfEveryUsageAndFrees(driver: "MoltenVK") | 4.9 |
| C19 | src/Tertius.Rendering/Vulkan/DeviceAllocator.cs:337 | Dispose leaks the dedicated allocations: memory.Free(single.Memory); -> (deleted) | killed | DeviceAllocatorTests.DisposingTheAllocatorReleasesEveryBlockAndDedicatedAllocation | 4.8 |
| C21 | src/Tertius.Rendering/Vulkan/IDeviceMemory.cs:46 | real device allocates type 0: MemoryTypeIndex = type, -> MemoryTypeIndex = 0, | killed | SceneTests.ReferenceSceneDrawsEveryDepthRange, SceneTests.PlanetShadowDarkensAMoonInIt, SceneTests.AFarSphereCastsASoftEdgedShadowOnTheGroundNearTheCamera | 4.8 |
| C22 | src/Tertius.Rendering/Vulkan/IDeviceMemory.cs:71 | a failed real map goes unseen: .Check("vkMapMemory"); -> ; | survived |  | 6.7 |
| C23 | src/Tertius.Rendering/Vulkan/IDeviceMemory.cs:75 | real memory never freed: => device.Vk.FreeMemory(device.Device, memory, null); -> { } | killed | SceneTests.ReferenceSceneDrawsEveryDepthRange, SceneTests.PlanetShadowDarkensAMoonInIt, SceneTests.AFarSphereCastsASoftEdgedShadowOnTheGroundNearTheCamera | 6.8 |
22 rows: killed 18, timeout 0, survived 4, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

Triage:
- C15, C16, C17 (weaker invariant checks in AssertInvariants): judgment call. No sequence of calls on the allocator's interface makes a block or a dedicated allocation whose pointer disagrees with its type, or fewer live allocations than dedicated ones. So a weaker check cannot be told apart from the real one while the allocator is correct.
- C22 (drop .Check("vkMapMemory") in VulkanDeviceMemory.Map): judgment call. No test can make the real driver refuse vkMapMemory. The test double tests the failure path itself (AnAllocationWhoseMappingFailsFreesItsMemoryBeforeItThrows), and .Check is the one line that turns the driver's result into the exception that the path needs.
- C20 (dropped): judgment call. Without the name, the parameter is unused, which the analyzers refuse. A memory name shows only in validation messages and GPU debuggers, which no test reads.
