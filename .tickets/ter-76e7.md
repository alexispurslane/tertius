---
id: ter-76e7
status: closed
deps: []
links: []
created: 2026-11-04T22:27:33Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
---
# Remove the C# copies of the removed ambient estimate: AmbientTerms.Occluded and the estimate of MaterialSample.ReflectedHemisphere

**2026-11-25 · Retired.** Defer the internal cleanup. The new film work has no dependency on this representation or its removal.

The screen tracer (ter-gayt) removed occludedAmbient and the estimate target from the shaders. AmbientTerms.Occluded, GgxLobe.SpecularOcclusion and the occlusion and bounce parameters of MaterialSample.ReflectedHemisphere still copy them in C#, and AmbientTermsTests, GgxLobeTests, MaterialSampleTests, BrdfShaderTests and BrdfRenderTests test them. Remove the copies, or keep a part that a test of a live shader needs, and say which.

