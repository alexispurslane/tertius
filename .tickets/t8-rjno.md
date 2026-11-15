---
id: t8-rjno
status: open
deps: []
links: []
created: 2026-11-15T12:54:02Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering, shadows]
---
# Sunlight leaks onto the outer quarter of rect-lamps' side window jambs: 0.37 W/(m² sr) of direct light where the wall above blocks the sun

Found by t1-g4p1 (docs/research/back-face-depth-layer/stage-2/reference.md, part 3). In rect-lamps (scripts/rect-lamps.json, the person's camera, film frame 239, output 1920 x 1080, fixed exposure 2.7868438 stops), the far jamb of the nearest right side window shows a bright vertical strip on its outer quarter, output x 1799 to 1809, rows 500 to 580. With --screen-trace off the strip stays: 0.008 W/(m² sr) at x 1799 and 0.374 at x 1808, against 0.007 on the rest of the jamb. With the sun removed (probe switch TERTIUS_NO_SUN=1) no column shows it. So it is direct sunlight. The sun stands 0.26 degrees from the vertical, and the 0.5 m wall above the opening blocks it from the jamb. A Cycles reference of the exported scene with the sun and a black Earth shows 0.05 to 0.14 W/(m² sr) across the whole jamb and no strip. The strip starts at 0.75 to 0.79 of the jamb's width from the room side at three output sizes, so it covers about the outer 0.12 m of the wall. Candidates: the cascade's depth bias or normal offset near the hull's outer face, or the map's texel size at 14 m. Stage 1b's 'sunlit sill' (t1-gxzg, part 1) may be the same leak: check whether the sill is lit by the sun in a reference before relying on it.

