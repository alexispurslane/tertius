---
id: ter-o0a6
status: in_progress
deps: []
links: []
created: 2026-10-31T15:23:29Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-n7lw
---
# The scatter kernel carries the unit of its offsets for the slope rule

Review finding 3 of ter-t2w6 (2026-10-31). The kernel that the scatter pass runs is the authority for its geometry (ScatterKernel.Reach, kernel[2n].a), but the slope rule reads the unit of the same offsets from the global SkinProfile.WidthScale: in SkinScatter.Pass directly, and in scatterAlong through the specialization constant skinWidthScale (constant_id 3), which ShaderConstants.Specialization sets on every pipeline. Nothing ties the kernel in the buffer to the scale in the constant. Leakage: three SkinScatterTests that build their own kernels divide by SkinProfile.WidthScale to place their depths, and ShaderConstants knows a constant of the skin profile. ter-t2w6 kept the constant and documented the rule on SkinScatter.Pass (the review's second fix). The other design: SkinProfile.Kernel sets a width scale on ScatterKernel, ScatterImages writes it after the taps in the kernel's uniform block, and Pass and scatterAlong read it there; constant_id 3 and the fourth specialization entry go. The rendered image does not change. Cost: a change of the kernel buffer's layout. The user decides whether it is worth it.

## Design

Two designs: (1) keep the specialization constant, documented (as built in ter-t2w6); (2) the kernel carries its width scale in its uniform block. Ask the user before building (2).


## Notes

**2026-10-31T15:41:33Z**

2026-10-31: the user chose design 2, the kernel carries its width scale: SkinProfile.Kernel sets the scale on ScatterKernel, ScatterImages writes it after the taps in the kernel's uniform block, SkinScatter.Pass and scatterAlong read it there, and constant_id 3 with the fourth specialization entry go. The image must not change. Reason: one path for the 4, so a kernel of another width cannot meet the slope rule of the wrong one, and ShaderConstants no longer knows the skin. Waits for a free builder slot.

**2026-10-31T18:11:55Z**

2026-10-31: launched through the workflow MCP (ultracode-ticket-completion, version 869b0b07-3413-4b06-8707-a88a91bf6e91), run 984db3fb-f000-48ba-a589-9b4bd6e17315, branch ter-o0a6-kernel-width-scale from 99275c90. An earlier start, run 2f4c8a6d, had a mistyped base commit; it was cancelled before it made a thread, a branch or a worktree.

**2026-10-31T18:18:11Z**

2026-10-31, built: ScatterKernel(Taps, WidthScale) takes the scale as a required positional parameter, with no default, so every kernel names the unit of its offsets; SkinProfile.Kernel passes SkinProfile.WidthScale. ScatterImages.BlockOf lays the block out as 34 vec4: the 2n + 1 taps, zeros up to 33, then the scale in x of the 34th. In std140 the float kernelWidthScale lies at byte 528 after vec4 kernel[33], so the block holds 532 bytes, and the buffer holds 544 (whole vec4). Specialization constant 3 and SkinWidthScaleId are gone, SpecializationStorage.Count is 3, and ShaderConstants no longer names SkinProfile. The new test AKernelAtTheMeasuredWidthJudgesADepthStepByItsWholeOffset (a kernel with scale 1) failed before Pass read kernel.WidthScale (0.6 against 0.45) and passes after. The GPU tests cannot build a kernel of another scale, because ScatterImages makes its kernel from SkinProfile.Human with no test hook; the GPU tests that match the shader to SkinScatter.Pass at the skin's scale pass. All 486 PNG frames of scripts/skin-and-hair.json are byte-identical before and after.

**2026-10-31T18:38:35Z**

2026-10-31, review fixes: the kernel's uniform block has a C# mirror, GpuScatterKernel in Scene/GpuTypes.cs, as every other GPU block with more than one member has: an InlineArray ScatterTapArray of 33 Vector4 for the taps, then float WidthScale. ScatterImages.BlockOf fills it, the buffer takes Unsafe.SizeOf<GpuScatterKernel>() = 532 bytes (before: 544, a whole extra vec4), and Upload writes it with MemoryMarshal.Write. KernelVectors and the index arithmetic go. BatcherTests.TheScatterKernelMatchesTheShadersKernelBlock pins the float at byte 528 and the size at 532. The code comments name the mirror instead of the byte numbers. ARCHITECTURE.md and docs/plans/skin-and-hair.md now say 33 vec4 tap slots and a float, 532 bytes, where they said 33 vec4 with a 34th, or 544 bytes. The WidthScale param doc is cut to 20 words. All 486 frames of scripts/skin-and-hair.json stay byte-identical to the capture before the ticket.

**2026-10-31T19:39:20Z**

2026-10-31, integration: run 984db3fb ended ready for integration (build fcc1b120, review with 4 findings, fix c474ddf9 and a5a8d038). Hand mutations: Pass dividing by SkinProfile.WidthScale again was killed by the builder's new test; the block writing a scale of 1 was killed by the GPU tests that match the shader to SkinScatter.Pass; SkinProfile.Kernel building its kernel with a scale of 1 first survived, because no test pinned the human kernel's scale to the skin's, and a96e4f5e adds TheHumanKernelCountsItsOffsetsInTheSkinsOwnWidth, which kills it. The shader dividing by the constant 4.0 survives and is equivalent: every kernel that the renderer uploads is SkinProfile.Human at scale 4. Coverage of the changed lines: 12 of 12 lines, 2 of 2 branches.
