---
id: ter-hx3r
status: closed
deps: [ter-u6nc, ter-k1ne]
links: []
created: 2026-10-26T19:32:56Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-n7lw
tags: [rendering, materials]
---
# Skin and hair Part 5: the skin scatter

Part 5 of docs/plans/skin-and-hair.md: separable screen-space scatter with d'Eon's profile, SubsurfaceSetting, the two scatter compute passes, independentBlend. No transmission (fork 1 settled as A).


## Notes

**2026-10-27T08:10:34Z**

2026-10-27: started from the epic branch at 7df62c24 (Part 4 and the two-sided hair cards), on branch ter-hx3r-skin-scatter, while Part 4's last review runs. The branch merges back into ter-n7lw-skin-hair.

**2026-10-27T09:39:18Z**

2026-10-27: merged into the epic branch ter-n7lw-skin-hair at 9343ca6d. Skin scatters by the separable screen-space method with d'Eon's profile; two compute passes after the ambient pass; SubsurfaceSetting with 8 taps per side by default; --subsurface on|off; independentBlend required. The scatter output is a shader variant, not a specialization constant, because validation refused an unconsumed output with NoSubsurface. 28 of the 31 timer passes with every setting on. Review findings fixed (d36609cf). Checked 9115 on the merge, 0 failed.
