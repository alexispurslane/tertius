---
id: ter-b2da
status: open
deps: [ter-jw7g, ter-0pj8, ter-55xu, ter-s6zr, ter-g6af, ter-pn1w, ter-nw4a, ter-cvyx, ter-ipc1]
links: []
created: 2026-11-01T16:01:19Z
type: epic
priority: 1
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, reflections, screen-space-light]
---
# Screen-space artifacts: correct reflections and screen-space light before more optimization

Design: docs/plans/screen-space-artifacts.md. The user decided on 2026-11-01 to pause the performance epic ter-pq5y and fix the artifacts of the reflections and the screen-space light first, because the fixes change the starting point of the optimization. Each effect gets a diagnosis ticket that attributes every artifact to its stage before any fix is designed.

## Acceptance Criteria

Demo-exit's nine films and the review scenes of both effects show no artifact that comes from a fault of the implementation. Each remaining limit of a screen-space method is recorded in ARCHITECTURE.md with the scene that shows it.


## Notes

**2026-11-02T10:29:52Z**

2026-11-02, for the user's decision before this epic closes. ter-gf0t's build (run ef5ed419) measured that the widened lobe of a sized light gives back none of the reflected brightness that the SSR limit of 0.37 removed: -1.8 % to +1.2 % of it in each box. Against ter-f3g2's 64-ray reference (g/lobenorm), the image at 0.37 loses on the mean of each box, in levels of 255: terminal door 42.9, brake-run teeth 17.9, floor film lamp cut 15.6, chute-top slide 14.4, chute ceiling 8.4, terminal floor 7.1, antispinward edge 6.5, floor film steps 3.4, unbelt floor 3.2, and the deck 0.2 (captures/gf0t/analysis/glow.txt in the main checkout). The deck, the decision's test case, matches the reference, but the other views do not: that brightness is the reflection of lit geometry and emissive strips, which the environment map does not hold at those places and which no light's specular term holds. The epic waits for the user to choose: accept the loss and close the epic, or open a ticket for another mechanism.
