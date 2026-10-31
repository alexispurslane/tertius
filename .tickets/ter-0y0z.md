---
id: ter-0y0z
status: open
deps: []
links: []
created: 2026-10-31T20:04:24Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# The exposure pass runs slower in a frame that runs the haze pass

Found by ter-bgpv, 2026-10-31. In mars-air, view above (1920 x 1080, Release, M1 Max, paired Metal System Traces with frames that alternate with and without the atmosphere record), the exposure pass costs 0.115 ms more in a frame that runs the haze pass. The haze pass itself costs 0.175 ms in the same traces. This is a lead on the exposure row of the cost table in ARCHITECTURE.md, 'The atmosphere', which ter-6vhj measured at 0.126 ms against 0.069 ms and could not explain. Every hazed view pays the cost. Find the cause, and remove the cost if the cause allows it. Candidates to test, not established: the haze pass's buffer barrier on the exposure buffer (HazePass.cs, barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead)), and the state of the HDR image after the dual-source blend. Measure with the paired method of ter-bgpv's note, under the GPU lock.

