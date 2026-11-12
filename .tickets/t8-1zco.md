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


## Notes

**2026-11-12T18:26:55Z**

The check value of the 95 % half width: at the first 59 jobs that ran, the code gives mean $0.0540, sd $0.0406, median $0.0405 and ±$0.0106, with Student's t(58) = 2.0017 times sd/√59. The orchestrator's ±$0.0107 matches sd/√58, the wrong denominator, so the code stays. The execution-only figures differ by about $0.0005 from the orchestrator's (mean $0.0313 against $0.0318), probably from a different treatment of jobs with no or zero execution time; the code follows the rule in its docstring (a lost job has no execution figure, a zero is a zero).
