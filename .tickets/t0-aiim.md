---
id: t0-aiim
status: open
deps: [t0-jdaq]
links: []
created: 2026-11-13T01:01:07Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Carry the moved mark and the motion bit from the ray plan to the sample age

Design: docs/plans/bounce-light-confidence.md (Design A with Design X's evidence), section 23. Branch t7-3e22-retained-deviation. Step 13, sections 8.4 and 8.5, tests in section 20 (the tracer's bits). The ray plan marks each pixel whose surface moved in the camera's frame (its last place from the motion target against a still point's last place, LeastMotion or 2^-10 of the motion) in the hit light's alpha. A ray takes the mark from its hit's light load into bit 15 of its record's w; the light code narrows to bits 1 to 14 (the diagnostic's field to 14 bits, SeenField 0x2000). The resolve writes the youngest light code and the motion bit (a ray's mark, or the pixel's own motion of at least LeastMotion) into each sample age. Also tools/Tertius.ReflectionReference's decoding.

