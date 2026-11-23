---
id: t0-r1ow
status: open
deps: []
links: [t0-3vwx]
created: 2026-11-23T03:19:21Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# The films' take script goes on after a failed survey run: final.sh step 4 checks no exit code, no played-back size log and no disk space

Found by the reviewer of t0-3vwx on 2026-11-22 (finding S9), recorded and not fixed.

docs/research/t0-3vwx-dynamic-scale-films/tools/final.sh, step 4 (the survey's takes):
- It calls t0-gbgj's take.sh (docs/research/t0-gbgj-combined-check/tools/take.sh), which exits 0 also when the survey's run failed. So final.sh goes on and the later steps measure old or missing pictures.
- It does not compare the survey run's played-back render size log with the card's log. The Mac's picture runs are compared (queue-a.sh), the survey's runs are not. A playback that fell out of step with the card's log would pass unseen.
- It has no check of the free disk space before the takes, where the line's rule is no new build or take under 22 GiB.

Why it was not fixed with the review: the region take was using take.sh at that time.

The final films of t0-3vwx are not in doubt by this: their runs ended well, and the reviewer checked their numbers against the logs. The fault matters for the next take that reuses the script.

## Acceptance Criteria

take.sh exits with the survey's failure, and final.sh stops at the first failed step. Step 4 compares each survey run's played-back size log with the card's log and stops on a difference. Step 4 checks the free disk space first. A run with a forced failure in step 4 shows that the script stops.

