---
id: t53-yy6p
status: open
deps: []
links: []
created: 2026-11-22T00:18:59Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-kh4g
tags: [rendering]
---
# Speckle on rect-lamps' window reveals under the back-face slab: name its cause

Found in the stills of t1-7b4x on 2026-11-21. In rect-lamps' capture "cabin" after 240 settling frames, at a fixed exposure of 3.341 stops, the reveals of the windows (the side faces of each opening in the wall) lost the white glow that they had under the 10 % slab. The ticket expects that change. But they now show grey speckle and a bright vertical strip, which is new noise and not only the expected darkening. The crop is /Users/alexispurslane/.t3/worktrees/tertius/t3-58f87f59-3970e2671e4b1d5d-evidence/stills/rect-lamps-windows-crop.png (left: the review base 2f18fd829, right: the branch).

Candidate causes, none checked: the walk's misses on slabs of no depth (t53-eyrv), diffuse rays that leave through the opening past the reveal's thin wall, or ray noise that 240 frames do not settle. Probe the reveal's rays (which hit, which miss, and where) before the epic's acceptance stills (t1-8b4w).

