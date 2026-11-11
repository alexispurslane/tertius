---
id: ter-gi1v
status: open
deps: [ter-sfsb]
links: []
created: 2026-11-10T13:25:05Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# The lobe fit's shared reflection rays show a grain on glossy floors seen at a low angle

Ticket ter-4oow shares a block's reflection ray with each pixel whose reflection lobe holds it. On a flat glossy or rough floor every pixel shares, so the reflection takes one ray per 2 x 2 block. The rough reflection plate's temporal noise rose from 5.53 to 14.02 W/(m² sr), and the smooth plate's from 1.00 to 2.85. The grain shows at normal viewing distance in the still reflections-low (clearly) and reflections-front (faintly), in the floor's reflections of the red box and the blue ball. Images: /private/tmp/ter-4oow/decision/reflections-low-crop.png and reflections-low-side.png. Alexis accepted it on 2026-11-10 for the deck's 7 ms.

## Design

Candidate: a checkerboard of own rays. Each rough pixel that shares traces its own ray every second draw, half of the pixels in each draw, so a glossy floor takes about half a ray per pixel per draw instead of a quarter. Measure its cost on the deck's reflection rays (2.65 ms with the lobe fit, 9.65 ms without) and its noise on the plate, once the plate test measures ray noise again.

## Acceptance Criteria

The rough plate's noise and the grain in reflections-low fall measurably, at a reflection-ray cost on the deck that the user accepts.

