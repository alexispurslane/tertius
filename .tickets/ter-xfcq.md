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


## Notes

**2026-11-12T19:23:50Z**

Built on branch ter-xfcq-billed-hours. `GET /v2/billing?bucketSize=hour&lastN=N` gives one record per UTC hour with startTime and totalAmount, and the serverless parts (GPU, CPU, disk, fees) apart from pods and storage. The current hour has no record. The day's total, the month's total and the hourly records agreed to the cent when read in the same minute (18:26 UTC).

How late an hour becomes final, sampled every 10 minutes on 2026-11-12 (UTC):

| Time | 16:00 | 17:00 | 18:00 |
|---|---:|---:|---:|
| 18:12 | 0.69838 | 0.08086 | none |
| 18:22 | 0.69838 | 0.13503 | none |
| 18:32 | 0.69838 | 0.18643 | none |
| 18:42 | 0.69838 | 0.20165 | none |
| 18:52 | 0.69838 | 0.21329 | none |
| 19:02 | 0.69838 | 0.21947 | 0.0043 |
| 19:12 | 0.69838 | 0.21947 | 0.0043 |
| 19:22 | 0.69838 | 0.21947 | 0.0043 |

So an hour's record appears soon after the hour ends, grows for about one more hour, and then stays. BILL_FINAL_HOURS = 2 counts the bill of 17:00 as final from 20:00 on, one hour of margin.

The rule: each cap counts Runpod's bill of the final hours as it is, and each later hour the larger of its own bill and the logged costs of the jobs that ended in it. Each settled line now records `ended` (a cancelled job: its expiry). The review (reviewer agent) found that one max over all open hours let a pod's bill hide another hour's unbilled jobs; fixed with a max per hour and a test. On the real log at 18:2x UTC, the bill of the final hours 12:00 to 15:00 was $0.95 for the 31 jobs that ended in them, against a settled estimate of $1.17 (0.81 times). The bill also holds a disk charge per worker that the cost model leaves out.
