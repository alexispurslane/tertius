---
id: ter-qdn8
status: closed
deps: []
links: [ter-onkj]
created: 2026-11-25T15:25:33Z
type: epic
priority: 1
assignee: Alexis Purslane
---
# Blue Danube second film: fitted interiors, credible surfaces and a moving pilot

See docs/plans/blue-danube-fidelity.md. One continuous route from boarding through a real hub bay and a spoke elevator to the rim.


## Notes

**2026-11-25T20:09:42Z**

The Linux release render ran the complete script on d392c3ae4. Both worker masters contain 32,189 frames. Each source frame has its readout in script order. With 1,020 title and credit frames, both MP4s contain 33,209 frames at 60 fps (553.483 s). Both passed a full audio and video decode. Samples at 30, 270 and 540 s matched the Marine Band recording. The 1080p and 720p copies and the recoverable master are in ~/Movies/Blue Danube. The editor input-clock correction is recorded in ter-onkj.

On the RTX 2000 Ada 16 GB, driver 595.112.07, with an EPYC 7352, GPU time from deformation through outlines averaged 11.286 ms per shown frame. Median was 11.035 ms and p95 was 17.462 ms, excluding capture and readback. The full render and recording took 2,738.857 s (11.75 source frames per wall second). The workbench counted $0.6165 across both attempts. Both temporary endpoints were removed.

The integrated revision passed 11,638 Checked tests, 11,625 Release tests and all 34 asset tests. Both .NET configurations skipped 27 tests. The exterior cockpit shot at 0:28 is overexposed (ter-fl8r). Some grain remains in wide shots of the berth.
