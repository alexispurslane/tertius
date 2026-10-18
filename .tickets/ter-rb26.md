---
id: ter-rb26
status: open
deps: []
links: []
created: 2026-10-18T14:08:42Z
type: epic
priority: 2
assignee: Alexis Purslane
tags: [demo, video]
---
# Blue Danube demo: one unbroken run from a launch terminal to a spinning station's wheel

Design doc: docs/plans/blue-danube-demo.md. A long demo video from one scripted run with the real renderer, camera following one character capsule: surface terminal and boarding, launch to orbit and weightlessness, approach to a spinning station, spin match and docking in the axial slot, exit and chute to the wheel. Gray-box meshes, title cards and music added after. Only engine changes need the user's approval (user, 2026-10-18).


## Notes

**2026-10-18T14:14:58Z**

Plan (user, 2026-10-18): the steps that depend only on the rails epic run in workflows parallel to the spin gravity epic. When ter-ozm8 closes, start two workflows at once: the spin gravity epic ter-s9m0 in the main checkout, and the demo steps in their own worktrees on a demo branch (ter-2n31 the pipeline and ter-na4f the surface in parallel, then ter-m13s the launch). The demo work stays in src/Tertius.Demo, scripts/, tools/ and flake.nix, so it does not touch the files of the spin gravity epic. Merge each into main when it is done. Steps 4 to 7 wait for ter-s9m0.
