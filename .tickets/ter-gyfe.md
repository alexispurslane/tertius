---
id: ter-gyfe
status: open
deps: []
links: [ter-cikj]
created: 2026-11-04T02:25:08Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering, portability, upstream]
---
# On KosmicKrisp the reflections' own-ray dispatch runs a varying share of its workgroups


Found on 2026-11-03 by ter-cikj. `CaptureSinkTests.ABgraOutputHandsItsSinkThePixelsOfAnRgbaOutputInRgbaOrder` fails on KosmicKrisp (Mesa 26.2.4) because of it, in the Checked and the Release suite. The failure is not about the BGRA order: two renderers with the RGBA output differ too.

## What the runs show

- Each run renders one frame of the reference scene in a new offscreen renderer, 320 x 180, with SMAA and bloom off. On KosmicKrisp, the captures of 5 to 8 such renderers in one process differ from each other in 0 to 384 pixels, by up to 136/255. On MoltenVK 1.4.2 they are identical.
- With `NoReflections`, or with `TraceResolution.Full`, the captures are identical on KosmicKrisp too. So the half-resolution path differs.
- Every pixel that differs is on the own-ray list, which `ReadOwnRayPixels` reads back after the frame. The list holds the same 502 pixels in every renderer.
- A shader that marks every pixel that `reflection_own_rays.comp` reaches showed 0, 64, 192 or all 502 of the listed pixels marked, by renderer. So the indirect dispatch ran 0, 1, 3 or all 8 of its workgroups. It read the header's x while the upsample's atomic adds were still landing, or before them.
- The engine's synchronization is the spec's: `OwnRayDispatch.Trace` places a barrier from the upsample's storage writes to `DRAW_INDIRECT | COMPUTE_SHADER` with `INDIRECT_COMMAND_READ | SHADER_STORAGE_READ`. The validation layer's synchronization validation, which these tests turn on with its shader-access heuristic, reports no hazard. A barrier from all commands to all commands, with all memory writes to all reads and writes, added right before the indirect dispatch did not change the result.
- The GPU timer is not involved: with the timer off, the pixels still differ.

## No standalone reproduction yet

A standalone program with the same commands (`vkCmdUpdateBuffer` of the header, a barrier, an append dispatch with atomic adds, the barrier above, `vkCmdDispatchIndirect`), with its buffers in one 64 MiB block at an offset, ran every workgroup in 60 submissions on KosmicKrisp. So the trigger needs more of the frame than these commands. The next step is to cut the engine's frame down: drop passes before the reflections until the dispatch runs every workgroup again. Then report the reproduction to Mesa, which is an outward-facing step for the user to approve.

The test stays as it is.
