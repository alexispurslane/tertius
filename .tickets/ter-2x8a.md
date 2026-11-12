---
id: ter-2x8a
status: closed
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


## Notes

**2026-11-12T10:23:35Z**

2026-11-12 orchestrator: ON HOLD for a new user decision. The user's yes (2026-11-11) was 'then explore lowering rays to match'. Addendum A7 (/private/tmp/tertius-sweep-20261111/addendum.md; also docs/research/frame-time-sweep-2026-11-11/ on branch ter-2zga-patched-moltenvk) shows fewer rays do NOT match: with half the rays, ssr4h's daub score on paint rises 24 to 28 %. Picked variant ssr4h (edits/rs.py '4 ratio all') halves the deck floor's grain and removes 12 to 58 % of grain in the test view, but costs about +0.56 ms, raises broad error on paint by 46 to 97 % (more draws do not lower it), raises the plain floor's daub score 0.0049 -> 0.0087, and does not help the daubs on rough colored paint. Ask the user whether to build it at that cost.

**2026-11-12T12:23:35Z**

2026-11-12 user decision: DROP. The 4-block resolve (ssr4h) halves the deck floor's grain but costs about +0.56 ms, raises broad error on paint by 46 to 97 %, and does not help the daubs; fewer rays cannot pay for it (addendum A7). Closed without a build. The measurements stay in docs/research/frame-time-sweep-2026-11-11/ (branch ter-2zga-patched-moltenvk).
