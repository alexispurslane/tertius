---
id: ter-o0a6
status: open
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

**2026-10-31T18:18:11Z**

2026-10-31, built: ScatterKernel(Taps, WidthScale) takes the scale as a required positional parameter, with no default, so every kernel names the unit of its offsets; SkinProfile.Kernel passes SkinProfile.WidthScale. ScatterImages.BlockOf lays the block out as 34 vec4: the 2n + 1 taps, zeros up to 33, then the scale in x of the 34th. In std140 the float kernelWidthScale lies at byte 528 after vec4 kernel[33], so the block holds 532 bytes, and the buffer holds 544 (whole vec4). Specialization constant 3 and SkinWidthScaleId are gone, SpecializationStorage.Count is 3, and ShaderConstants no longer names SkinProfile. The new test AKernelAtTheMeasuredWidthJudgesADepthStepByItsWholeOffset (a kernel with scale 1) failed before Pass read kernel.WidthScale (0.6 against 0.45) and passes after. The GPU tests cannot build a kernel of another scale, because ScatterImages makes its kernel from SkinProfile.Human with no test hook; the GPU tests that match the shader to SkinScatter.Pass at the skin's scale pass. All 486 PNG frames of scripts/skin-and-hair.json are byte-identical before and after.
