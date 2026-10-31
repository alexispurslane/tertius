---
id: ter-kr7e
status: open
deps: [ter-uqmn]
links: []
created: 2026-10-31T21:15:53Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [rendering, hair, shadows, tests]
---
# Test that a back-lit hair card keeps the shadow of a nearby caster under the sun's cascades

Found by the hand mutations of ter-uqmn (2026-10-31). In modelLight (lighting.glsl), a fiber passes abs(geometricNdotL), the cosine of its geometric normal turned toward the light, to lightVisibility (ter-k1ne, decision 27 of docs/plans/skin-and-hair.md). The mutant that passes the raw, negative cosine survives every test: HairRenderTests.AHairCardLitFromBehindDoesNotShadowItself, which the fixer named, still passes. The reason: tileShadow uses only sinTheta, which is even in the cosine, and sampleCascade uses the cosine only in the receiver-plane slope, slope = -n.xy / max(n.z, MinimumReceiverCosine = 0.1), added per tap as max(dot(slope, offset), 0). So a negative cosine takes the floor 0.1 and only makes the slope bias larger (about 0.37/0.1 against 0.37/0.93 for the test's sun), which can only make taps more lit. The card's own lighting does not change, but a caster close behind the card (toward the sun) loses part of its shadow on the card: light leaks. The mutant is not equivalent. The work: a render test with a back-lit hair card and an opaque occluder a few cascade texels behind it toward the sun, at a gap between the true slope bias and the floored one, that expects the card's pixel ahead to stay in the occluder's shadow. Watch it fail with the mutant (the raw cosine) and pass with abs. Pick the gap from the cascade's texel size, not by trial, and state the arithmetic in the test's comment.

