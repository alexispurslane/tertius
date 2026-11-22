---
id: ter-ewua
status: open
deps: [t7-3e22]
links: []
created: 2026-11-12T02:59:35Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# Design: accumulate and filter bounce light once per 2 x 2 block, as one design with the noise value stored beside the slow light

Sweep row 7 (/private/tmp/tertius-sweep-20261111/report.md, section 7), approved by the user on 2026-11-11 as a design ticket. Expected gain on the deck: 1.5 to 1.9 ms. Rays are already one per 2 x 2 block, but the diffuse filter (4.4 ms), the specular accumulation (1.47 ms) and the diffuse accumulation (0.60 ms) run per pixel. It changes the store and the filter that the mottle fix's Design A extends (a deviation stored with the slow light, chosen by the user on 2026-11-11), so it starts after Design A lands and is designed as one model with it.


## Notes

**2026-11-12T12:17:41Z**

User decision 2026-11-12: this ticket stays one design with Design A (t7-3e22). Design A's doc defines the retained deviation and the 8 x 8 block test in units of independent ray samples, so that the store can move to 2 x 2 blocks without a second model.
