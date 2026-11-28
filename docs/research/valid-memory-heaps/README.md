# Vulkan allocation heap validity (#35)

Every new device-memory request is no larger than its selected heap. An undersized heap produces the existing out-of-device-memory result before the driver call. The allocator then tries the next compatible memory type in its existing preference order.

## Cause and allocation ownership

The sole approved #4 Linux job, `40d9f654-f5dc-4671-8a58-01c21712037f-u2`, stopped in baseline `19e17c5ba364392ca6bf84b00c11a7d709771ded`. It requested 268435456, 536870912 and 1069547520 bytes from heap 2, which reported 257949696 bytes. The device was NVIDIA RTX 2000 Ada Generation, driver `595.112.07`. The raw job evidence remains with [#4](https://github.com/alexispurslane/tertius/pull/32). Candidate diagnostics and matched timings never ran.

`GpuBuffer.Create` obtains buffer memory requirements. `GpuImage.BindMemory` obtains image memory requirements. Both call `DeviceAllocator.Allocate`. New blocks use 64 MiB, and resources larger than half a block use a dedicated allocation of `MemoryRequirements.Size` bytes. Both creation paths call the private `DeviceAllocator.AllocateMemory`. This calls `IDeviceMemory.Allocate`, implemented by `VulkanDeviceMemory.Allocate`, which contains the sole `Vk.AllocateMemory` call in `src`. Existing blocks reuse already allocated memory.

