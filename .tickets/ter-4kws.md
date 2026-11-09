---
id: ter-4kws
status: open
deps: []
links: []
created: 2026-11-09T15:10:17Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Rays that hit a surface read its fast diffuse history again, if the specks stay away

Since ter-9371, a diffuse ray's hit reads the filtered light, which comes from the 32-frame slow history, so the orbit cabin's bounce light needs 84 draws to settle after lamps switch off, against the goal of about 60 (ter-8hyd). With hits on the fast history, the research measured cabin 44/41, bay with the sun off 35/32, lamp hall 31/34 (/private/tmp/tertius-diffuse-clamp-20261108/). ter-9371 moved hits to the filtered light so that specks would stop spreading; that was before measuring. Test whether hits on the fast history bring the specks back now that the filter cleans every surface's diffuse light. Accept only if every settle count is at most 60, the speck counts and region means of still scenes stay within the noise of main, and walks and the bay's moving sun show no new blotches.


## Notes

**2026-11-09T17:36:21Z**

Experiment, 2026-11-09: rejected. The change failed the speck acceptance in the orbit cabin, so nothing merged.

The change: `ScreenTraceImages.HitDiffuse` returned the last fast diffuse history (`Diffuse.HitImages(part: 0, parity).LastFast`) instead of `Filter.Filtered`. The filter, the clamp and everything else stayed as on main (8e7aa203b). Binaries: main and the change, each with the temporary probe of /private/tmp/tertius-diffuse-clamp-20261108/probe.diff plus a sun turn (`TERTIUS_PROBE_SUN_TURN`, degrees per draw). Evidence and scripts: /private/tmp/tertius-hits-fast-20261109/.

Speck counts (ter-9371's rule, per 10,000 pixels) and mean linear display luminance, film frames 120 to 159 from an empty history, still camera, auto exposure. The ± value is the standard deviation over the 40 frames.

| Scene, region | Specks, main | Specks, change | Mean, main | Mean, change |
|---|---:|---:|---:|---:|
| Cabin ceiling | 0.4 ± 0.2 | 0.3 ± 0.1 | 0.1365 ± 0.0015 | 0.1435 ± 0.0018 (+5.1 %) |
| Cabin floor | 0.0 ± 0.0 | 1.2 ± 0.1 | 0.1185 ± 0.0001 | 0.1162 ± 0.0004 (−1.9 %) |
| Cabin left wall | 0.0 ± 0.0 | 0.2 ± 0.2 | 0.0904 ± 0.0023 | 0.0967 ± 0.0031 (+7.0 %) |
| Cabin whole image | 1.2 ± 0.1 | 2.8 ± 0.2 | 0.1272 ± 0.0005 | 0.1285 ± 0.0014 |
| Bay (sun on), whole image | 0.3 ± 0.1 | 0.3 ± 0.1 | 0.0158 ± 0.0007 | 0.0154 ± 0.0004 |
| Lamp hall, whole image | 0.7 ± 0.0 | 0.7 ± 0.0 | 0.0685 ± 0.0008 | 0.0685 ± 0.0008 |

The cabin floor's new specks are single bright pixels spread over the whole floor (crop-cabin-floor-150.png). The cabin walk shows the same: still part 1.1 → 2.8 specks, walking 1.2 → 1.5, after the walk 1.4 → 3.2. The bay's moving sunbeam (sun turned 0.1° per draw, 300 draws) shows no new blotches. In the cabin, the mean whole-image light at a fixed exposure (4.08, lamps on, draws 153 to 192) is 3.8 % higher with the change (0.4729 against 0.4558), because a hit then reads the unclamped fast history (ter-18hc).

Settle counts: not measured again on today's main. The runs were cancelled after the speck result decided the ticket, to free the GPU queue for the survey. The research of 2026-11-08 (built at 3e5038916, settle2.py) measured main 85/62, 57/47, 35/37 and hits on the fast history 44/41, 35/32, 31/34 (cabin, bay with the sun off, lamp hall; lamps off/lamps on). So the change would meet the 60-draw goal, but it brings back the specks that ter-9371 removed.

Conclusion: the reason of ter-9371 for the filtered light still holds. A sunlit hit pixel's fast history holds a speck at a quarter of its sample's light, and a hit copies it into the floor's diffuse light, where the filter does not remove it. The faster settle must come from somewhere else, for example the light-change age on the diffuse history (ter-wkvw).
