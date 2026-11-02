---
id: ter-gf0t
status: open
deps: [ter-8bda]
links: []
created: 2026-10-26T18:21:30Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, lights]
---
# A sized light's highlight: the widened GGX lobe of a sphere light

Decision 47 of docs/plans/local-light-shadows.md. Every light now has the radius of its emitting sphere (PointLightData.Radius, SpotLightData.Radius), which the PBR design waited for (docs/plans/pbr-brdf.md, "Compared with a design from scratch"). A smooth surface still shows a pinpoint highlight of the Sun or of a lamp's bulb, and the clamp bounds the stored value. Widen the GGX lobe of brdf for a sphere light by its angular radius, as Karis 2013 does (the representative point and the normalization of the widened lobe), so that a mirror shows the light's disc and the radiance stays bounded. It is a change of the BRDF with its own tests: the energy of the widened lobe, a disc of the right angular size in a mirror, and no change for a light of radius 0.

## Acceptance Criteria

A smooth sphere under the Sun and under a lamp shows a highlight of the light's angular size; the widened lobe's energy matches its point-light lobe within the tests' bounds; the tests pass in Checked and Release; ARCHITECTURE.md, "Shading", describes the lobe.


## Notes

**2026-10-26T20:35:11Z**

2026-10-26: taken out of the rendering upgrade epic. The user closed that epic's scope on 2026-10-26 with skinning, morph targets and skin and hair shading ('and then that'll be it'), and decision 47 of local-light-shadows.md keeps this lobe out of the lights epic. It waits for the user to ask for it.

**2026-11-02T02:28:35Z**

The user's decision of 2026-11-01, after ter-f3g2's probe: SSR's roughness limit goes to 0.37 now, inside epic ter-b2da; below it, today's method (one ray with a cone) stays; stochastic SSR (ter-rnjk) is not built; ter-gf0t moves into the epic to give back part of the lamp glow on rough surfaces that lose SSR. Reason: the probe showed that stochastic SSR helps only between roughness 0.37 and 0.6, which the 0.37 limit removes from SSR anyway, at +3 to +6 ms measured and with grain that never settles, while the limit saves 1.65 to 2.0 ms and matches the 64-ray reference on the deck paint. So this ticket moves into ter-b2da after ter-8bda. Added acceptance: captures with the 0.37 limit of the rough surfaces that lost SSR (the chute slide on chute-top, the terminal door on demo-surface, the brake-run floor) beside ter-f3g2's 64-ray reference (captures/f3g2/ in the main checkout), which show how much of the lamp glow the widened lobe gives back.

**2026-11-02T10:27:46Z**

