---
id: ter-wkvw
status: open
deps: [tb-8zew]
links: [ter-18hc, ter-9371]
created: 2026-11-09T12:08:39Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-icn3
---
# The light-change age cuts the diffuse history too, so bounce light follows a lamp switch faster

User decision 2026-11-09. Since the diffuse filter (ter-9371), a ray's hit reads the filtered light, which comes from the 32-frame slow history, so bounce light needs about twice as many draws to follow a lamp switch: settle counts (draws until the indirect diffuse light stays within 5 % of a control run, lamps off then on) are cabin 85/62, bay with the sun off 57/47, lamp hall 35/37; with hits reading the fast history they were 44/41, 35/32 and 31/34 (/private/tmp/tertius-diffuse-clamp-20261108/, settle2.py). The user keeps the clamp (ter-18hc note). Build: the light-change age that today cuts only the specular history also cuts the diffuse slow history where the light changed, with the same change test, floor, 4-draw wait and own-band rule. Hits keep reading the filtered light. Acceptance: the settle counts fall toward the fast-history numbers, still scenes keep their light within 1 % and their speck counts, and the moving lamps and the bay's moving sunbeam show no new blotches from false cuts.

