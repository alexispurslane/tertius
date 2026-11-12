---
id: t8y-9h5t
status: open
deps: []
links: []
created: 2026-11-12T18:04:49Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-vy3m
---
# A cloud Stryker run reads the shared host's load: judge a worker's timeouts by its own CPU pressure

On a Runpod CPU worker, /proc/loadavg and os.cpu_count() read the shared host (192 processors, loads of 16 to 204 on 2026-11-12), so changed.py's load rule allows 3,360 and never applies there. The job may use 2 processors (sched_getaffinity). The cgroup's /sys/fs/cgroup/cpu.pressure is the worker's own measure: some avg60 reached about 60 % during a Physics run with 4 xunit threads on 2 vCPUs. Calibrate a rule on it before a worker's timeout can make a run not valid. Until then a timeout on a worker counts as a kill; the timeout share rule still applies.

