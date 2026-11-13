---
id: t8-ol7x
status: closed
deps: []
links: [t8-pkag]
created: 2026-11-12T21:48:41Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: t8-pkag
tags: [rendering, linux]
---
# The light-age diagnostic finds receivers with a hit distance of 0 and a hit on NVIDIA

LightAgeHistoryTests.TheDiagnosticGivesEachReceiverTheFramesSeenOfTheUnchangedSurfaceThatItsRaysHit fails on the RTX 2000 Ada: about 30 receivers, in pairs of one block, have a specular sample with alpha (the hit distance) 0, but their rays hit a surface seen for 31 frames.


## Notes

**2026-11-12T22:13:32Z**

Kind (a). The diagnostic on the card (diag3) put the hit pixel and the hit distance into the record: the failing receivers are the emitter's own pixels. The test's glow material has roughness 0.5, and the G-buffer stores roughness as RGBA8 unorm. 0.5 × 255 = 127.5 lies halfway between the codes 127 and 128, and Vulkan lets a driver store either (spec, 'Conversion from Floating-Point to Normalized Fixed-Point'). The Mac stores 128, 0.502, above MaxRoughness 0.5: the emitter traces no reflection ray, and its sample's hit distance comes from its diffuse ray. NVIDIA stores 127, 0.498, inside the hand-over band: the emitter traces a reflection ray of weight 0.02, which misses and writes a hit distance of 0, while the seen code comes from the diffuse ray that hit. The engine works as designed on both. Fix: BrdfTesting.Glow takes the roughness 128 / 255, the code that the Mac stores, so no image on the Mac changes and every driver stores the same code. No test can be red on the Mac, because the Mac never makes NVIDIA's choice.

**2026-11-12T22:55:21Z**

Card confirmation on a7924ebd7 (2026-11-12): the fixed Rendering methods 10 of 10, the fixed classes 80 of 80 and the Host edit classes 40 of 40 pass on the RTX 2000 Ada. The Mac's Checked and Release suites pass.
