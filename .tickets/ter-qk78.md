---
id: ter-qk78
status: in_progress
deps: []
links: []
created: 2026-11-04T01:46:03Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, tools]
---
# Record the GPU clock beside every timed run, and compare builds only at the same clock

Found in ter-3ug8 (docs/research/temporal-renderer/gpu-calibration.md). The GPU's clock on the development Mac moves with the GPU's duty cycle: headless demo runs at 1280 x 720 run at a mean of 800 MHz, at 1920 x 1080 at about 1050 MHz, and the sustained clock is 1293 MHz. A pass whose time follows the clock takes a time inversely proportional to the clock. So two timed runs compare only at the same clock, and a timed run that does not record its clock cannot be converted to the clock at which section 11 of docs/plans/temporal-renderer.md states its budgets. Make the timing tools record the clock beside every timed run: tools/gpu-trace.sh, the headless demo's GPU timings, FSR's timing test and the spikes' rounds. tools/gpu-calibration/gpu-clock.c reads it through IOReport without root, on macOS only. A run on another driver records that its clock is unknown.

## Notes

**2026-11-04T03:25:10Z**

2026-11-03 (ter-3ug8's review fix): record the clock over the timed span, not over the whole run. The run's mean holds the setup, which runs at 648 MHz: FSR's batched measurement ran at 1208 MHz over the whole run and at 1257 MHz over its timed batches. The measurement prints the lines timed-start and timed-end around its timed work, and tools/gpu-calibration/analyze.py's span_clock averages gpu-clock's samples between them. Every timing tool takes the GPU lock through tools/gpu-lock.sh, the one helper of the lock, whose waiters take turns in the order in which they came.


**2026-11-04T14:04:14Z**

2026-11-04: started as workflow run b918231c-7a42-41bc-8d71-fd4a73fe4341 (version 869b0b07), branch ter-qk78-gpu-clock from ef5ae90c4.
