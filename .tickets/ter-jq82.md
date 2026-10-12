---
id: ter-jq82
status: open
deps: []
links: []
created: 2026-10-12T12:17:32Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [host, scripts, slice]
---
# Record free play as a script that replays it

The user saw a thrown crate jitter in free play and asked for a trace of what they did. Free play records nothing today.

Add --record <path> to the Demo's free play (and to a windowed script run, if it fits). It writes an ordinary script in the existing format (docs/plans/host-and-scripted-runs.md): the scene, the window size, the world's camera at the start, a post step for every InputEvent that the game receives, with run steps for the ticks between them, a camera step for every view switch (V), and a dump at the end. Ticks, not wall-clock time, so the record does not depend on --speed. Running the script headless replays the session tick for tick, so a dump or a capture can be added anywhere in it.

## Acceptance Criteria

A free-play session recorded with --record replays headless to the same state: the final dump of the replay equals the state at the end of the session, checked by a test that drives the window's input path.

