# Screen-space artifacts: correct reflections and screen-space light before more optimization

Epic: ter-b2da.

## Goal

The reflections and the screen-space light give a correct image on demo-exit and on their review scenes. This comes before the rendering performance epic (ter-pq5y) cuts any more cost. A correct image has no artifact that comes from a fault of the method's implementation. Examples of such faults are a ray that stops on the wrong level of the depth pyramid, or noise that the denoise misses. A known limit of screen-space methods is not a fault: nothing off the screen or behind the nearest surface can reflect or occlude. ARCHITECTURE.md records each such limit, with the scene that shows it.

## Why now

The user decided on 2026-11-01 to pause the performance work and fix these artifacts first. Correct behavior comes before more optimization, because the fixes change the starting point of the optimization. A fix can change a pass's cost, so the shares of ter-3g1g and its candidate default changes (fewer occlusion steps, no own rays at outlines) wait for this epic.

## Decisions

1. Diagnose before any fix. Each effect gets one diagnosis ticket. The ticket finds every artifact in a gallery of captures, and attributes each to its stage with probes and A/B switches. A fix is designed only after its cause is known. (User, 2026-11-01)
2. Decision 8 of [rendering-performance.md](rendering-performance.md) holds: no temporal accumulation, no TAA, no temporal upscaling. Each frame's image comes from that frame alone. (User, 2026-10-25)
3. A fix that changes a default, or that costs GPU time, is a design decision. It comes to the user as two designs with captures, as CLAUDE.md says. A fix of a plain bug, where the code does not do what its design says, needs no such step.
4. The running performance workflows finish the stage that they are in, and nothing new starts in ter-pq5y until this epic closes. Finished performance work still merges: each such change keeps the image under the share tickets' image rule, so it does not move the starting point.

## The artifacts known on 2026-11-01

| Artifact | Effect | Where | Evidence |
|---|---|---|---|
| The reflection of the greenhouse breaks into blocky, angular fragments | Reflections | demo-exit, deck film | `captures/artifacts-2026-11-01/deck-reflection-blocks.png` (git ignores `captures/`) |
| Horizontal stair steps, hard cut lines, and a green line that floats above the reflection | Reflections | demo-exit, floor film | `captures/artifacts-2026-11-01/floor-reflection-steps.png`, ter-eqtb |
| A band of dark brown specks along each crease between the floor and a wall | Screen-space light | demo-exit, deck film | `captures/artifacts-2026-11-01/deck-crease-specks.png`, ter-k96l |

The base code before ter-pq5y's share tickets shows each of them, so no performance change caused them.

## Hypotheses to test, not established

- The blocky fragments: a ray of the half-resolution trace stops on a coarse level of the depth pyramid. Or a hit fails or passes the thickness test wrongly.
- The stair steps and cut lines: rays at a grazing angle run out of their 256 steps, leave the screen, or step over the depth quantization of the far floor.
- The floating green line: a hit that lands on the wrong surface, for example an edge of the green box at a depth that the trace accepts.
- The crease specks: 3 slices with 3 steps per side give too few samples where the environment map's ambient light is bright. The spatial denoise does not remove that noise. The bounce light can add the cabin's dark color.

## Order of the work

1. The screen-space light's diagnosis, ter-xgra. It starts in the next free builder slot.
2. The reflections' diagnosis, ter-y8z0. It starts after ter-rjts merges, because ter-rjts changes the reflections passes.
3. For each attributed cause, a fix ticket. ter-k96l and ter-eqtb become fix tickets that wait for their diagnosis.
4. When the fixes merge, ter-3g1g measures the frame again from the corrected image, and the performance epic goes on.

## What a diagnosis delivers

- A gallery: captures at 1920 x 1080 of one or more frames of each of demo-exit's nine films, and of the effect's review scenes. Each artifact found gets a crop, its position, and the switch or probe that removes it.
- For each artifact: the stage that causes it, with the probe's evidence, and whether it is a fault or a limit of the method.
- For each fault: the candidate fixes, with their cost and their effect on the image where a probe can show it. No fix goes into the code in the diagnosis ticket.
