---
id: t2b-d2m6
status: open
deps: []
links: [ter-4iic, ter-6fka, t1-mybi, ter-13rm]
created: 2026-11-22T20:08:08Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering]
---
# A glossy wall beside a spot-lit block shows a bright patch that the path trace lacks: the environment map at the eye holds the lit spot

Found by t1-r9vj (docs/research/back-face-depth-layer/stage-2/fade-lit-front.md), on main 906d1c260 and in every column of the probe.

The scene: the throwaway scene lit-block-front of refs/scratch/t1-r9vj-lit-block (patch docs/research/back-face-depth-layer/stage-2/probe-lit-block.patch). A closed gray room, a block 2.4 x 1.6 x 1.5 m, a spot lamp that lights a disk on the block's face toward the camera, and a glossy wall (roughness 0.2) 0.8 m beside the block.

What an eye sees: a bright trapezoid on the glossy wall beside and behind the block, with a hard lower edge. The Cycles path trace of the same frame shows an even wall there. Crop: docs/research/back-face-depth-layer/stage-2/lit-front-glossy-wall.png (the columns, then the path trace).

Measurements (frame 239, mean luminance over the lit floor patch of the same image): the glossy wall beside the block reads 0.158 in main and 0.146 to 0.158 in every rule for a blocked ray's light. The path trace reads 0.098 with all bounces and 0.003 with one bounce. So the engine puts more light there than all bounces of the truth.

With --environment-map off, the patch is gone, in layer and in dblack05: docs/research/back-face-depth-layer/stage-2/lit-front-glossy-map-off.png (layer with the map, layer and dblack05 without it).

Reading, not measured: the environment map is one capture at the eye, and it holds the spot's lit disk. A ray from the wall that misses, or whose hit fades toward the miss light, reads the disk from the map without parallax.


## Notes

**2026-11-22T20:29:37Z**

2026-11-22, the line orchestrator, from the orchestrator. This is a visible bug by the user's bar, because the path trace has an even wall. It stays a ready ticket under ter-8hyd. The back-face line does not take it: it belongs to the environment maps. Which map holds the lit spot, from what the take knows: the scene has one root frame, the room (probe-lit-block.patch, world.CreateRootFrame), so it is the map of the camera's frame, captured at the eye. Whether box projection is on in that scene was not checked.
