---
id: ter-enb1
status: closed
deps: []
links: []
created: 2026-10-25T15:53:10Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, performance]
---
# Explain the G-buffer pass's doubled p95 on the exposure branch

On demo-exit at 1920x1080, the G-buffer pass's p95 rose from 1.14 ms on main to about 2.24 ms on the exposure branch, in both runs, far beyond the spread between runs. Its mean rose only 0.12 ms. Find the cause, for example the metering draws of headless films or the frames that capture impostors, and fix it or record why it is expected.


## Notes

**2026-11-03T12:35:13Z**

Closed on 2026-11-03 at the user's request, without its work merged: it belongs to the pre-temporal renderer (spatial reuse, the 12 ms performance push, or the old reflection and bounce pipeline), and epic ter-89xq, the temporal renderer, supersedes it. Its notes and any branch or worktree stay for reference.
