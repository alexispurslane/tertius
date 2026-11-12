---
id: ter-2x8a
status: open
deps: []
links: []
created: 2026-11-12T02:59:35Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# Reflection resolve from the rays of 4 blocks, then fewer reflection rays at the same quality

Sweep row 10 (/private/tmp/tertius-sweep-20261111/report.md, section 7). The user said yes, "then explore lowering rays to match". The plain 4-block division costs about +0.56 ms and alone does not pay for half the rays (about 41 % more clouds). The sweep is testing own-weight variants (/private/tmp/tertius-sweep-20261111/edits/rs.py); a builder starts from the variant that the sweep's scores pick. Overlaps the stale reflection reconstruction tickets tiunr-463x, tiunr-ax4c and tiunr-ipmk.

