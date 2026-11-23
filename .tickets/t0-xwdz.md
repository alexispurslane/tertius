---
id: t0-xwdz
status: in_progress
deps: []
links: [ter-bcwr, ter-4iic, t58-np60]
created: 2026-11-23T12:08:20Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, temporal]
---
# After a reset, 23 to 50 % of the pixels still trace their own reflection ray in draw 8, and the tail lasts to about draw 12

Found by the lead of the announced dip of the render scale (ter-bcwr) on 2026-11-23, in the own-ray logs of the catch-up ray experiment (/private/tmp/t0-gbgj/gumm-mac/{cut,start}-today/ownrays-none.txt, 1280 x 720, today's code). Reflection / diffuse own rays per draw after a reset: draws 0 to 7 about 678,000 / 678,000 (every pixel); draw 8: 154,400 / 2,306 after a camera cut and 335,822 / 32,997 after a start; draw 9: 43,798 / 1,991 (cut); draw 12: 15,711 / 1,988 (cut). So the bounce light's own rays end at draw 8 as designed, and the reflections' do not: 23 % (cut) to 50 % (start) of the pixels still trace their own reflection ray in draw 8. On the card draw 8 costs 1.9 to 3.6 ms more than draw 9 for it (15.9 to 16.5 ms per unit of area against 12.8 to 13.4). At a start, draw 8 is the first ordinary measurement of the render scale's controller, and it alone lowers the ordinary request to 0.92 to 0.96, which takes 28 to 39 draws to return (part of the start's own dip, t58-np60).

The cause is inferred from the shader code and is not logged: the anti-lag of accumulate_spatial.comp acts once a history is longer than the fast cap (4) and cuts the length to max(fastCap, L (1 - lag / antiLagShare)). The reflections' box takes no retained widening, so in draws 5 to 7 a young and noisy reflection history moves the clamp and is cut back to 4 at many pixels. Those pixels read under FullRateFrames in draw 8 and some for a few draws more.

## Design

Step 1, measure and build nothing: log the reflections' history length per draw 4 to 12 after a cut and after a start (the share of pixels under FullRateFrames, and the share that the anti-lag shortened in that draw), to confirm or refute the inferred cause. Step 2, only with the cause confirmed, write the design as a note here. Two directions to weigh then, neither chosen: the anti-lag does not shorten a reflection history that is still young (its excursions are its own noise, not a change), or the plan's rule for an own reflection ray counts the draws since the reset and not the stored length. Each changes what the first draws after a reset look like, so the picture of draws 0 to 20 decides.

## Acceptance Criteria

The record holds the per-draw log and names the cause with its evidence. If a change follows: after a cut and after a start, the reflections' own rays in draw 8 fall to the level of draw 12, the picture of the first 20 draws is not worse than today's in a side-by-side video, and at a start the render scale's ordinary request is not lowered by draw 8.


## Notes

**2026-11-23T14:12:05Z**

2026-11-23, 09:12 New York time, line orchestrator of the Design X line. Step 1 starts on the line's free slot (answer 100, reported to the orchestrator): the lead of the render scale's dip, in its worktree t3-52bfcd29-81249085ce80dd9f, branch t0-xwdz-reflection-tail from main 4bb8489b3. It measures and builds nothing that merges: a scratch probe logs the reflections' history length in draws 4 to 12 after a camera cut and after a start, on the Mac, and the record goes to docs/research/t0-xwdz-reflection-tail. Money: 0.
