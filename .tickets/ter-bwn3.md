---
id: ter-bwn3
status: open
deps: []
links: []
created: 2026-11-14T15:29:51Z
type: task
priority: 3
assignee: Alexis Purslane
---
# Cloud test shards build the test project once, not once per shard

Seen 2026-11-14 (sweep-pw52's card run of 3dae7207b): each of the six Release Rendering shards from tools/runpod/shards.py built the test project itself on its worker; the longest shard ran 702 s, and the six cost $0.51 against an estimate of $0.45. A full card run of the suites happens before most renderer merges, so a build per shard repeats the same compile six times. Measure how much of a shard's time the build takes, then decide between one build job whose output the shards reuse (for example a pushed artifact or the worker image) and keeping it as is if the build is a small share. Owner: the Runpod agent, next week.

