---
id: t8y-fvzn
status: open
deps: []
links: []
created: 2026-11-12T18:04:49Z
type: task
priority: 4
assignee: Alexis Purslane
parent: ter-vy3m
---
# One store for each job kind: hardware, disk, choices and names from endpoint.json

Review of t8-yd4j: a kind is defined in five places (endpoint.json kinds, HARDWARE, the OURS regex, two argparse choices, and cloud.py's literal --kind cpu). Move hardware and disk into each kind of endpoint.json and derive the rest. Also rename what now means either kind: the audit log's gpuSeconds (read old lines too). And move the schema of results.json into one module: changed.py writes it with print_report, and cloud.merged reads the same keys as literals.

