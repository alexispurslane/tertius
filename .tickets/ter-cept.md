---
id: ter-cept
status: open
deps: []
links: []
created: 2026-11-12T07:03:50Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
---
# A headless still inside the station's cabin is black whenever the environment map is on

Sweep addendum (/private/tmp/tertius-sweep-20261111/addendum.md) A11. scripts/station.json capture crate-at-wall is all zeros. Only stills (a clock step of at least 0.09 s: history reset, 32 warm-up draws, every environment face refreshed in the first draw) are black, and only with the environment map on; continued draws show the cabin (in blue light from the environment map, which is a second oddity for a closed cabin). Not caused by the crate's launch, screen trace, bloom, upscaling, fast math or the exposure (black at a fixed +0.5 stops too). Other cameras and the cabins of four other scripts are fine. Reading, unmeasured: a non-finite value reaches every pixel, and MoltenVK's clamp turns NaN into 0; a Linux driver can show something else, so run this script on the cloud workbench once. First step: in the first draw of a still, find the first image of the frame that holds a non-finite value. Repro: /private/tmp/tertius-sweep-20261111/refl/capscripts/station-nocrate.json (sixth capture is the first black one).

