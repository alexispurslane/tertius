---
id: ter-xlp8
status: open
deps: []
links: []
created: 2026-10-27T06:49:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-n7lw
tags: [rendering, materials]
---
# Skin and hair: draw hair cards from both sides

A hair card seen from behind draws nothing in any pass that shades. The G-buffer, fringe and environment-face mesh pipelines keep the default CullMode BackBit (src/Tertius.Rendering/Vulkan/GraphicsPipelineDesc.cs). Part 4 makes a hair pixel take light from either side of its card (decision 2), and MPFB's hair cards are authored to be seen from both sides, such as the cards at the back of a head. The cascades and the atlas cull nothing, so such a card still casts a shadow.

Confirmed on 2026-10-27 at the Part 4 review: a PlusV hair card turned to face away from the camera (its normal -Z), ambient light only, left the middle pixel at the background's (0, 0, 0).

The design does not mention double-sided cards, so this is a question for the user before it is built: should hair materials draw double-sided? A sketch of one answer: CullMode None for the hair materials' runs, with gbuffer.frag turning the geometric normal toward the eye. Settle it before Part 6's demo scene (ter-3dp5).

## Acceptance Criteria

The user has chosen whether hair cards draw from both sides. If yes, a hair card seen from behind shades as the same card seen from the front, in every pass that shades, and a test pins it.

