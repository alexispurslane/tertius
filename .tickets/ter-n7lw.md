---
id: ter-n7lw
status: closed
deps: [ter-t2w6, ter-3yy5, ter-nwhv, ter-o0a6]
links: []
created: 2026-10-26T18:16:23Z
type: epic
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, humans]
---
# Skin and hair shading: a shading model per pixel, screen-space subsurface scattering for skin, and hair cards that do not show

The user added this to the rendering upgrade on 2026-10-26, and said that it closes the epic's scope: 'add to this rendering epic morph targets and skinning and basic screen space subsurface scattering or whatever for skin and whatever we need for the cards not to show up in hair and then that'll be it.' Skin: basic screen-space subsurface scattering (the user chose screen space over a pre-integrated table). Hair: whatever the renderer needs so that hair cards do not show, as in the crew member of ter-1qce (MakeHuman card hair, glTF BLEND). Not in scope, by the user's list: an eye material, two specular lobes for skin, and contact shadows. Design: docs/plans/skin-and-hair.md.


## Notes

**2026-10-27T03:19:37Z**

2026-10-26: main's TransparentPass now destroys its earlier pipelines when a later one fails (ter-uyi3). When this branch merges main, its fringe pipeline takes the same rule: each later creation destroys every earlier pipeline before it rethrows.

**2026-10-31T21:27:38Z**

Closed by the orchestrator (2026-10-31): all 14 children are closed. Coverage of the epic's changed C# lines from c3ee4337 (the main commit before ter-7lg5's merge), on 5d1860ab (ter-uqmn's branch, which holds every child's code): 90.1 % of changed lines and 93.2 % of changed branches over all files touched since then. The epic's engine files: 96 to 100 %; their uncovered lines are the null arms of closed unions, which throw UnreachableException or InvalidOperationException, and the cleanup after a failed Vulkan pipeline creation (ImpostorPass 83-94, ShadowPass 103-106), which no test can provoke. The demo scene builders are the gap (DemoScenes.HairCards.cs 5 of 148, HairRugs.cs 0 of 68, Reflections.cs 1 of 18); ter-5ird adds their script checks. Follow-up tickets outside the epic: ter-a9hh (why a flat hair card hits itself), ter-kr7e (the back-lit fiber's shadow cosine test), ter-5ird.
