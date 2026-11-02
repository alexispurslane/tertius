---
id: ter-g6af
status: open
deps: [ter-s6zr]
links: []
created: 2026-11-02T12:51:56Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, environment-map]
---
# The captures and the cost of the corrected environment read

Ticket 4 of ter-jw7g's design (docs/plans/environment-map-distances.md, 'Ticket 4', 'The measurement plan' and 'The acceptance captures'). Box numbers against ter-f3g2's 64-ray reference (each box within half its loss or 2 levels; the terminal door within 21.5 of 65.5; control boxes worsen by at most 1 level), interleaved timing with a control, and the defaults of MapSearch.Samples and SecantSteps. Writes ARCHITECTURE.md's 'Cost' of the environment map.


## Notes

**2026-11-02T14:23:54Z**

2026-11-02, from ter-0pj8's fix: (1) The near sample t0 = 1/64 stays for every N of the measurement plan, so a pixel reads at most N + K + 1 times. (2) An Illinois step (halve the kept end's clearance when the same end is kept twice) cut the near bracket's edge error from up to about 3 level-0 texels to about 1 to 2.4 in the fixer's Python port, at the same 12 reads. It changes decision 15, so it was not built. Measure it here beside the plain regula falsi, and report the box numbers and the cost of both for the user's choice.
