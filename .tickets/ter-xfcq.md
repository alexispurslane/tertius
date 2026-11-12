---
id: ter-xfcq
status: in_progress
deps: []
links: []
created: 2026-11-12T15:03:53Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [runpod, tools]
---
# GPU job caps: replace the log's estimates with Runpod's bill once an hour's bill is in

The user asked on 2026-11-12 that the caps not limit jobs early. gpu_job.py already reserves a job's worst case only while it waits or runs, then settles it to (executionTime + 60 s start allowance + 10 s idle) x $0.00016. At 10:05 local the log held $1.27 for 35 settled jobs, of which $0.90 was execution time and about $0.39 the flat 70 s per job. Runpod's bill showed $0.199 serverless GPU: its serverless figures lag by about an hour. Execution x price per UTC hour: 12h $0.019, 13h $0.138, 14h $0.745; the bill matched 12h + 13h plus starts and idle (factor about 1.27), and did not yet hold 14h. A warm worker that runs jobs back to back pays no start and shares one idle period, so the flat 70 s overcounts most for short jobs.

Change: for each hour whose bill is final (find out whether /v2/billing can give bucketSize=hour, and how late an hour becomes final), count Runpod's billed serverless amount instead of the log's estimates for that hour. For hours not yet final, keep the estimates and the in-flight worst cases. Never count less than the bill. Optionally calibrate the start-and-idle allowance from the closed hours instead of the flat 70 s. Tests first; keep the audit log's per-job lines as they are (the reconciled sum is a separate figure). Low priority while the caps are far from binding ($10 a day).

