---
id: t2-vk4s
status: open
deps: []
links: []
created: 2026-11-12T22:18:30Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, lights]
---
# Key and fill darkens the lamp hall's floor by 2.5 %: measure 4 key lights on scenes with many lamps

Found in t2-vsqh. With the default of 3 key lights, the stills of the deck and of the cabin show no change beside every light pair. The hall of 64 lamps is 2.53 % darker as a whole, and 5.5 % of its pixels change by more than 2 steps of 255: the floor loses a part of the soft shine of its far lamps. This is what the model gives up, and it is even, with no step. The trace of the hall with 4 key lights did not end in t2-vsqh (xctrace, t2-d75t), so its cost and its image are not measured for the new corner pass. Version 2 measured a blotch of 2.09 % with 4 key lights.

## Acceptance Criteria

The hall's image measures and its trace with 4 key lights are recorded beside those of 3. The default changes only if the user decides so.


## Notes

**2026-11-13T00:53:51Z**

The image with 4 key lights is measured (t2-vsqh, stills at 1920 × 1080, one fixed exposure). The hall's level is −2.47 % with 4 key lights and −2.53 % with 3. Its blotch is 2.28 % against 2.27 %, and 5.25 % of its pixels change by more than 2 steps of 255 against 5.52 %. The deck and the cabin are the same with 3 and 4. So the count of key lights is not the cause. The floor's loss is the sum of the soft shine of about 60 far lamps, each of which loses too little at a corner to be a key light. An idea for the model: a fill response that carries the lamp's mean specular light, so that the fill keeps the energy and loses only the highlight's shape.

**2026-11-14T15:33:46Z**

The default is 4 key lights since 2026-11-14 (t2-vsqh). The orchestrator's rule was the cost alone, because the stills of 3 and of 4 look the same: on the Mac, the hall's corner, tile and direct passes took 3.48 ms with 3 key lights and with 4 (take 8, traces at a normal GPU clock), and on the RTX 2000 Ada the frame spans lie within 0.01 ms of each other. On the merged code, the hall's level is −2.47 % with 4 key lights and −2.52 % with 3. A crop of 480 × 320 pixels of the floor has a mean of 111.03 against 112.74 of 255 with 4 key lights, and a mean FLIP of 0.049. So the measures of this ticket's acceptance are recorded. What stays open is the floor's loss itself, and the idea of a fill response that carries the lamp's mean specular light.

**2026-11-14T15:44:35Z**

The cause is measured, and the ticket t2-kibe holds the repair (a cheap specular fill). With every lamp's specular light in full and the diffuse light by key and fill, the hall's still equals every light pair within 1 step of 255. So the floor's loss is the missing specular light of the fill lamps, and nothing else.
