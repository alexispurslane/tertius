---
id: t1-g4vd
status: open
deps: []
links: []
created: 2026-11-12T03:32:29Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The empty-pass span test fails at a load of about 28: High 0.142 ms against 0.05 ms

GpuTimingTests.APassThatRecordsNothingAddsNearlyNoTimeToTheFramesGpuSpan (GpuTimingTests.cs:418) failed in the full Checked suite of t1-4hij on 36791ba1e, 2026-11-11 22:12 to 22:31. It measured SpanAddedBy(renderer, "empty").High = 0.141959 ms against the bound NearlyNoTime = 0.05 ms. The take was a shared take of the GPU lock (up to 3 shared runs at once), and the one-minute load was 28.6 at its end. t1-4hij changes no GPU timing code. ter-abun made this test a decision over chunks of 128 frame pairs and closed it on 2026-11-04 after 10 runs beside a rendering loop at loads of 5.5 to 20.3. A load of about 28 with other shared GPU takes lies outside that acceptance. Find whether the test must hold under shared takes, or whether it needs the exclusive lock.


## Notes

**2026-11-12T06:36:50Z**

2026-11-12 01:34: the test passed 5 of 5 times run alone on the same build (Checked, 36791ba1e plus a new mask test), in a shared take at a one-minute load of 5.7 to 6.3.
