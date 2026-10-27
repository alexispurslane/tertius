---
id: ter-ylzk
status: open
deps: []
links: []
created: 2026-10-27T15:10:52Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, lights]
---
# The environment map counts a lamp's power twice: its faces add the glow of an entity that holds a light

Found by the review of ter-vtc7 (2026-10-27). The user's rule: an entity sends its power once; an entity that holds a light sends it through that light, and its glowing surface is the face of that light. ter-vtc7 applies it to the bounce light's source. environment_face.frag still adds the emission of every instance, so a light-holding entity's glow goes into the map's irradiance, which lights the room as ambient light on top of the lamp itself. ARCHITECTURE.md and docs/plans/screen-space-illumination.md (Known limits) record it.

Options, for the user:
(a) Keep it as a known limit.
(b) Drop the glow in the faces where the instance holds a light (one line in the face shader). The lamp panel then shows dark in the map's reflections.
(c) Leave the glow out of the irradiance only (a second output of the faces, or an irradiance-only path), so the reflections keep the panel. Costs memory and a change to the faces and the prefilter.

