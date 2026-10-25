---
id: ter-zqla
status: closed
deps: []
links: []
created: 2026-10-25T15:53:10Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, performance]
---
# Meter the exposure on every second pixel in each axis

The exposure pass costs a mean of 0.311 ms at 1920x1080 on demo-exit (median 0.320, p95 0.422), above the 0.2 ms line of docs/plans/auto-exposure.md ("Cost"). That doc's rule makes a meter on every second pixel in each axis a ticket.

## Acceptance Criteria

The exposure pass costs under 0.2 ms at 1920x1080, and the metering tests still pass.


## Notes

**2026-10-25T21:58:44Z**

Merged into main on 2026-10-25 (2509aaac). The exposure pass costs a mean of 0.150 ms at 1920x1080 on demo-exit (median 0.118 ms), from 0.298 ms (median 0.302 ms), loads 3.4 to 6.3. The metered target moves by at most 0.016 stops on the test ramps. Coverage of the changed C# lines: 6 of 6. No screenshots: nothing new shows on screen.
