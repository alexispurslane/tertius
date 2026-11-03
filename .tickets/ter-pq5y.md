---
id: ter-pq5y
status: closed
deps: [ter-nydu, ter-izsu, ter-ff6y, ter-of1p, ter-3g1g, ter-okrp, ter-19fm]
links: []
created: 2026-10-25T22:29:46Z
type: epic
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, performance]
---
# Rendering performance: the whole frame in 12 ms of GPU time at 1080p

The coda of the rendering upgrade (user, 2026-10-25). Design: docs/plans/rendering-performance.md. The frame of demo-exit at 1920x1080, Release, on the development Mac (Apple M1 Max), takes at most 12 ms of GPU time once every feature of the epic is in.


## Notes

**2026-11-01T13:10:42Z**

The image rule of the share tickets, refined by the orchestrator on 2026-11-01. It replaces the 2-level rule of 2026-10-31, which blocked every restructuring of a shader: a change that computes the same values in another shader module or with another instruction order moves a few knife-edge pixels by more than 2 levels, because a rounding difference crosses a threshold (a ray that grazes an outline, or SMAA's edge test). ter-rjts (own-ray list: 2 pixels of 2.07 million at 7 levels on reflected outlines), ter-mfyv (gather PCF: 6 single pixels at 3 to 20 levels with SMAA on, all within 1 level with SMAA off) and ter-pjza (an upsample with array arguments: 1 pixel at 11 levels on a silhouette under SMAA) each found such pixels.
A change keeps the image when both conditions hold:
1. Same method and same quality. It computes the same quantity with the same sample count, step count, resolution, filter kernel and weights, stored precision, thresholds and defaults. Moving a computation into another shader, pass or dispatch, sharing reads, skipping work whose result no pixel uses, and computing a hardware filter's kernel in the shader with the same weights all count as the same method.
2. Only isolated threshold pixels move. In the comparison captures, no pixel differs by more than 2 of 255 levels in any channel, except isolated pixels where a rounding difference crosses a threshold: at most 20 such pixels per 1920 x 1080 capture (0.001 %), none adjacent to another. The builder shows the cause of each: the same comparison with SMAA off stays within 2 levels, or a probe shows that the pixel lies on a knife edge (for example an own-ray pixel on a reflected outline). The ticket note lists each such pixel with its position and difference, and the builder looks at a crop of the base and of the change (at least 10x zoom) and states that the difference does not show.
A change that fails either condition is a default change: ter-3g1g takes it to the user with captures, and no such change goes into the code. A shift of the whole frame (for example the automatic exposure) fails condition 2 even when each pixel moves little.

**2026-11-01T13:15:31Z**

2026-11-01: continuation runs after the disk filled up (version 869b0b07, each with worktreePath and the refined image rule): ter-mfyv d5506515-e4c2-4a03-87cb-7bd5c4a4546b, ter-rjts fca26158-0f42-4fc7-a7c2-0c8118a6e5b2, ter-pjza d9dea78a-d313-438e-8aaf-951e0c1c53f3, ter-prio 9c9870f4-0baa-44e0-8e9b-825d23f39a17. The first runs: b14a2f6e (blocked, ENOSPC), 338f0bac, 251261a0 and ab44f491 (agent_failed, ENOSPC).

**2026-11-01T14:25:02Z**

Ruling on the image rule's adjacency clause (orchestrator, 2026-11-01, asked for by the ter-mfyv review). SMAA blends across an edge onto the pixels on both sides of it, so one edge decision that flips moves up to two touching pixels. Such a pair counts as one threshold site under condition 2, when the same comparison with SMAA off stays within 2 levels. Any other touching threshold pixels still fail condition 2. This covers ter-mfyv's gather PCF: unbelt (1259,590)+(1260,591) and pillar-room (1526,832)+(1527,832) are each one SMAA edge decision, and all 15 captures stay within 1 level with SMAA off. Apart from the rule, the user asked on 2026-11-01 for the gather PCF to go into ter-mfyv without further benchmarking, so the gather is the user's decision too.

**2026-11-01T15:34:07Z**

2026-11-01: ter-prio's first continuation (9c9870f4) ended with done=false and no fork (environment faces, G-buffer and prefilter not tried). Second continuation: run 06ca6f7f-672b-4d5f-af1c-770674c27361 (version 869b0b07), from HEAD d1c9c63c. Settled for it: a single-pass prefilter downsampler with the same kernel is pass fusion and in scope; a different bloom filter is a default change (a note only); async compute is out of this ticket (an estimate as a note).

**2026-11-01T16:02:47Z**

Paused on 2026-11-01 by the user's decision: correct reflections and screen-space light come before more optimization, because the fixes change the starting point (epic ter-b2da, docs/plans/screen-space-artifacts.md). The running workflows finish their current stage only: ter-rjts finishes its fix stage and merges. ter-prio (06ca6f7f) and ter-ww0h (cc31bfac) stop when their build stage ends, before review. Their branches stay for a later continuation. ter-pjza merges after its hardening. ter-g25a, ter-ku9c and ter-u102 do not start until ter-b2da closes. ter-3g1g now depends on ter-b2da.

**2026-11-01T18:30:34Z**

Ruling on condition 2 of the image rule (orchestrator, 2026-11-01, asked for by ter-rjts's fix stage). A pixel counts as shown to lie on a knife edge when a probe places it on the boundary where the pass under test switches between two ways of computing it, for example the edge of the upsample's own-ray region, and its two candidate values there differ by about as much as the moved pixel. The probe need not reproduce the base's exact value. Applied to ter-rjts's change 1 (the own-ray list): unbelt (1136, 493), 19 levels with SMAA off, is shown directly (the base's blend equals the pixel's own ray, the head's blend does not). Unbelt (1043, 670), 7 levels, is a blended pixel at the own-ray region's edge, and its blend and its own ray lie within a level of each other in the head. Both are isolated, the other 2.07 million pixels and the other 25 captures are unchanged, and the 10x crops show no difference. So change 1 keeps the image and stays in ter-rjts.

**2026-11-03T12:35:12Z**

Closed on 2026-11-03 at the user's request, without its work merged: it belongs to the pre-temporal renderer (spatial reuse, the 12 ms performance push, or the old reflection and bounce pipeline), and epic ter-89xq, the temporal renderer, supersedes it. Its notes and any branch or worktree stay for reference.
