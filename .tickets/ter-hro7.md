---
id: ter-hro7
status: open
deps: [ter-2t5o, ter-63wr, ter-a24f, ter-b3h5, ter-gcm3, ter-jxv9, ter-s9cv, ter-zzyr, ter-mgze, ter-nl5u]
links: []
created: 2026-10-25T12:01:26Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, frames]
---
# Each semantic frame sets the sky and ground colors of the hemisphere ambient light

Today the world holds one pair of ambient colors, World.Ambient (SkyColor, GroundColor, Intensity), and the render snapshot copies it each tick. Each instance takes only its up from the gravity field of its own frame (RenderSnapshotBuilder.HemisphereOfFrame and HemisphereOf). So a world that holds a planet's surface and deep space gives both the same sky and ground colors, and the demo works around this by setting the pair per scene. The user decided on 2026-10-25 that each semantic frame determines its own sky and ground colors, as it determines its gravity field and its medium (FrameSpec(Bounds, Gravity, Medium)).

Design to decide (two designs, then the user chooses):
- Where the colors live: a field of the frame beside Gravity and Medium, or a component of the frame's owner.
- Which frame's colors an object takes: its own semantic frame's, or those of the nearest semantic ancestor that sets colors, for example a ship's interior inside a planet's frame. A numeric frame takes its semantic frame's, as it does for its turns.
- What a frame that sets no colors gives, and whether World.Ambient stays as the root's value or goes.
- Whether the colors hold only inside a region, as a uniform gravity field does.
- How the snapshot carries them: per instance (GpuInstance is 80 bytes today) or as a small table per frame that each instance indexes.
- The impostor manager keys a capture's record of the light on the colors, so a change of colors captures again.
- The ambient light's specular part (ter-ptft) uses the same colors as its diffuse part.

The colors are stored state: they describe the last finished step, like the gravity field.

## Acceptance Criteria

In a GPU test, a world with a planet's surface frame that has a blue sky and a brown ground, and a space frame with dim even colors, shades each object with its own frame's colors. The demo scenes set their colors on their frames. ARCHITECTURE.md describes the rule.

