---
id: t2-bne2
status: open
deps: []
links: []
created: 2026-11-11T23:10:43Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
tags: [tools, rendering, performance]
---
# A fixed exposure for the trace and timing tools, so that a cost variant keeps every lamp's reach

Found in t2-mlvy (docs/plans/light-pairs.md, 'How the cost was measured'). A shader variant that removes a shadow changes the image. The automatic exposure follows the image, and a lamp's reach follows the exposure. On the deck, three variants let the sun flood the bay: the exposure fell by about 10 stops, every lamp fell out of reach, and the direct pass read 1.0 ms. The first reading credited 3 ms to the sun's shadow lookup, which costs 0.03 ms. The ticket's variants then fixed the exposure with a patch of ScriptCommand.Options.cs (docs/research/light-pairs/variants/fixexp.py). Give the headless demo an option for a fixed exposure in stops, pass it through tools/gpu-trace.sh and the timing tools, and make a trace report print the exposure of its run beside the passes, so that two runs with different exposures are never compared by mistake.

## Acceptance Criteria

A headless run takes a fixed exposure in stops. gpu-trace.sh passes it on. passes.md states the run's exposure. A comparison tool warns when two runs' exposures differ by more than 0.05 stops.

