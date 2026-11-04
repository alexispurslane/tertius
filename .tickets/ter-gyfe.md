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
# On KosmicKrisp the stores of a varying share of the reflections' own-ray workgroups do not land


Found on 2026-11-03 by ter-cikj. `CaptureSinkTests.ABgraOutputHandsItsSinkThePixelsOfAnRgbaOutputInRgbaOrder` fails on KosmicKrisp (Mesa 26.2.4) because of it, in the Checked and the Release suite. The failure is not about the BGRA order: two renderers with the RGBA output differ too.

## What the runs show

- Each run renders one frame of the reference scene in a new offscreen renderer, 320 x 180, with SMAA and bloom off. On KosmicKrisp, the captures of 5 to 8 such renderers in one process differ from each other in 0 to 384 pixels, by up to 136/255. On MoltenVK 1.4.2 they are identical.
- With `NoReflections`, or with `TraceResolution.Full`, the captures are identical on KosmicKrisp too, in 7 of 7 pairs each (2026-11-04). So the half-resolution path differs.
- Every pixel that differs is on the own-ray list, which `ReadOwnRayPixels` reads back after the frame. The list holds the same 502 pixels in every renderer.
- The engine's synchronization is the spec's: `OwnRayDispatch.Trace` places a barrier from the upsample's storage writes to `DRAW_INDIRECT | COMPUTE_SHADER` with `INDIRECT_COMMAND_READ | SHADER_STORAGE_READ`. The validation layer's synchronization validation, which these tests turn on with its shader-access heuristic, reports no hazard. A barrier from all commands to all commands, with all memory writes to all reads and writes, added right before the indirect dispatch did not change the result.
- The GPU timer is not involved: with the timer off, the pixels still differ.

## The cut-down frame (2026-11-04)

The fault needs none of the optional passes. These runs used 8 renderers in one process for each setting, and read the HDR image with `ReadHdr` after one frame:

- With every option that removes a pass set to its off value, the frames still differ: `NoScreenSpaceLight`, `NoSubsurface`, `NoLocalShadows`, `NoEnvironmentMap`, `FixedExposure(0)`, `Deformation = false`, `CascadedLights = 0`, `NoBloom`, `AntiAliasing.None` and `Impostors = false`.
- The frames still differ with validation off, without a capture, and in the second frame of each renderer.
- `renderer.Graph.Skipped` can drop passes by name. With the options above and every pass skipped but "G-buffer", "depth pyramid", "radiance pyramid", "reflection trace" and "reflections", the stores of whole workgroups still go missing.

A temporary edit of `reflection_own_rays.comp` marks each pixel that an invocation reaches, after the invocation's trace:

```glsl
        ivec2 pixel = ownRayPixel(entry);
        vec3 scratchDelta;
        bool scratchHit = ownRayDelta(pixel, scratchDelta);
        imageStore(hdrImage, pixel, vec4(1000.0, float(gl_NumWorkGroups.x), scratchHit ? 1000.0 : 500.0, 1.0));
        continue;
```

where

- `ownRayDelta` = the own ray's trace. It returns false when the pixel starts no ray.
- `gl_NumWorkGroups.x` = the workgroup count of the dispatch, as the GPU read it from the list's header.
- A marked pixel is one whose red and blue are above 100.

| Setting | KosmicKrisp: marked pixels per renderer | MoltenVK |
|---|---|---|
| The cut-down frame | 502, 502, 0, 0, 502, 502, 502, 502 | 502 in every renderer |
| The same frame, with robust buffer and image access turned on | 502, 448, 0, 0, 192, 64, 502, 0 | not run |
| The options above, with no pass skipped | 128, 501, 501, 64, 256, 501, 64, 0 | 501 in every renderer |

Every marked pixel reads a workgroup count of 8 in the cut-down frame, on both drivers, also in a renderer where only 1 or 3 workgroups stored. So the dispatch reads the header correctly. The note of 2026-11-03, that the dispatch read the header's x before the upsample's atomic adds landed, was wrong.

A marker that skips the trace (the `imageStore` and `continue` alone) marked every listed pixel in every renderer on KosmicKrisp, in 11 settings: the options above with one pass skipped at a time, with none, and with all but the five. When the transparent pass runs, it covers 3 of the 502 pixels. So the fault needs the trace in the shader. The trace's one loop is `for (uint step = 0u; step < maxSteps && s < ray.end; step++)` in `reflection.glsl`, with an integer bound. Every invocation reaches the unconditional store, and robust access does not remove the fault, so no defined behavior of the shader explains a lost store. With Metal's shader validation (`MTL_SHADER_VALIDATION=1`), the list itself came back empty, so that tool does not help here.

## No standalone reproduction yet

A standalone program with the same commands (`vkCmdUpdateBuffer` of the header, a barrier, an append dispatch with atomic adds, the barrier above, `vkCmdDispatchIndirect`), with its buffers in one 64 MiB block at an offset, ran every workgroup in 60 submissions on KosmicKrisp. Its dispatch had no trace, which agrees with the marker without the trace. The next step is to give that program the own-ray shader with its trace and the inputs of the cut-down frame: the depth pyramid, the radiance pyramid and the G-buffer. Then report the reproduction to Mesa, which is an outward-facing step for the user to approve.

The test stays as it is.
