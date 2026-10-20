---
id: ter-oehq
status: open
deps: []
links: []
created: 2026-10-20T17:17:26Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [performance, testing]
---
# The world's debug checks cost 6.6 ms a tick with the demo station in the world

Found by ter-tzf6 part 6 on 2026-10-20 with a timing test (deleted after): WorldOptions.DebugChecks, which runs the world's invariant checks at the end of every sync point, costs 6.6 ms a tick in demo-surface with the station of DemoScenes.DemoStation, against 0.125 ms a tick without the checks and 0.008 ms of the checks without the station. The station adds a kinematic hull of 256 segment hulls plus spokes and a hub in a following frame, a floor mesh as its interior, and static parts in a turning frame. So DemoStationTests takes about 42 s, a first RendezvousAutopilotTests run with the checks took 8.5 minutes, and a run of the long script with the checks would take about 30 minutes. The long tests now run without the checks. Find which check scales with the station's shape (a pass over compound children, contact pairs, or static bodies per frame, for example) and make it scale with what changed, or say why it cannot. A check that only runs in tests can still cost too much to run in the tests that need it most.

