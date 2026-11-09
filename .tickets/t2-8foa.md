---
id: t2-8foa
status: open
deps: []
links: []
created: 2026-11-09T12:22:46Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-icn3
---
# The block ray's turn flips its pixel's column parity with the draw index, as FSR's jitter does

Found in the review of ter-yfb5, not yet measured. In Shaders/screen_trace.glsl, the block ray's chosen pixel is corner = (trace.drawIndex + hash(block) + k) & 3, chosen = 2 * block + (corner & 1, corner >> 1). So the chosen pixel's column parity flips with the parity of the draw index. FSR's Halton base-2 x jitter puts a sample in the right half of its render pixel exactly in the draws of even index. ter-yfb5 showed that a per-pixel choice that flips with that parity does not average out under FSR: the shadow tap quarter made a fixed comb 3 output columns wide. Here each block has a fixed hash phase, so the expected result is a fixed per-block pattern in the shared diffuse rays rather than a regular comb. Reproduce with the setup of UpscalingRenderTests.ASoftSunShadowEdgeShowsNoStripeThreeOutputColumnsWideUnderFsr under ScreenTrace.Default and fully lit ground: compare a 3-column and a 3 x 3 output pattern with the shipped turn and with a turn that hashes the draw index. Fix it only if the measure shows a pattern.

