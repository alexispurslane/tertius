---
id: ter-nw4a
status: in_progress
deps: []
links: [ter-eqtb]
parent: ter-b2da
created: 2026-11-02T18:08:19Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [rendering, reflections]
---
# A reflected outline at a depth edge shows as large pixel stairs on a floor seen at a grazing angle

Asked by the user on 2026-11-02 about the floor film's stair steps in ter-eqtb's sheet at the old limit 0.6 (captures/eqtb/fix/sheets/shot-floor06-mutual.png, crops in captures/ring/). What remains after ter-eqtb's bounded planes: a pixel at a depth edge keeps one depth over the half of its square past its center line, so the edge of a reflected surface is a staircase of whole pixels. A floor seen at a grazing angle stretches each pixel of that staircase into a long step, and each floor pixel takes one ray with a hard hit, so a floor of roughness 0.38 shows the staircase sharply where a 0.38 lobe gives a soft outline. On demo-exit's floor film at limit 0.6 the steps lie from y 540 to 600 (ter-eqtb's notes). At today's limit 0.37 that floor (paint, 0.38) takes no SSR and shows no steps (captures/ring/floor-steps-037-vs-06-2x.png). It shows on surfaces below 0.37 that reflect a depth edge at a grazing angle, at a size that shrinks with the roughness, and it stands in the way of a higher limit. Design it twice before a build, for example: an outline as soft as the cone's footprint at the hit, against a place of the edge inside the pixel. The user's decisions hold: nothing accumulates over frames, no stochastic SSR (ter-rnjk closed).


## Notes

**2026-11-02T18:11:44Z**

2026-11-02, the user's decision: this ticket belongs to the correctness epic ter-b2da and blocks the performance work behind it. The user's reasons: the steps look horrendous, they point to deeper issues with SSR, and the fix can change the cost, so it must come before performance work. Priority 1.

**2026-11-02T18:12:56Z**

2026-11-02: launched as workflow run 5934b07c-bded-4c5d-8a9b-42e1c6c64310, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-nw4a-reflected-outline, base 8b2a942a3 (brief /tmp/briefs/nw4a.txt): a probe that finds the cause with traced rays, answers whether SSR has a deeper fault, and measures two designs (A: an outline as soft as the cone's footprint at the hit; B: the edge's place inside the pixel) for the user's choice.

**2026-11-02T19:45:32Z**

2026-11-02, the user's choice after the probe (run 5934b07c, head 3de2147e1, sheets in captures/nw4a/sheets, copied to the main checkout): design A, footprint coverage from the neighbors' rays. Cause as traced: no new mechanism; ter-eqtb's remainder on a far floor seen almost edge-on (no row passes the 1 % test) meets artifacts 3 and 5 (one ray decides the lobe), and the grazing view magnifies each far row 25 to 30 times. Steps do not shrink with roughness (30 to 31 rows at 0.2 and 0.3). A: step edges per 1000 px 7.81 to 0.28 at 0.6 and 4.96 to 0.05 at 0.2; +0.10 ms on the step view, about 0 elsewhere; the build must fix the tear on curved receivers; artifact 5's bright region stays. Rejected: B (closed creases and silhouettes inside the pixel), and A with B.

**2026-11-02T19:47:06Z**

2026-11-02: the build of design A launched as workflow run 67504b63-8e98-4971-9e1c-945c289508ee on this branch (brief /tmp/briefs/nw4a-cont.txt). Settled by the orchestrator: store t in the hit image if a free channel or an exact encoding allows it; fix the tear on curved receivers; below one texel the pixel keeps today's path; remove the upsample's own rays at outlines where the footprint passes one texel, if that keeps the images.

**2026-11-02T22:55:09Z**

2026-11-02, the user's choices after the fix stage (run 67504b63, head e4af3ce28) stopped at the fork of the taps' cost: nearest taps (rejected: bilinear taps, +3.37 ms on reflections low, +0.46 ms on the step view, +0.15 ms on frames with no blending pixel), and a cost pass in this ticket before the merge (rejected: merge now and cut the cost later). The user was told that nearest taps start at +1.75 / +0.20 / +0.03 ms and that the cost after the pass is unknown. ter-7d82 keeps the blocks as follow-up work. The ball of 0.2 still shows its neighbor's reflection sharply near the rim: ter-cklj (an elliptical footprint) is filed in the epic, not as a blocker. Continuation launched: workflow run ba772b3f-6bd8-41f3-b372-651053f130d1 (version 869b0b07), brief /tmp/briefs/nw4a-cost.txt.

**2026-11-03T00:40:02Z**

2026-11-02: the cost pass (run ba772b3f) did all its work, clean at 008dab0bb, but failed with output_json because its report began with a sentence of prose. Result: nearest taps built; no image-keeping cut lowered the cost (the texel-depth and tile cuts were reverted because they cost the deck and floor windows 0.05 to 0.06 ms); final cost of the 'reflections' pass: reflections low +1.805 ms, step view +0.191 ms (goal +0.10, not met), deck +0.025 ms, floor +0.023 ms. Continuation launched for the review and fix: run cee79292-e694-4feb-9b3e-f68b4f961544, brief /tmp/briefs/nw4a-cost-cont.txt.
