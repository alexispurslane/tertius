---
id: ter-7ul9
status: closed
deps: []
links: [ter-imi5]
created: 2026-10-17T05:07:23Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, contacts]
---
# A kinematic sensor detects a moving hull's ghost but not its interior, and keeps a pair after the hull's driver turns static

From the fixes of the contact review (2026-10-17): the kinematic ghost of a moving hull (a Kepler or physics-driven hull) is detected by a kinematic sensor in a sibling frame, but the same sensor does not detect the hull's static interior inside the hull's own frame. Decide what a sensor detects of a frame owner, the same way in every frame. Also: if a hull's driver turns Static while its kinematic ghost already touches a kinematic or static sensor, Jolt keeps that existing pair, because the filter of ghosts of static bodies acts only on added contacts. The pair lasts until the bodies separate.

