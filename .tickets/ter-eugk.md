---
id: ter-eugk
status: open
deps: []
links: []
created: 2026-11-13T04:15:28Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Run the full Host and Rendering suites on the card on 2026-11-14 UTC and correct the README's list of Linux failures

No full card run happened after ter-8esk and t2-pw52 merged. On the UTC day 2026-11-14, from that day's cloud share of t8-sjli's owner, run the Host suite and the Rendering suite in GPU shards (tools/runpod/WORKBENCH.md, "Tests in the cloud"; about $0.50 on 2026-11-12) on main, and rewrite "Linux failures" in tools/runpod/README.md from that run. Each new failure gets a ticket of kind (a) or (b).

