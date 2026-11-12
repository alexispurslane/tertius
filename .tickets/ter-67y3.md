---
id: ter-67y3
status: closed
deps: []
links: []
created: 2026-11-12T02:59:35Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# One image for the light at a ray's hit, in place of 13 to 18 image reads at every hit

Sweep row 3 (/private/tmp/tertius-sweep-20261111/report.md, section 7), approved by the user on 2026-11-11 ("we should probably still deal with this!"). Expected gain on the deck: about 0.2 ms. Exact image intended; if the hit light must change, show stills against main.


## Notes

**2026-11-12T10:32:57Z**

Built 2026-11-12 on branch ter-67y3-hit-light-image. The ray plan writes the light that leaves each pixel toward a ray of each kind into ScreenTraceImages.HitLight (RGBA16F, one layer per kind, binding 26), and a hit reads one texel. The plan shares its material and reprojections with the hit light. No new dispatch: 141 encoders per frame before and after.

Timing, deck (scripts/temporal-upscale-timing.json, 1920x1080, 120 frames from frame 1), one exclusive take of 2026-11-12 04:38, Metal System Trace without counters, clock 1291 MHz in every run, rounds a and b:

| Build | Frame span median (ms), timer | Frame span p95 (ms), timer | ray plan | reflection rays | diffuse rays |
|---|---|---|---:|---:|---:|
| main | 22.207, 22.239 | 22.711, 22.729 | 0.892 | 2.637 | 1.400 |
| branch (shared reads) | 22.034, 21.978 | 22.513, 22.555 | 1.293 | 2.127 | 1.274 |
| first build (no shared reads) | 22.011, 22.087 | 22.611, 22.688 | 1.369 | 2.122 | 1.272 |
| shared reads + light-age reprojection | 22.004, 21.961 | 22.464, 22.499 | 1.284 | 2.128 | 1.282 |

Pass times are trace busy medians. The first frames after the cut gain more: the trace's frame span p95, which holds them, fell from 32.2 to 29.7 ms. A B10G11R11 hit light measured the same as RGBA16F (plan 1.36 against 1.37 ms in take 1), so the format is not the cost; kept RGBA16F, the exact one. The light-age reprojection share gained 0.009 ms and is not built.

Image: stills of deck, rect-lamps (frame 240) and bay-sunbeam (demo-surface, bay-camera, tick 420), fresh 33-draw stills. Main renders the same bits twice. Branch against main: max 31, 41, 59 codes, mean 0.20, 0.45, 0.41, pixels above 2 codes 657, 3,963, 10,107 of 2,073,600. Main with each hit's light rounded to a half float in place: max 12, 47, 60, mean 0.18, 0.43, 0.41, pixels above 2 codes 509, 3,849, 9,795. FLIP mean against main: branch 0.0050, 0.0089, 0.0083; rounding control 0.0047, 0.0087, 0.0082. The histories amplify a rounding of this size into scattered specks; the half float of the stored image explains the difference. Heat maps show no structure.

Film: split of lamp-hall-4-spot-moving at t1-4hij's fixed stops, main and branch identical: direct blotch 0.034 / 0.037 (t1-4hij's trail measure), whole 0.048 / 0.045, flicker 0.0059; only the reflections' end level moved, -0.003 to -0.004.

Mutants (tools/mutate.py, runs mutate, mutate2, mutate3, mutate4 in /private/tmp/ter-67y3): 28 rows, killed 23, timeout 1 (S04), survived 4, each answered:
- S07 (hittable takes the sky too): equivalent, because no walk hits a pixel of depth 0, so no hit reads the sky's texels.
- C04 (pool without the hit light's two descriptors): judgment call, the shared pool's other counts leave slack (the signal sets count 8, a renderer uses 4), and no test checks the pool's exact size.
- C11 (no plan barrier on the exposure buffer): equivalent in the frame's order, because only the exposure pass, after the tracer, writes the buffer.
- C13 (no rays barrier on the environment map): equivalent, because the plan declares the same sampled read earlier in the stage and nothing writes the map between.
- C05 (layout binding off by one) crashed the test host (exit 139), which mutate.py lists as no-tests and tools/README.md counts as killed.
New tests killed S01, S05, S09, S13, S14, C12 and C14: the MaxRadiance cap under a bright sun, the closed form where a roughness change fails only the specular history (with the plan's own reflection rays there), an impostor's glow, and the theory rows without the guided rays and with an environment map.
