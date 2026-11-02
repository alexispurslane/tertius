---
id: ter-b1j3
status: open
deps: []
links: [ter-s6zr]
created: 2026-11-02T18:24:50Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, environment-map, reflections]
---
# The environment faces draw a lamp's glow dark, so rough reflections from the map miss the lamps that SSR and the reference see

Found by ter-s6zr's probe (run efa3cac1, 2026-11-02). environment_face.frag draws the glow of an entity that holds a light dark (glowSent), so that the map's irradiance does not light the room with the lamp's power a second time. The specular map comes from the same faces, so every reflection that reads the map also shows the lamp dark. SSR's screen rays and ter-f3g2's 64-ray reference (lobenorm, SSR's trace with 64 rays) see the lit lamps. A probe that draws the glow in the faces (glow) moved brake-run's teeth, the antispinward edge and the chute ceiling to 148.7, 126.2 and 95.2, against the reference's 141.2, 120.7 and 96.2; it also changed the diffuse light and the exposure. To decide first: whether the map's specular read must show the glow, given that the sphere light's analytic lobe (ter-gf0t) already gives the lamp's highlight, so that neither the irradiance nor the specular counts the lamp twice. Then the smallest change that gives that, for example the glow kept out of the irradiance convolution only. The probe also named the faces' missing ambient light as a likely cause of the chute-top slide and the floor's lamp cut, which no probe showed cleanly.


## Notes

**2026-11-02T19:46:01Z**

