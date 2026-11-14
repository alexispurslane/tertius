---
id: ter-eugk
status: closed
deps: []
links: []
created: 2026-11-13T04:15:28Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Run the full Host and Rendering suites on the card on 2026-11-14 UTC and correct the README's list of Linux failures

No full card run happened after ter-8esk and t2-pw52 merged. On the UTC day 2026-11-14, from that day's cloud share of t8-sjli's owner, run the Host suite and the Rendering suite in GPU shards (tools/runpod/WORKBENCH.md, "Tests in the cloud"; about $0.50 on 2026-11-12) on main, and rewrite "Linux failures" in tools/runpod/README.md from that run. Each new failure gets a ticket of kind (a) or (b).


## Notes

**2026-11-14T14:19:02Z**

Done on 2026-11-14, main 774353dcf. Rendering in six shards: 3,161 tests, 1 failed (t8-v31a), 9 skipped. Host: 2,061 tests, 0 failed, 2 skipped, 8 not run. Cost 0.589 USD, 0.09 over the share of 0.50: the six shards cost 0.53 against 0.43 on 2026-11-12. The shards came from a list that covers all 204 classes; the new tools/runpod/shards.py gives 6 x 34 on the worker too. The hand-written lists of cloud-tests-split (2026-11-12, d1fc1fff3) and sweep-pw52 (2026-11-13 02:09 UTC, adcd60cf4) left out 15 and 19 classes that existed then; t7-3e22's lists of 2026-11-13 03:19 UTC did not.
