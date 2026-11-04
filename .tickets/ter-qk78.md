---
id: ter-qk78
status: open
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

