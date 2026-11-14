---
id: ter-kh4g
status: open
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
