---
id: ter-kh4g
status: open
deps: []
links: [ter-ukw6]
created: 2026-11-14T18:53:04Z
type: epic
priority: 1
assignee: Alexis Purslane
---
# Back-face depth layer: the screen walk learns each surface's real thickness

Alexis decided on 2026-11-14 to design the real mechanism for ter-ukw6 first: a second depth layer of back faces, so that each screen pixel knows where its object ends, in place of the walk's fixed ThicknessShare slab. The brief, the evidence, the code places, the rejected designs, the points to cover and the acceptance are in docs/handoffs/2026-11-14-renderer-lab.md, part 1. Steps: find the sources and their situations, measure the frame cost with a throwaway back-face pass on the Mac and the RTX 2000 Ada, write two designs with Alexis's questions, then the design doc in docs/plans/ and the tickets.

