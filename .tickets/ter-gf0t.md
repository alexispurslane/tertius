---
id: ter-gf0t
status: in_progress
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

**2026-11-02T09:33:10Z**

2026-11-02: workflow run ef5ed419-a69e-4059-82a0-58a663d0aa83 (version 869b0b07), branch ter-gf0t-sphere-light-lobe from 47abec35b (main after the ter-8bda merge).
