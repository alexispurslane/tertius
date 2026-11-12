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
