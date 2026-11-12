---
id: te-r7nt
status: open
deps: []
links: [ter-2zga]
created: 2026-11-12T07:34:16Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, performance]
---
# The depth and light pyramids in fewer dispatches: build only if the user rejects the patched MoltenVK

The second half of ter-nstr (sweep row 4, addendum A6 proposal 7). On stock MoltenVK 1.4.2 each pyramid builds its levels in 7 dispatches behind barriers, 7 Metal encoders each, and fewer dispatches save an estimated 0.10 to 0.15 ms on the deck. With the sweep's patched MoltenVK (proposal 1), which keeps a compute encoder open across compute-only barriers, the patch already keeps all 13 barriers of the two pyramids in one encoder, so this saves about 0 ms. It waits for the user's decision on the patched MoltenVK, and it is worth building only if the user rejects the patch.

Design notes from ter-nstr (2026-11-12), not built:
- One block dispatch builds up to 5 levels: a workgroup of 16 x 16 invocations reads a 32 x 32 tile of the level below, writes 16 x 16, 8 x 8, 4 x 4, 2 x 2 and 1 x 1 cells, and keeps each level in shared memory with barrier() between levels. A level-k tile depends only on its own level-(k-1) tile, so no workgroup waits for another: no global atomic counter and nothing beyond core Vulkan. environment_downsample.comp already builds 3 levels per dispatch this way (ARCHITECTURE.md, the environment map's mips).
- Exact bits: each cell merges its four children in the down shader's order, (0,0), (1,0), (0,1), (1,1), and a child outside its mip's extent counts as the empty cell before it enters shared memory, exactly as childBounds and childLight test it. The levels image is padded (PyramidImages.ImageExtent), and the blocks cover the whole mip as the per-level dispatches do.
- The tail (PyramidTail) stays. The block's set can reuse DescriptorLayouts.PyramidTailLayout: a source texture and an array of storage mips with a push-constant level count, as the tail binds them.
- The depth pyramid: its finest dispatch reads only the G-buffer, so its discard barrier can join the first block's barrier, and the pass drops from 7 encoders to 2 (finest and first block, then the tail). The light pyramid's levels read its finest level, so it drops from 7 to 3.
- ter-y3k4 (a one-image depth pyramid for the walk) touches the same pass; build this after its decision or keep the block shader independent of the level-0 format.

