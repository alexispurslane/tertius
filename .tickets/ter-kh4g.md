---
id: ter-kh4g
status: in_progress
deps: []
links: [ter-ukw6]
created: 2026-11-14T18:53:04Z
type: epic
priority: 1
assignee: Alexis Purslane
---
# Back-face depth layer: the screen walk learns each surface's real thickness

Alexis decided on 2026-11-14 to design the real mechanism for ter-ukw6 first: a second depth layer of back faces, so that each screen pixel knows where its object ends, in place of the walk's fixed ThicknessShare slab. The brief, the evidence, the code places, the rejected designs, the points to cover and the acceptance are in docs/handoffs/2026-11-14-renderer-lab.md, part 1. Steps: find the sources and their situations, measure the frame cost with a throwaway back-face pass on the Mac and the RTX 2000 Ada, write two designs with Alexis's questions, then the design doc in docs/plans/ and the tickets.


## Notes

**2026-11-14T19:10:52Z**

Head start from the stopped designer of 2026-11-14 (copied from /tmp/t7-3e22/ukw6-backface-notes.md, which does not survive a restart; these are notes, not decisions):


The coordinator stopped this work early. No source search, prototype, timing run or cloud job happened. Cloud spend: $0. The agent never made /tmp/ukw6 or a scratch worktree.

## What was read

- `tk show ter-ukw6`. The scene is rect-lamps, with the Sun on and clamps off. False sill hits bring about 74 % of the glow box's bright reflection light on the ceiling. These rays left through the end window past the sill's outer edge. The place is render row y 375, x 596-679. There the sill is a sub-pixel strip, seen almost edge-on.
- `src/Tertius.Rendering/Shaders/screen_walk.glsl`, `walkScreen`.
  - At level 0, the slab of a pixel runs from its center w back to w * backShare. Here backShare = 1 / (1 + ThicknessShare), and ThicknessShare = 0.1.
  - Above level 0, the slab runs from the cell's near bound to its far bound times backShare.
  - A level-0 hit needs `meetsSlab && hitsPixel`. `hitsPixel` already rejects a surface whose normal faces away from the ray. It also rejects the hair receiver's own card.
  - `entryShare` is 0 at the slab's front and 1 at its back.
- ARCHITECTURE.md, "The screen tracer".
  - Pyramid images: level 0 is R32F, the center w. The coarser levels are RG32F, the near and far bounds of the pixel planes' corners.
  - Device needs: `maxPerStageDescriptorStorageImages` of at least 20. The tracer sees 16 to 21 storage images.
  - Pass costs: the depth pyramid takes 0.27 ms on the deck. The reflection rays take 0.60 ms on the RTX 2000 Ada, and about 2.65 ms in the M1 Max deck trace.
- The evidence folder holds hits9/hits-true.txt (light sums for each hit class) and the probe patches. Only the head of hits-true.txt was read.

## A first geometric argument (not yet checked against the data)

Take a convex solid, such as a box sill, and the view column of one pixel. A point in that column can lie behind the front surface and outside the solid. Then it lies behind the solid's back face in that column. A ray past the sill's outer edge is outside the solid. So a back-face depth at the same jitter rejects that ray where the ray crosses the pixel near its center.

Two limits to check in a design:

- The sill is sub-pixel. The back-face depth comes from the pixel center only. The ray can cross the pixel elsewhere, where the column through the sill is thinner or empty. So some false hits can remain at the strip's edge pixels.
- Open surfaces have no back face of their own. Examples are single-sided cards, hair cards, glass and thin hull walls. Then the nearest back face belongs to a farther object, and the slab grows thicker than today's 10 %. A design can clamp it, for example to the smaller of the back-face slab and today's slab.

## Points that the design must still cover

- The sources and their situations: McGuire and Mara 2014 (a back-face buffer), depth peeling, and the fixed thickness of current engines.
- The frame cost of a depth-only pass with front faces culled, at the same jitter. Measure it on the Mac and on the RTX 2000 Ada, the prototype against main in one job.
- Whether the back layer needs its own pyramid of bounds for the coarse levels.
- Memory at 1280 x 720. One R32F layer takes about 3.7 MB.
- The extra texel read in each walk step.
- Moving objects and skinned meshes. The back pass must draw the same placements as the front pass.
- Linux portability. A depth-only pass needs no new device feature.
- The effect on contact shadows.

**2026-11-14T22:51:57Z**

Stage 1 of 2026-11-14 is done: sources, cost and two designs. The record is docs/research/back-face-depth-layer/README.md on the branch ter-kh4g-back-face-depth-layer, with sources.md (22 sources with quotes), the patch of the whole probe and two stills. The probe's pass is the commit bacd0680d, marked THROWAWAY. Stage 2 removes it.

