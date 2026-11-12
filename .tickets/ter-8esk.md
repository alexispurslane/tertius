---
id: ter-8esk
status: closed
deps: []
links: [ter-cgif, t8-pkag]
created: 2026-11-12T03:15:00Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [linux, rendering]
---
# The radius-zero sphere light's reference image fails on NVIDIA by 16 pixels

`SphereLightRenderTests.ALightOfRadiusZeroShadesAsThePointLightDidBeforeTheWidenedLobe` fails on the L4 pod of ter-libf (NVIDIA driver 595.91.07, 2026-11-12): 16 pixels have a FLIP value above 0.1, the largest 0.119, the mean over the image 0.00298. The reference was approved on MoltenVK. I looked at the reference, the actual image and the heat map: the two images look the same, and the only visible spot of the heat map is the specular highlight of the second dark sphere of the top row. It looks like a difference of precision in a sharp highlight, not wrong shading. Decide with the other Linux drivers of ter-cgif: a tolerance that holds on every driver, or a reference per driver. The three images: /private/tmp/ter-libf/l4-out/ref/sphere-light-radius-zero*.png.

## Notes

**2026-11-12T21:52:41Z**

Kind (a), a reference image that one driver recorded. Reference, actual and FLIP heat map (diag2 on the RTX 2000 Ada, 2026-11-12): the two images look the same; the heat map is nearly black, with one faint spot on the highlight of the second dark sphere of the top row. 16 pixels are above 0.1, the largest 0.119. Fix: a snapshot test on a driver other than MoltenVK compares with its own reference, <name>.<driver>.png, where one exists (ReferenceImages.ReferencePath); approval on that driver writes it. The tolerance stays 0.1. The NVIDIA reference sphere-light-radius-zero.nvidiaproprietary.png is approved from the card's image after review.

**2026-11-12T22:13:32Z**

2026-11-12, the orchestrator's check before approval (diag4). The radius-zero path has no unguarded division: lightSineAt gives 0 / max(d, 1e-6) = 0, specularResponse takes the point light's branch, asin(0) = 0, and farShadowVisibility floors the angular radius at 1e-5 before discCoverage divides by it. The card's HDR target and normals hold no NaN or Inf (230,400 texels each). But the difference is not spread-out rounding: 18,055 pixels differ by 1 level all over the image, and one compact cluster of about 36 pixels, the highlight of the second sphere of the top row (metal, roughness 0.1) at (265 to 273, 153 to 156), is up to 10 levels darker. Its HDR red on the card is 0.867 to 0.90 times the Mac's, lowest at the brightest pixels (Mac 6.49, card 5.64), while the normals agree to one 16-bit code (3.1e-5). A smooth factor inside the light's highlight is not rounding, so this stays a suspected kind (b): no NVIDIA reference is approved. Next: print the direct pass's specular target, the material factor and the pre-exposure at those pixels on both machines, to find the stage that loses 13 %.

**2026-11-12T22:39:03Z**

Suspects from the orchestrator (2026-11-12), hints only. (1) Precision of the GGX peak at roughness 0.1 (alpha² about 1e-4). ggxDistribution already computes the denominator as |n × h|² + (n·h)² α², so the cancellation of 1 − (n·h)²(1 − α²) is out. A RelaxedPrecision or float16 path on the way is still possible: check the SPIR-V of the direct pass for RelaxedPrecision on that path. (2) The BRDF tables and the LTC table are sampled with bilinear filtering, whose weight precision (subTexelPrecisionBits) differs by vendor, and their gradient is steep at low roughness. Printing the direct pass's specular target and the material factor at the cluster's pixels on both machines separates (1), which shows in the direct target, from (2), which shows in the factor.

**2026-11-12T23:22:27Z**

Cause found (2026-11-12, three probes on both machines). The material factor differs by 0.15 % and the shadow visibility not at all; the raw direct specular is 13 % lower on the card, and by the same factor for each of the three lights (0.855, 0.866, 0.873). So the loss sits in what the lights share: the surface's roughness. The sphere's roughness 0.1 times 255 is 25.5000004, and the G-buffer's specular target was RGBA8 UNORM. Vulkan lets a float-to-UNORM conversion return either of the two nearest codes. Apple's store took 26 and NVIDIA's 25. In the tail of a sharp lobe D grows with alpha² = r⁴, and (25/26)⁴ = 0.855. Kind (b): our target left a visible choice to the driver. Fix: the specular target is RGBA8 UINT. gbuffer.frag writes floor(x × 255 + 0.5), with a correctly rounded multiply and add and an exact floor, so every driver writes one code, and specularAt reads code / 255. The shared Mac reference holds: the card passes SphereLightRenderTests with no NVIDIA image (20 of 20 with SpecularTargetTests and ShadingTargetTests). SpecularTargetTests was red on the Mac first for 0.7 and 0.9, where the UNORM store took the other code.

**2026-11-12T23:26:26Z**

Checks before the merge (orchestrator, 2026-11-12). No blend: GraphicsPipelineDesc.RefuseBlendIntoAnInteger throws at pipeline creation for any blended pipeline with an integer attachment, and the g-buffer, cutout and impostor pipelines use BlendMode.None. No filtering: gbufferSpecular is a utexture2D without a sampler, and its one reader is specularAt, a texelFetch (accumulate_temporal.comp, surface_test.comp, gbuffer_surface.glsl, accumulate.glsl). The target has one mip and one sample, and nothing resolves, copies or gathers it but the test hook's readback.

Follow-up list, not part of this merge: other G-buffer and texture channels where a driver may take either of two codes near a shading decision.
- Base color, R8G8B8A8_SRGB: the store may take either code nearest to a constant. It sets the diffuse color and a metal's F0, so a code apart moves a metal's highlight by under 1 %. The map code in its alpha is already guarded (mapCode, gbuffer.frag).
- Normals, R16G16B16A16_SNORM: either code, 3.1e-5 per step. At the roughness floor (0.045, alpha² = 4.1e-6) a step moves the tail of D by a few percent. The dump showed the two machines one code apart here.
- Material textures, R8G8B8A8_UNORM and _SRGB, sampled with linear filtering: the filter weights' precision (subTexelPrecisionBits) is the vendor's, which moves a textured roughness near MaxRoughness across the hand-over band of the reflection rays (start 0.4, end 0.5).
- FSR 3.1's reactive mask, R8_UNORM: a halfway value may land on either code.
- The decisions that read roughness, the hand-over band and startsKind (ScreenTrace.MaxRoughness), now read exact codes from the UINT target, so a constant roughness falls on one side on every driver.
