---
id: ter-8esk
status: open
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
