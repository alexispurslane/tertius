---
id: ter-gf0t
status: open
deps: []
links: []
created: 2026-10-26T18:21:30Z
type: feature
priority: 3
assignee: Alexis Purslane
tags: [rendering, lights]
---
# A sized light's highlight: the widened GGX lobe of a sphere light

Decision 47 of docs/plans/local-light-shadows.md. Every light now has the radius of its emitting sphere (PointLightData.Radius, SpotLightData.Radius), which the PBR design waited for (docs/plans/pbr-brdf.md, "Compared with a design from scratch"). A smooth surface still shows a pinpoint highlight of the Sun or of a lamp's bulb, and the clamp bounds the stored value. Widen the GGX lobe of brdf for a sphere light by its angular radius, as Karis 2013 does (the representative point and the normalization of the widened lobe), so that a mirror shows the light's disc and the radiance stays bounded. It is a change of the BRDF with its own tests: the energy of the widened lobe, a disc of the right angular size in a mirror, and no change for a light of radius 0.

## Acceptance Criteria

A smooth sphere under the Sun and under a lamp shows a highlight of the light's angular size; the widened lobe's energy matches its point-light lobe within the tests' bounds; the tests pass in Checked and Release; ARCHITECTURE.md, "Shading", describes the lobe.


## Notes

**2026-10-26T20:35:11Z**

2026-10-26: taken out of the rendering upgrade epic. The user closed that epic's scope on 2026-10-26 with skinning, morph targets and skin and hair shading ('and then that'll be it'), and decision 47 of local-light-shadows.md keeps this lobe out of the lights epic. It waits for the user to ask for it.