Builder, 2026-11-02, branch ter-gf0t-sphere-light-lobe from 47abec35. Evidence: captures/gf0t/ in this worktree (README.txt lists every file; git ignores captures/, so the orchestrator copies it to the main checkout).
- The change: specularResponse in lighting.glsl, the one place for every caller of directLight (the direct pass, the transparent pass, the impostor capture, the environment faces), shades each light as a sphere of sin ϕ = min(R / d, 1) (lightSineAt). l is Karis 2013's representative point (page 15, equation 11; the notes print the vector toward the ray with the wrong sign, the code uses (L·r) r − L as Unreal Engine 4 does), and D takes (α / α′)² (page 16, equation 14) with α′ = min(α + sin ϕ / (2 √(v·h)), 1). The diffuse part stays the point light's (a sphere above the horizon gives a point's irradiance, Karis page 15). The lit puffs have no lobe. A hair fiber shades every light as a point. A light of radius 0 takes the old operations. The Sun's radius already lives in its light (DemoScenes.SunRadius, 6.957 × 10⁸ m, at 1 AU: sin ϕ = 0.00465), so no new value was needed. C# copies: GgxLobe.SphereLightDirection, GgxLobe.SphereNormalization, MaterialSample.ReflectedTerms (lightSine, 0 by default).
- Why not Karis's α + sin ϕ / 3 (equation 10): over every view direction the paper's form reflects 1.32 to 2.26 times the point lobe's light on a mirror lit head-on and up to 8.1 times at a light 1.3 rad from the normal. α + sin ϕ / 2 is exact head-on but reaches 3.7 times at grazing light. The built form, the half vectors' cone of the light's disc, keeps 0.73 to 1.0 everywhere in the grid (α to 0.6, sin ϕ to 0.3, the light to 1.3 rad), never adds light, and makes a mirror show the light's own surface radiance (analysis/energy.txt; pbr-brdf.md, "Sized lights", has the table).
- Tests, red first: SphereLightLobeTests (CPU): ASizedLightReflectsBetween85And100PercentOfTheLightOfAPointLight (36 cases), AMirrorShowsASizedLightNoBrighterThanTheLightsOwnSurface (9 cases; the point lobe measured up to 1,682 times the light's surface), AMirrorShowsALargeLightAtNearlyItsOwnSurfaceRadianceInsideItsDisc (3 cases). SphereLightRenderTests (GPU): AMirrorShowsTheDiscOfASizedLightAtTheLightsAngularSize (sun, lamp; red: the lamp's disc measured 0.0017 rad against 0.031), ALightOfRadiusZeroShadesExactlyAsThePointLightDidBeforeTheWidenedLobe (a new reference image approved on 47abec35's shaders, tolerance 0; it passes before and after by design). BrdfShaderTests' theory gained a sun 0.2 rad wide. The paper's constant fails 35 of the 48 CPU cases and 4 GPU cases, α + sin ϕ / 2 fails 16 CPU cases.
- Changed test: BrdfRenderTests.ASmoothMetalReflectsTheSunWithTheLobeOfTheRoughnessFloor passes the test star's sine to the C# side. No reference image changed: all 2,297 Rendering tests passed with the existing six references, so none was re-approved. The new reference sphere-light-radius-zero.png shows two rows of spheres (metal over non-metal, roughness 0 to 1), a glass sphere and a floor of roughness 0.1, with pinpoint highlights of the radius-0 star, lamp and spot, as its test describes.
- Views (analysis/after-vs-before.txt, where.txt): every change is a lamp's or the Sun's highlight on a smooth part. The largest: unbelt 1.14 % of the pixels above 4 levels, mirror-balls/balls 1.26 %, pbr-spheres 0.4 %, the films' deck and deck-late about 0.1 % (the ceiling panes' hot lamp points become soft discs, sheets/deck-lamps-2x.png), the terminal 0.08 % (the ceiling lamps, sheets/terminal-lamps-2x.png), reflections and mirror-balls/plate none above 4 levels. sheets/shot-mirror-lamp.png: a 3-pixel dot becomes the lamp's disc. sheets/shot-mirror-sun.png: the floored lobe's peaked blob becomes the Sun's flat disc with a soft edge. sheets/shot-spheres-lamp-3x.png: roughness 0 to 0.1 show the lamp's image at one size, 0.2 and 0.38 barely change.
- The lamp glow on the surfaces that lost SSR (analysis/glow.txt, ter-f3g2's boxes): the widened lobe gives back none of it, −1.8 % to +1.2 % of the lost glow (reference minus the 0.37 gallery) in every box: chute-top slide −0.23 of 13.8 levels, terminal door −0.01 of 40.9, floor lamp cut −0.09 of 15.9, brake-run teeth −0.10 of 18.3, chute ceiling +0.04 of 8.0. Sheets with the reference: chute-top-slide-ref-1x.png, terminal-door-ref-2x.png, floor-lampcut-ref-2x.png. Why: the lobe keeps the point light's energy and only reshapes the highlight, and on roughness 0.38 a lamp of sin ϕ ≤ 0.05 changes the lobe little. The glow that SSR gave came from reflected lit surfaces and emissive strips (the doorway, the lamp fixtures), which no light's specular term holds.
- Cost (timing/summary.txt): 7 interleaved rounds per window in a rotated order of before, before2 (a byte copy, the control) and after, each under the GPU lock, 1-minute load 4.0 to 7.2 (deck) and 3.2 to 6.0 (floor). Paired medians against before: direct light pass +0.29 ms on the deck (3.21 to 3.46 ms, control −0.00) and +0.54 ms on the floor (3.92 to 4.51 ms, control −0.03); transparent pass +0.16 ms on the deck; frame span +0.30 ms on the deck (control +0.04) and +0.37 ms on the floor (control +0.06).
- Docs: pbr-brdf.md "Sized lights" (new), "Lights", "Compared with a design from scratch", the C# BRDF and the tests; local-light-shadows.md decision 47 and its two table rows; ARCHITECTURE.md "Shading" (respond and a paragraph "Sized lights").
