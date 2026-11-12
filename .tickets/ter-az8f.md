---
id: ter-az8f
status: open
deps: []
links: []
created: 2026-11-12T02:50:00Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-vy3m
tags: [linux]
---
# The timing tools record no GPU clock on Linux: read NVIDIA's clock with nvidia-smi

tools/gpu-clock.sh reads IOReport, so on Linux every timing record says that the clock is unknown, and tools/gpu_clock.py cannot check that two runs compare. On the Runpod pods of ter-libf (2026-11-12), `nvidia-smi --query-gpu=timestamp,clocks.sm,clocks.mem,temperature.gpu,power.draw --format=csv -lms 500` worked without privileges, and the SM clock under load stayed at 2205 to 2460 MHz on the RTX 2000 Ada. `nvidia-smi -lgc` fails without host root (exit code 4). Add an NVIDIA sampler to gpu-clock.sh with the same sample format, so the span clock and the 3 % check work on the serverless workers. Also the survey's GPU_SAMPLER calls ioreg, which Linux lacks: give it the same branch. ter-94uo needs this to compare a change with main inside one job.
