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