Main numbers:
- Sources: no large engine (Lumen, HDRP, Godot, EEVEE, AMD SSSR) gives its screen walk a back layer. Two small sources draw back faces for a reflection walk (kode80 2015, an optional mode of Babylon.js), without a time.
- A pass that keeps the nearest back face fails: in orbit's cabin the camera stands inside the person's body mesh, 0.3 m from every pixel. The pass must keep the first back face behind the front surface.
- With that rule, 99.99 to 100 % of the surface pixels of rect-lamps, the cabin and the deck have a back face behind them. The real thickness is above the 10 % rule on 33 to 46 % of the pixels.
- Cost of the pass alone: +0.037 to +0.047 ms on the RTX 2000 Ada (main 7.1 to 9.4 ms, five runs per side, every run differs), and +0.03 to +0.05 ms on the M1 Max (main 14.8 to 20.5 ms, two rounds, about the size of the run-to-run spread).
- Cost of a second texel read at every step of the finest level: about 0.21 to 0.24 ms on the RTX card and 0.35 to 0.43 ms on the Mac (one round). The rays that walk on after a rejected hit cost 0.02 to 0.05 ms on the RTX card.
- Memory at 1280 x 720: 1.76 MiB (R16F side image) to 5.27 MiB (RG32F finest level plus a side target).
- Cloud spend: $0.26 by the job log, of which $0.09 for a stills job that timed out.

The two designs: (1) the back layer is the slab, stored in the depth pyramid, with no thickness constant in the walk. (2) the back layer only trims the 10 % slab, from a side image read at a candidate hit. Three questions for Alexis are in the record. Not done, by the brief: the design document, the build tickets and the real mechanism.

Not checked: a new probe run of the false sill hits, contact darkening in stills, Design 1's rule in a running walk, hair and other two-sided surfaces, dense geometry, and RADV, ANV and lavapipe.

**2026-11-14T23:05:06Z**

2026-11-14, the answers of Alexis to the three questions of stage 1. (1) The layer is the slab (Design 1): the real back face replaces the 10 % rule. Alexis chose it knowing that slabs grow on a third to a half of the pixels and that contact darkening can change in both directions. (2) A surface with no back face behind it is a sheet with no thickness. (3) Settled by the parent agent from answer 1: the layer lives in the depth pyramid, with the front depth and the back depth at every level. Stage 1b is narrower for it: the false sill hits under the 10 % rule and under the layer as the slab, stills of both rules at one fixed exposure to find a misbehavior with a name, the sheet beside the 10 % rule in a hair view with a check that a ray can hit a slab of zero thickness, and the cost of the pyramid store only. The side image is out. The design doc, the build tickets and the build wait for a later stage.

**2026-11-15T01:25:26Z**

Stage 1b of 2026-11-14 and 2026-11-15 is done: the evidence for the chosen model, the layer as the slab in the depth pyramid. Records: docs/research/back-face-depth-layer/README.md (section 'Stage 1b') and stage-1b/probe.md, cost.md, pictures.md. Main numbers. (1) False sill hits in rect-lamps: 74.34 % of the glow area's bright reflection light is false under the 10 % rule, and 0.016 % of that false light stays under the layer. Stage 1's estimate of 8 % does not hold. (2) Cost of the chosen model against main per frame at 1280 x 720: on the RTX 2000 Ada +0.031 ms on the deck, +0.055 ms in the cabin, -0.109 ms in rect-lamps and +0.446 ms in a crowd of 2.0 million skinned vertices. On the M1 Max +0.060 ms (inside the noise), +0.140, -0.141 and +1.014 ms. The store alone is free. The pass costs by vertex count. (3) Stills at a fixed exposure: rect-lamps becomes darker by about 64 of 255 on 1,972 of 8,040 tiles. In the cabin the recess between two thick pillars brightens by up to 44 of 255, and the fade over the first 10 % turns it darker again, so the fade rule on a thick slab is open (t1-g4p1). (4) The borrowed back face: a hair card that the pass does not draw takes the floor's back face, and 95 % of the layer's reflection hits in lamp-hall-16-hair then lie behind the ray's start (t1-yy2f). (5) A sheet with no thickness keeps 25 to 56 % of the real hits that the 10 % rule finds on it, and a sheet as deep as its own plane keeps 52 to 89 %. The cause is open (t1-i54g). Cloud: $1.24 of $3. Not done, by the brief: the design document, the build tickets and the real mechanism.
