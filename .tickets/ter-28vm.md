---
id: tfa-28vm
status: open
deps: []
links: []
created: 2026-10-18T16:21:33Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering]
---
# Decide whether an invisible entity casts a shadow

Found by the ARCHITECTURE.md drift check (ter-foin, 2026-10-18). The two halves of the renderer disagree. RenderSnapshotBuilder.cs:311-317 skips instances without the Visible flag, and RenderSnapshotTests.AnEntityWithAMeshButWithoutTheVisibleFlagDrawsNothing pins that. But RenderFlags.CastsShadow and ShadowPass.cs:103 say that an invisible caster still casts. ARCHITECTURE.md now says what happens today: an entity without Visible casts no shadow. Decide the rule, then make both halves and the doc agree.

