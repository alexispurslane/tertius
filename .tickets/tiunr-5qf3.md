---
id: tiunr-5qf3
status: closed
deps: [tiunr-nxtr]
links: [ter-12xp, tiunr-5jqs]
created: 2026-11-08T14:41:59Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# A firefly limit removes the rough floors' white specks

User decision 2026-11-08: once the box fix (tiunr-nxtr) reports, add the standard firefly limit without asking again: each sample's brightness is limited by a bound from its neighbors before it enters the temporal histories, so that one rare bright ray cannot make a speck. Cost accepted: reflections of small bright lamps get somewhat darker. When done, show the user only a few videos: a moving camera and a moving emitter, on the smooth and the rough plate. Keep the box fix on by default only if its measurements clearly help.


## Notes

**2026-11-08T16:05:02Z**

2026-11-08 implementation agent: STOPPED at step 4. The median limiter removes real rare-hit light, not only specks.

Done: firefly_limit.glsl (limitFirefly, knee 4 x median of the 8 same-surface neighbors 2 px away, specular within 0.1 roughness, at least 4 neighbors) is the default in accumulate_temporal.comp and accumulate_diffuse_single_temporal.comp. Removed spec constant 9, ShaderConstants.NeighborLimitPrototype, IndirectLightPrototype(.Select) and its tests, TERTIUS_INDIRECT_LIGHT_NEIGHBOR_CAP (test helper, Program.History.cs, Program.Live.cs lost its live "uncapped" mode). ARCHITECTURE.md updated. Tests: AccumulatorRenderTests+FireflyLimit (red 13/19 for the right reason, then green); 3 older inputs adapted (spatial repair 9-among-0 -> 3.25-among-1, precise-history checkerboard phase, box-tail hit also on the 8 pixels 2 px away). AccumulatorRenderTests + ScreenTraceHardeningTests Checked 114/114 green. Two more tests written after that run and not yet run: ThreeNeighborsAreTooFewToJudgeTheSample, TheBrightnessOfASampleAndOfItsNeighborsIsTheirBrightestComponent.

Finding: where fewer than half of the 8 neighbors hit an emitter, their median is 0, so the limit removes every hit. Light that only rare hits carry disappears: the plate's whole diffuse glow from the emissive bar, and the rough reflection's halo. The cap-isolation study of 2026-11-06 measured the same loss (rough crop 75.6% vs 105.4% of reference, 73% of the outer band missing). A larger knee cannot help where the median is 0.

FSR still captures (options of tiunr-nxtr, 128 draws), crop 80x160 at (530,240):
| case | plate mean pre-FSR | plate noise pre-FSR | final red mean | final red noise | whole-frame PNG mean |
| rough before | 103.36 | 7.83 | 113.99 | 2.48 | 5.40 |
| rough knee 4 | 75.89 | 4.20 | 76.95 | 6.14 | 1.54 |
| rough knee 8 | 75.99 | 4.27 | 77.07 | 6.12 | 1.54 |
| rough knee 16 | 76.03 | 4.29 | 77.12 | 6.14 | 1.54 |
| smooth before | 106.66 | 6.12 | 116.43 | 1.11 | 4.80 |
| smooth knee 4 | 104.71 | 2.60 | 105.64 | 8.61 | 1.49 |
| smooth knee 8 | 104.71 | 2.61 | 105.66 | 8.60 | 1.49 |
| smooth knee 16 | 104.71 | 2.61 | 105.67 | 8.60 | 1.49 |
The before build is byte-identical to tiunr-nxtr's static-rough-still-off capture (14/14 files). Demo reflections scene: the floor's specks are gone and the bar's reflection stays; the demo floor is lit mostly by direct lights, so it hides the loss.

Not run: full Checked and Release suites, snapshot review, coverage, mutation testing. Evidence: /private/tmp/tertius-firefly-limit-20261108 (result.json, captures/, shots/, videos/, source/src-and-tools.patch).

**2026-11-08T16:10:40Z**

2026-11-08: the median-based limit removed real light (rough crop plate mean 103.4 to 75.9, whole frame 5.40 to 1.54; the plate's glow from the bar vanished, /private/tmp/tertius-firefly-limit-20261108/videos/). The orchestrator reverted it: the src-and-tools patch reversed, AccumulatorRenderTests.cs and ScreenTraceHardeningTests.cs rebuilt from HEAD plus the box-tail run's saved patches, IndirectLightPrototypeTests.cs restored from HEAD, an empty firefly_limit.glsl removed. Every reverted file equals the before copy; the three test classes pass in Checked, 116 of 116. Cause of the specks found in source: the glowing bar (DemoScenes.Reflections GlowingBox, Emissive 6800) holds no light, so only lucky rays carry its light; an entity that holds a light sends no glow to rays (frame.glsl glowSent, user rule 2026-10-27).

**2026-11-08T16:22:16Z**

2026-11-08: superseded by tiunr-5jqs (the user chose a tube light over a spatial filter).

**2026-11-08T20:05:06Z**

2026-11-08: superseded by the tube light (tiunr-5jqs). The specks came from emissive objects that hold no light, so only rare rays carried their light. The tube light sends the bar's light through the exact light loop and hides its glow from the rays, so the floor's specks are gone without a firefly limit, and reflections of small bright lamps keep their brightness.
