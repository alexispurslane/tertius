---
id: ter-3dp5
status: closed
deps: [ter-hx3r, ter-xlp8]
links: []
created: 2026-10-26T19:32:57Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-n7lw
tags: [rendering, materials]
---
# Skin and hair Part 6: the cost, the captures and the docs

Part 6 of docs/plans/skin-and-hair.md: the skin-and-hair demo scene, the cost at 1080p, captures, ARCHITECTURE.md.


## Notes

**2026-10-27T13:38:52Z**

2026-10-27: the epic merged into main (32077dc1); full Checked on the merged main: 9,270 total, 0 failed. Coverage of the epic's changed lines against main: engine code 1,123/1,159 (96.9%); the misses are cleanup after a failed pipeline creation, a guard for an unsupported device and unreachable arms. The demo scene code (DemoScenes.SkinAndHair.cs, HairCards.cs) has no tests, as the lean demo process allows. The merge moved --subsurface into the RendererOverrides of the options union (ter-2k3b). Captures sent to the user.
