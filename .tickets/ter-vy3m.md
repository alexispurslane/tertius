---
id: ter-vy3m
status: open
deps: []
links: []
created: 2026-11-12T01:50:17Z
type: epic
priority: 1
assignee: Alexis Purslane
---
# Run the agents' GPU jobs on Runpod serverless workers, within $10 a day and $30 in total

The Mac's one GPU lock queues about 20 runs at a time, and agents wait up to an hour. Runpod serverless flex workers bill per second only while a job runs ($0.00016/s for the A4000, A4500 and RTX 4000 Ada class). Agents never hold the Runpod key: they hand each job to one queue script, which holds the key, logs agent, command, GPU seconds, cost and result per job, and refuses jobs past $10 a day or $30 in total for the 3-day trial. The account also carries only a prepaid balance without auto-pay. A job compares a change only against main inside the same job, because cards and hosts differ between jobs. User decisions of 2026-11-11: Runpod, not Vast.ai; serverless, not one pod per job; the caps above. The Runpod key works only on API v2 (https://api.runpod.io/v2); v1 answers 401.