Builder, 2026-11-02, branch ter-b1j3-lamp-glow-once from d7ace33a (main's code). Evidence: captures/b1j3/ in this worktree (git ignores captures/; the orchestrator copies it to the main checkout). The galleries are in /tmp/b1j3/g (base, d1, d2, ref, ref1, ref2), Release, headless, 1920 x 1080, Apple M1 Max, every GPU run under /tmp/tertius-gpu-measure.lock.

Result: design 1 is built. The screen's reflections leave out the glow of an entity that holds a light, as the map and the bounce source do. Step 1 showed that SSR, and with it lobenorm, counted such a lamp twice, and that the map counts it once. In the demo views, the change moves nothing visible: the paint (0.38) is above SSR's maximum (0.37). The 64-ray reference counted the cabin lamps twice on that paint, and that double count was most of what this ticket took for a missing lamp glow.

Step 1, the facts. A probe test (captures/b1j3/probes/LampProbe.cs, numbers in analysis/probe-base.txt) renders a black floor 1.5 m below the eye under a lamp 10 m ahead, with the map on, at roughness 0.05, 0.2 and 0.38. Three lamps: the demo's cabin panel (1.2 m square, glow P / (pi A) = 2.21, light of 10 W, radius 5 cm, 10 cm under it), a bulb whose glow is its light's own sphere (radius 15 cm, glow = I / (pi R^2) = 11.26, as DemoScenes.GlowLamp), and a strip 2 m x 10 m that the map's read from the eye meets. Each runs with the glow and light, with the light alone, and with the glow alone (no light holds the entity), under reflections off, the default, and a maximum of 1. "C# once" is MaterialSample.Reflected of the light alone, or the C# integral of the panel's bottom face at its radiance.
(a) SSR counts the lamp twice. Bulb at 0.05, default setting: 3.461 = light's lobe 1.541 + ray's glow 1.920; once is 1.542 (C# 1.542). Panel at 0.05: the glow adds 0.359 at the light's disc and 0.271 on the panel's image 30 cm beside it, where the lobe adds 0.0005. Panel at 0.2: +0.049. lobenorm traces the same pyramid, so it adds the glow the same way: ref - ref1 below.
(b) The map counts the lamp once, by the lobe alone. With the reflections off, glow-part = 0.0000 for all three lamps at every roughness, and the floor equals C# light once within 0.3 %. The strip shows that the map's read does see a glow that no light holds: 0.381 at 0.05, 0.334 at 0.2, 0.141 at 0.38. For the panel and the bulb, the map's read from the eye misses the lamp by parallax anyway (0.0001 to 0.0011, against the true reflection of 0.03 to 0.39).
(c) A reflection that counts the lamp once is either the light's lobe alone (the reflections off; design 1) or the glow alone (C# panel once; design 2). They differ in shape where the glowing surface is larger than the light's sphere. Panel at 0.05: the lobe is a disc of 9.83 under the 5 cm sphere and 0 elsewhere, the glow is 0.385 over the whole panel image. At 0.2: 0.434 / 0.037 (lobe, at the disc and 30 cm beside it) against 0.143 / 0.140. At 0.38: 0.039 / 0.028 against 0.034 / 0.031. For the bulb they are one image.

Step 2, the probes (captures/b1j3/probes, built in Release from a git archive of d7ace33 in /private/tmp/b1j3/probe):
- d1.patch, design 1: radiance_pyramid_first.comp subtracts emission - glowSent(emission, mark) from the lit image.
- d2.patch, design 2: the faces draw twice. The first draw leaves every lamp's glow out and feeds the irradiance alone; the second draws the glow of a lamp's entity (a light of radius under 1 km whose entity has an instance) and feeds the specular levels alone. The analytic specular of such a lamp leaves every reader of the main view (lighting.glsl, view == MainView). A star keeps today's rules, because a sun's sphere is smaller than a texel of the map. A real design 2 would draw once into a second output of the faces, which the user declined in environment-map.md, decision 18; the probe's double draw shows the image and an upper bound of the cost.
- ref = lobenorm.patch (ter-f3g2's p-lobenorm) + full06.patch (MaxRoughness 0.6, full resolution), as ter-f3g2's reference; ref1 = ref + d1; ref2 = ref + d2.

Step 3, the boxes (analysis/boxes.py, ter-gf0t's glow.py boxes, mean Rec. 709 luma in levels of 255; twice = ref - ref1, the glow that lobenorm adds on top of the lobe):
| view, box | before | d1 | d2 | ref | ref1 | ref2 | twice |
| films/brake-run teeth | 123.3 | 123.3 | 146.7 | 141.8 | 124.5 | 144.1 | 17.3 |
| films/antispinward edge | 114.2 | 114.2 | 128.0 | 122.9 | 113.5 | 125.2 | 9.4 |
| films/chute ceiling | 87.8 | 87.8 | 85.1 | 96.2 | 92.8 | 87.7 | 3.4 |
| films/chute-top slide | 103.9 | 103.9 | 103.3 | 118.9 | 107.9 | 121.1 | 11.0 |
| films/floor lampcut | 115.3 | 115.3 | 94.8 | 131.5 | 115.6 | 115.1 | 15.8 |
| films/floor steps | 101.1 | 101.1 | 99.2 | 104.7 | 101.4 | 102.6 | 3.3 |
| films/antispinward pillar | 110.7 | 110.7 | 151.6 | 110.9 | 109.1 | 123.1 | 1.7 |
| films/stack curve | 80.2 | 80.2 | 141.4 | 82.3 | 80.0 | 142.9 | 2.3 |
| films/deck fragments | 100.2 | 100.2 | 119.8 | 100.4 | 99.3 | 108.8 | 1.0 |
| demo-surface/terminal door | 22.6 | 22.6 | 22.8 | 64.2 | 64.2 | 64.3 | 0.0 |
| deck-late lowhalf | 139.4 | 139.4 | 140.6 | 134.5 | 134.7 | 135.3 | -0.2 |
| pbr-spheres/front floor | 85.9 | 85.9 | 85.9 | 88.1 | 88.1 | 88.1 | 0.0 |
The full table with the other boxes and the mean absolute differences is analysis/boxes.txt. Per box, lobenorm counts the lamp twice where twice is above 2: brake-run's teeth, the antispinward edge, the chute ceiling, the chute-top slide, the floor's lamp cut and steps, and the stack's curve. With the reference counting once (ref1), today's image matches it in those boxes within 1.2 levels, but for the chute ceiling (5.0) and the chute-top slide (4.0). The terminal door's gap (42 levels) does not come from a lamp: ref1 keeps it. d1 equals before in every box: the largest pixel difference over every view of the four galleries and the reflections scene is 7 levels (pbr-spheres). d2 moves boxes by up to +61 levels (the stack's curve) and -20 (the lamp cut). It removes the lamps' tall wall highlights, and with the map's read from the eye it puts a lamp's reflection where no lamp is (the bright patch in the middle of antispinward's floor).

The sheets (sheets/, look at each first; top row before | design 1 | design 2, bottom row the reference today | with design 1 | with design 2):
- films-brake-run.png: design 1 is before; the reference with design 1 loses the floor's extra glow of the reference today; design 2 drops the wall highlights under the lamps.
- films-antispinward.png: design 2 puts a lamp's glow on the floor in front of the pillar, where no lamp stands.
- small-scene.png: the small scene's floor crops, rows panel and bulb at 0.05, 0.2 and 0.38, columns before | design 1 | design 2 | the light's lobe alone. Before shows the panel's image plus the disc, and the bulb doubled into an oval; design 1 equals the lobe alone; design 2 shows the panel's image without the disc and loses the reflection at 0.38, where SSR does not run and the map's read misses the lamp.
- Also films-chute.png, films-floor.png, deck-late-deck-late.png, demo-surface-terminal.png, pbr-spheres-front.png.

The cost (timing/runs.txt and summary.txt, timing.sh after ter-eqtb's: per round the builds in a rotated order, base, basecopy (a byte copy of base, the control), d1 and d2, 4 rounds each of time-deck and time-floor, 120 frames each, --video none, the 1-minute load read once the lock was held, from 2.26 to 4.06). Medians in ms:
| window, build | radiance pyramid | environment passes | frame span |
| deck base | 0.510 | 0.981 | 15.347 |
| deck basecopy | 0.510 | 0.978 | 15.449 |
| deck d1 | 0.553 | 0.980 | 15.453 |
| deck d2 | 0.514 | 1.582 | 15.535 |
| floor base | 0.516 | 1.003 | 15.332 |
| floor basecopy | 0.513 | 1.003 | 15.346 |
| floor d1 | 0.555 | 1.001 | 15.402 |
| floor d2 | 0.515 | 1.653 | 15.447 |
Design 1 costs 0.04 ms in the radiance pyramid; its span change stays inside the control's spread (0.01 to 0.10 ms). Design 2's probe costs 0.60 to 0.65 ms in the environment passes.

Step 4, why design 1 follows from the principle alone. The rule is the user's (2026-10-27, and environment-map.md decision 18): an entity that holds a light sends its power through that light, and its glow is the light's face, not a second source. The map and the bounce source already follow it. SSR was the one reader that counted the glow as a second source, so design 1 removes a double count and changes nothing else; in the demo's views it changes nothing visible. Design 2 reverses the user's decision 18, needs the second output that the user declined, removes the lamps' analytic highlights, and without a corrected read of the map counts the lamp zero times wherever a ray misses (the small scene at 0.38: 0.0004 against 0.034 once). So no fork remains.
The cost that the user accepted for the map now holds for SSR too: a lamp whose glowing surface is larger than its light's sphere, such as the cabin panels, shows in a mirror floor as its light's disc on a dark panel. No demo view has a floor below roughness 0.37 under a panel.

The change: radiance_pyramid_first.comp subtracts the glow that the pixel does not send (glowSent, frame.glsl, the rule of the map and the bounce source). Tests, each red first on base (analysis/red.log): PyramidRenderTests.RadianceLevelZeroLeavesOutTheGlowOfAnEntityThatHoldsALight (expected 11.89, found 31.89 before), LampReflectionRenderTests.AGlossyFloorShowsABulbWhoseGlowIsItsLightsSphereOnce (expected 1.542 +/- 3 %, found 3.461), LampReflectionRenderTests.TheScreensReflectionOfALampPanelIsTheMapsDarkPanel (expected below 0.001, found 0.272). Docs: screen-space-reflections.md ("The radiance pyramid", new "A lamp's glow", the kinds of pixel, the tests), environment-map.md (decision 18, the glowSent readers), screen-space-illumination.md ("The bounce source" and its diagram), ARCHITECTURE.md (step 15, "Screen-space reflections", the faces' table).

Checks: nix develop --command dotnet build Tertius.slnx -c Checked (0 warnings, 0 errors); dotnet test --solution Tertius.slnx -c Checked (9,963 passed, 0 failed, 14 skipped); the same with -c Release (9,950 passed, 0 failed, 14 skipped). No reference image changed, so none needed a review. Coverage and mutation testing are the orchestrator's pass.
