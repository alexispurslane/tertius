---
id: ter-05wb
status: in_progress
deps: []
links: []
created: 2026-11-09T20:22:30Z
type: feature
priority: 0
assignee: Alexis Purslane
parent: ter-8hyd
---
# Aim part of the diffuse rays at bright lit pixels on screen, so rare sunlit patches stop making blotches

Diagnosis 2026-11-09, /private/tmp/tertius-motion-blotches-20261109/: in the bay's moving sunbeam and the cabin walk, 0.13 to 2.1 % of diffuse samples (those at least 8 times the settled light) carry 36 to 86 % of the bounce light. They are rays that happen to hit small sunlit patches about 100 times as bright as the walls. At 0.27 to 0.35 rays per pixel per frame, no history or filter averages them out, and every cheap knob either loses light or gains under 12 %. Decision (parent, under the user's goal of 2026-11-09, which rules out ReSTIR): importance-sample the diffuse rays toward bright lit pixels on screen. Each 2x2 block's diffuse ray is drawn either from the cosine lobe or toward a screen pixel picked in proportion to its directly lit, albedo-weighted outgoing light, from a luminance sum pyramid of this frame (or the reprojected last frame if the order of passes needs it). Each ray is weighted by one-sample multiple importance sampling against both densities, with the pixel-to-solid-angle Jacobian, so the estimate stays unbiased. The existing screen walk gives visibility. No sample is kept or shared between pixels or frames (not ReSTIR). Situation check by the study: Lumen guides screen-probe rays by last frame's light at console 1080p; Reflective Shadow Maps sample the pixels that a light lights; our bright patches are on screen and the direct-light image holds them. Expected cost 0.3 to 0.5 ms with split rays. Loses: bright surfaces off screen are not guided (as today). Acceptance: the study's blotch score (4 px Gaussian, against a long still truth) on the bay frames 300 and 360 and the walk frames 86 and 110 falls far, toward the survey's clean threshold; the indirect light of long still runs stays within 3 % of the cosine-only estimate (no bias); the speck counts of still scenes do not rise; the lamp hall and other scenes show no new artifacts; the GPU cost is measured.

