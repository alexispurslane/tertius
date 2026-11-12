---
id: ter-xo83
status: closed
deps: []
links: []
created: 2026-11-12T10:23:35Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# Reflection resolve: each shared ray counts with the pixel's own density (fewer daubs near contacts)

Sweep addendum A7 variant ownw1 (/private/tmp/tertius-sweep-20261111/addendum.md). Main's one-block resolve divides each shared ray by the density of the pixel that traced it; ownw1 uses the receiving pixel's own lobe density for the ray's direction. On flat surfaces nothing changes (deck floor equal to two decimals; paint and plain floor equal). Near a contact (roughness 0.1) the daub score after 60 draws falls 0.0261 -> 0.0104 and grain 1.69 % -> 1.23 %. Cost: one density evaluation per shared ray, not yet timed. Image change, so stills and a film against main; the mottle agent (t7-3e22) owns the reflections' clamp, so coordinate. Proposed for the user's morning list.


## Notes

**2026-11-12T12:23:35Z**

2026-11-12 user decision: BUILD. A builder makes it with tests and times it on the deck; it merges only if the cost is small. Coordinate with the mottle agent (t7-3e22, Design A), which owns the clamp of the bounce history.

**2026-11-12T13:47:10Z**

2026-11-12 build (branch ter-xo83-own-density-resolve). What changed: screen_trace_resolve.comp weighs a block's reflection ray by the receiving pixel's own density along the recorded direction (reflectionDensity), floored to 0 below LeastDensity like a recorded density. Own rays and diffuse rays keep their recorded density. The sweep's ownw1 also turned each ray through its hit point; that part is not needed: the plain own density reproduces its numbers.

Image (reflections scene, low view, lowfilm-000026, against the sweep's long reference /private/tmp/tertius-sweep-20261111/refl/out3/reference-mean.npy; today's main scores as the sweep's base):
- roughness 0.1 near a contact: daub 0.0263 -> 0.0105, grain 1.70 % -> 1.23 %, clouds 1.37 -> 0.99, broad 1.66 -> 1.08. Film flicker in that box 0.89 -> 0.63 of 255.
- plain floor 0.2, glow 0.3, blue paint 0.3: equal to two decimals. Red paint 0.2: daub equal (0.0445/0.0447), broad error 1.15 % -> 1.27 %.
- deck still: mean difference 0.07 of 255, 0.035 % of pixels differ by more than 2.

Cost on the deck (deck-120, 1920x1080, --in-flight 2, stock MoltenVK, four interleaved pairs, all at 1296 MHz): median frame span main 20.791/20.746/20.803/20.797, branch 21.292/20.840/20.813/20.822 ms; p95 main 21.298/21.169/21.333/21.355, branch 22.113/21.318/21.281/21.327 ms. Median of medians +0.037 ms, median of p95 +0.006 ms. The first branch run was 0.5 ms slower; the other three pairs differ by -0.05 to +0.15 ms in p95. Under the 0.15 ms bar against the 0.0157 daub gain.

Test: ResolveSharedReflectionTests (red on main: shared samples 4x and 1/4x of the own-ray sample). LightAgeHistoryTests.ARayDimmerThanAQuarterOfTheReceiversHistoryDoesNotCutIt dims its emitter to 0.2 instead of 0.32: steadier shared weights left 7 dim receivers, the scene now gives 48 (needs > 20).

Coverage: no C# line of src changed. The changed shader lines run in every resolve test; the own-density branch runs in ResolveSharedReflectionTests.

Mutants (tools/mutate.py):
| id | place | change | result | killed by |
|---|---|---|---|---|
| S01 | screen_trace_resolve.comp:41 | no own density: main's recorded density | killed | ResolveSharedReflectionTests |
| S02 | :41 | shared diffuse rays take the reflection density too | killed | ScreenTraceHardeningTests.TheDiffuseResolveCapsAWeightedRayBeforeHalfFloatStorage |
| S03 | :41 | own density for own rays, recorded for shared | killed | ResolveSharedReflectionTests |
| S04 | :43 | own density doubled | killed | ResolveSharedReflectionTests |
| S05 | :44 | no LeastDensity floor on the own density | survived | judgment call: the floor changes only a density between 0 and 2^-100, which only a driver that keeps denormals and a ray far outside the lobe give; no test scene reaches it, and the portable-Vulkan reason is written beside the line |
| S06 | :44 | a shared reflection ray never brings light | killed | ResolveSharedReflectionTests |
6 rows: killed 5, survived 1 (judgment call).
