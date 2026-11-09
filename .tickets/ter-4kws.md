---
id: ter-4kws
status: open
deps: []
links: []
created: 2026-11-09T15:10:17Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Rays that hit a surface read its fast diffuse history again, if the specks stay away

Since ter-9371, a diffuse ray's hit reads the filtered light, which comes from the 32-frame slow history, so the orbit cabin's bounce light needs 84 draws to settle after lamps switch off, against the goal of about 60 (ter-8hyd). With hits on the fast history, the research measured cabin 44/41, bay with the sun off 35/32, lamp hall 31/34 (/private/tmp/tertius-diffuse-clamp-20261108/). ter-9371 moved hits to the filtered light so that specks would stop spreading; that was before measuring. Test whether hits on the fast history bring the specks back now that the filter cleans every surface's diffuse light. Accept only if every settle count is at most 60, the speck counts and region means of still scenes stay within the noise of main, and walks and the bay's moving sun show no new blotches.

