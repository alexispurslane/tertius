---
id: t8-1zco
status: in_progress
deps: []
links: []
created: 2026-11-12T18:18:40Z
type: task
priority: 4
assignee: Alexis Purslane
parent: ter-vy3m
---
# gpu_job.py stats: the mean cost per job by kind and by agent, with its spread, and the bill beside it

For Alexis's curiosity only; nothing acts on it. Read only: from the audit log, the cost per job that ran (not refused or rejected): count, mean, standard deviation, 95 % interval of the mean, median, minimum and maximum, for the settled estimate and for the execution time alone times the kind's price; by kind and by agent. Runpod bills by the hour, so the bill is one line: the final hours since the first job against the estimate of the jobs that ended in them. Check values of the orchestrator at 59 jobs: settled mean $0.0540, sd $0.0406, 95 % ±$0.0107, median $0.0405.

