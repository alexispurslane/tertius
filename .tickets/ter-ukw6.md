---
id: ter-ukw6
status: in_progress
deps: []
links: [ter-kh4g, t1-tbz9, t1-3j2j]
created: 2026-11-14T17:52:37Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# A screen ray that passes a thin sunlit sill's edge into an opening still hits the sill's thickness slab

Found 2026-11-14 by the t0-adau probe (/tmp/t7-3e22/robust, patches hitclass-final.patch then hittrue-final.patch after the survey's probe patch; main 557d825b1). In rect-lamps with the Sun on and clamps off, about 74 % of the glow box's bright reflection light on the ceiling (about 64 % of all its reflection light) comes from reflection rays that already left through the end window past the sill's outer edge, where the camera sees sky, yet count as hits on the sill: render row y 375, x 596-679, the sill a sub-pixel strip seen almost edge-on. Code: src/Tertius.Rendering/Shaders/screen_walk.glsl, walkScreen: at the finest level a pixel's slab reaches from its surface w back to w / (1 + ThicknessShare), ThicknessShare = 0.1 (DepthPyramid.cs); a ray inside it hits (meetsSlab && hitsPixel) with entryShare 0..1. src/Tertius.Rendering/Shaders/screen_trace.glsl, traceFrom ~766-767: fade = edgeFade(hit) * (1 - entryShare); light = mix(missLight, hitLightOf(hitPixel, kind), fade). The sunlit sill is about 1,000 times brighter than the miss light, so even a hit near the slab's back reads a huge value. About 26 % of the bright light is real front hits on the sunlit sill (t0-qwx8 territory). The clamps-off reference holds the false light too; after the fix, re-measure the robust reference, the glow levels and the specks (t0-adau), and only then decide on guiding the rough reflection rays.


## Notes

**2026-11-14T18:53:10Z**

2026-11-14, Alexis: design the back-face depth layer first (epic ter-kh4g), not the edge-on test. Design A (test every surface) is out because it leaks behind thick boxes; design B (test only surfaces seen within about 6 degrees of edge-on) is a tuned exception that the back-face layer replaces. Brief: docs/handoffs/2026-11-14-renderer-lab.md, part 1.

**2026-11-14T22:51:57Z**

2026-11-14, stage 1 of ter-kh4g (docs/research/back-face-depth-layer/README.md). A dump of the front and back distances of rect-lamps shows the sill's real thickness: front 39.52 to 39.62 m, first back face behind it 40.02 to 40.12 m, so 0.50 m, where the 10 % rule gives 3.95 m. By the fade histogram of hits9/hits-true.txt, 66.3 % of the bright light enters the slab behind the real back face, and a back layer rejects it. About 8.2 % stays as hits that the probe counts as false: rays inside the real 0.50 m of depth and within one pixel of the sill. This is an estimate from the saved data, not a new probe run. Two Mac stills (the 10 % rule against the slab trimmed at the back face) show the ceiling's bright clouds and most of its specks gone, and the floor's sparkling patches smaller. The side faces of every window opening are sunlit strips in the same wall of 0.5 m and act as the sill does, so the false light is not the end window's alone.

**2026-11-15T01:25:26Z**

2026-11-15, stage 1b of ter-kh4g measured the sill leak with a census on the RTX 2000 Ada (docs/research/back-face-depth-layer/stage-1b/pictures.md, part 1). Under the 10 % rule, 6,783 bright samples with a light of 3,011,681 are false hits, 74.34 % of the glow area's bright reflection light, and 99.5 % of that light lands on the first render row below the end window. Under the layer as the slab, 4 samples with a light of 483 stay, 0.016 %. The back-face depth layer removes this bug, and no other mechanism is needed for it.

**2026-11-22T13:53:11Z**

2026-11-22, the line orchestrator. The design of this ticket's last step, a measurement on the merged main. No engine code changes.

The layer is on main since f7d606fec. Main before the merge is c4c1f1f2f: the 10 % rule, with the sun's shadow fix (t8-rjno). Main after the merge is f7d606fec: each pixel's slab ends at its real back face.

What exists already (docs/research/back-face-depth-layer/acceptance.md, point 2): on the layer's code, the census of stage 1b counts 0 bright samples of any class in rect-lamps' glow area over 400 draws. The shadow fix removed the leaked sunlight that lit the sill, so today no bright light lies there under either rule. That census alone does not show what the layer does to a sill that the sun really lights. And the census of main before the merge did not run.

The measurement has two parts, each as before against after, with stage 1b's hit classes (class 0 the false hits, class 1 the real hits):
1. rect-lamps as the scripts give it: the census of main before the merge (stage 1b's census.patch) beside the census after it (acceptance-tools/census-port.patch), bright samples and every sample. One still per side at one fixed exposure, frame 239.
2. A view where the sun really lights a thin sill or a reveal that the camera sees almost edge-on, with an opening behind it. This is the ticket's case without the leak. Change the sun's direction or the camera in a scratch copy of the script, or in a scratch patch of the demo if the script cannot. First list what lights each pixel of the glow area (the sun on the sill, the lamps, the sky), so that the view tests the false hits and not a missing light. Then the same census and the same stills, before against after.

The result says, per part: the count and the light of class 0 and class 1 before and after, the share of the glow area's bright reflection light that is false, and what an eye sees in the two stills. Then one of two ends. If the false hits are gone or near 0 in both parts, the ticket closes with the numbers and the stills. If some stay, the record names them: where they lie, how bright they are, and which rays make them. A fix is another ticket.

Deliverables: docs/research/back-face-depth-layer/sill-hits-on-main.md with the numbers, the commands and two to four crops as PNG files beside it, and a note in this ticket. The census stays a patch. Out of scope: the older follow-ups of t0-adau (the robust reference, guiding of the rough reflection rays).
