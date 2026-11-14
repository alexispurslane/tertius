---
id: t0-adau
status: open
deps: []
links: []
created: 2026-11-14T15:56:48Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# The rect-lamps ceiling shows single bright pixels near the far lamps; find their cause

Filed 2026-11-14 at the orchestrator's request. The long reference (clamps off, a long run) shows the same specks, so they are not a fault of the history or its clamp. Hypothesis to test first, stated as a hypothesis: a ceiling point's bounce ray that hits the glossy floor reads the floor's shaded light as the camera sees it, including the lamps' bright reflections in the floor. That light depends on the view direction, and the ceiling point does not receive it from that direction. Cheap test: count specks in a rect-lamps still with the hit light's specular part left out, against main and against the unchanged branch, on one machine. No fix this week unless the test is clear and the fix is small.

