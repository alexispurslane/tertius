---
id: ter-mu7k
status: in_progress
deps: [ter-1i4z]
links: []
created: 2026-10-11T13:52:09Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [host]
---
# Add the windowed host with input

Step 4 of docs/plans/host-and-scripted-runs.md.


## Notes

**2026-10-11T16:48:17Z**

Decided in ter-1i4z (User): this ticket also plays scripts in the window. Without --headless, the command opens a window and plays the script at real time, 60 ticks a second, with --speed <factor> for slow motion or fast forward. The window closes when the script ends. The window runs on the main thread (macOS), the script on its own thread, and the renderer draws the latest render snapshot.
