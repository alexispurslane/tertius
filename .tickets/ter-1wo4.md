---
id: ter-1wo4
status: open
deps: []
links: []
created: 2026-11-14T18:49:14Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The survey's probe patch no longer compiles on main: its local 'stops' clashes with ExposureStops

Found 2026-11-14 by the Design X takes (t0-f8oc): since the key-and-fill merge ff7b7da4e (t2-vsqh), tools/renderer-survey/probe.patch's local 'stops' clashes with ExposureStops, so the probe build fails on main. Scratch fix 797a3b2a9 renames the local to 'probeStops' (on no branch; published to a hidden job ref). The Design X branch's patch breaks the same way when it merges main.