[The Vulkan rule](https://docs.vulkan.org/refpages/latest/refpages/source/vkAllocateMemory.html#VUID-vkAllocateMemory-pAllocateInfo-01713) requires allocation size to be less than or equal to the selected heap size. The source excerpt from Khronos Vulkan-Docs is preserved in the evidence archive.

The shared allocation method now begins with this code:

```csharp
        PhysicalDeviceMemoryProperties properties = memory.Types;
        uint heapIndex = properties.MemoryTypes[type].HeapIndex;
        if (size > properties.MemoryHeaps[(int)heapIndex].Size)
            throw new VulkanException("vkAllocateMemory", Result.ErrorOutOfDeviceMemory);
```

where

- `memory` is the allocator's `IDeviceMemory` provider. `Types` reports the physical device's memory types and heaps.
- `properties` is that reported table. `MemoryTypes` maps type indices to property flags and heap indices. `MemoryHeaps` reports each heap's size in bytes.
- `type` is the compatible memory type index selected by the existing preference order.
- `heapIndex` is the heap index that `MemoryTypes[type]` reports. Several types can name the same heap.
- `size` is the actual new device-memory request in bytes, including the full 64 MiB for a new block.
- `VulkanException` carries the failure result. `Result.ErrorOutOfDeviceMemory` lets the existing handler try the next compatible type, or reaches the caller after the final type.

Equality is valid. Type-bit restrictions and required property flags still apply. In particular, GPU-only resources use non-device-local types only when no allowed device-local type exists, as before. The check adds no free-memory estimate, budget cache or type-selection API. Mapping, cleanup and block reuse retain their existing owner and path.

## Behavior and hardening evidence

| Check | Result |
|---|---|
| Red, before production changes | 43 cases: 15 failed on an invalid request reaching the driver boundary, 28 passed; exit 2 |
| Green, minimal implementation | 43 passed, 0 failed, 0 skipped; exit 0 |
| Checked focused coverage | All 4 new executable lines covered; both new branch outcomes covered (2/2) |
| Scripted mutations | 9 valid mutants killed, 0 survived, 0 timeout, 0 no-coverage |
| Restoration | Both mutation runs verified source bytes, semantic index entries and HEAD, then rebuilt the original assembly |

The tests run the real allocator against `TableMemory`, the existing device boundary double. Explicit heap sizes and a call log make oversized driver calls fail immediately. Cases cover new blocks, dedicated allocations, all three observed sizes, exact equality, shared heaps, a heap index different from its type index, no fitting compatible heap, resource type bits, upload flags, GPU-only restrictions, readback preference, driver refusal, block reuse and live-allocation invariants. Existing mapping and cleanup cases also pass.

[The mutation table](mutations.md) records every final mutant. One initial guard-removal attempt failed IDE0059 because it left unused reads. Its corrected scripted replacement also removes those reads and is killed. The invalid attempt is retained separately and excluded from the nine valid mutants. No Core, Simulation or Physics production file changed, so no official Stryker run applies.

The focused coverage run measured these four new executable lines after the fix. They did not exist before the fix. There is no baseline coverage percentage for these new lines. No timing-based or random tests were added, so no flakiness rerun or soak claim is made. The normal commit hook supplies the separate module-wide Checked validation recorded in the PR handoff.

## Identical patch for both measurement inputs

[allocator.patch](allocator.patch) contains only the production allocator change, including its method comment. Its SHA-256 is `61c94080b16aacd672c3ebc233590eeac63dc19d4d067dbd0d0eb3b70f800813`.

| Quantity | Value |
|---|---|
| Allocator before, Git blob | `9e8fe919f53c65950b792559637aa006c120486a` |
| Allocator after, Git blob | `f7aae7bd20d48ea3adf0d565ed476a6d2ec54594` |
| Allocator before, SHA-256 | `0661bea627615d74a195a285352a7f4daa78f291d8133dbf740b7ca97a6f87ba` |
| Allocator after, SHA-256 | `92d0c7d673a1801e23b793cf705eea2bd401f99fa8f6558abee6e0cba5556070` |
| Separate test instrumentation patch, SHA-256 | `be2ef72a14d4e692111de41ee85ec5f0c8fabf802839ccf9d255693109d477b6` |

| Input | Pinned commit | Production-only fixed tree | Local regression |
|---|---|---|---|
| Baseline | `19e17c5ba364392ca6bf84b00c11a7d709771ded` | `31d82893378a43a06b8911f7842bfddc39a61ecb` | 43 passed, exit 0 |
| Candidate evidence head | `468dc19d0f5e247819f24bfaea297919439c7b4f` | `402d40ad2f0567b14e4e16056bbb110be86905a5` | 43 passed, exit 0 |

Both inputs have identical allocator, device-memory interface, original allocator tests, flake and lock blobs. Candidate evidence head `468dc19` has no production diff from the measured candidate `a7a78944450584ad1c2ed2d0f3d38c583d80725c`, whose production seed is `cf84f02bc5fabfedb22c2b17835ae0d194c4cb91`.

Each input was independently extracted from its own immutable `git archive` under the ticket's external evidence folder. Before patching, every extracted file and symlink matched its Git blob. `git apply --check` passed for the same production patch and the separate regression patch. Each full Rendering test project compiled in the common pinned Nix dev shell and ran only `DeviceAllocatorTests` in Checked. Exactly the allocator and allocator tests differed during the build. The build changed no tracked source. Reversing both patches restored every original source hash. These were plain archive directories with no `.git` directory, branch or agent checkout. The first archive manifest attempt encountered a directory symlink before patching or building. The script was corrected to hash the link target and verify every original Git blob before continuing.

[verify-pinned-inputs.py](verify-pinned-inputs.py) is the evidence script. [pinned-input-verification.json](pinned-input-verification.json) records archive hashes, commands, exits and restoration. [production-only-trees.json](production-only-trees.json) records independent temporary-index checks: each fixed production tree differs only in `DeviceAllocator.cs`, and each full production diff equals `allocator.patch` byte for byte. No commit or branch was made for these temporary trees.

Any later paid comparison needs new authorization and new pinned baseline and candidate commit IDs that contain this same production patch on their respective originals. The worker currently checks both commit IDs and `baseline_tree`, so all three pins must reflect the fixed inputs. Retain the original commit IDs, the standalone patch digest and the fixed allocator blob in measurement provenance. Add an allocator-source digest check on both inputs before and after diagnostics. The existing 68-file asset/toolchain manifest excludes the allocator and remains unchanged. The regression patch is test instrumentation, not a measurement production change. Exposure code, settings, scene scripts and cooked assets keep their respective original inputs. Replacing the baseline with candidate code would invalidate the comparison.

This evidence establishes patch applicability, local compilation and allocator behavior on both pinned sources. It makes no Linux runtime or performance claim. No paid job ran. The #4 checkout and its source archives were read only.

## Preserved artifacts

[evidence.tar.gz](evidence.tar.gz) holds raw red/green logs, coverage XML, mutation inputs/results/logs, exact base/fixed allocator source, both archive test logs, original source manifests and restoration evidence. [evidence-manifest.json](evidence-manifest.json) gives the archive and member SHA-256 hashes. [provenance.json](provenance.json) records this ticket's base and production hashes.

The immutable source archives and durable command checkpoints remain under `/Users/alexispurslane/Movies/Blue Danube/exposure-bloom-recovery/issue-35-evidence/`. The source archives are about 1 GiB each and are excluded from the repository evidence bundle.
