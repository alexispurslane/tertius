---
id: ter-0y0z
status: in_progress
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


## Notes

**2026-10-31T20:18:16Z**

2026-10-31: the orchestrator filed the same finding as ter-kc9w on main while this ticket was created on the ter-bgpv branch; ter-kc9w is removed in the merge, because ARCHITECTURE.md's cost table names this ticket. Its one extra fact: on a loaded machine, separate runs with and without the haze drift by about 13 % in every pass, so only frame-alternating paired traces resolve this cost.

**2026-10-31T21:19:49Z**

Measurement (builder, 2026-10-31). Result: the exposure pass's extra time in a hazed frame is not a cost of the frame. The pass's first command, the histogram's clear (vkCmdFillBuffer, which MoltenVK records as a compute encoder of its own), does not wait for the HDR image. So the GPU starts it about 0.007 ms after the haze pass starts, and its interval lasts until the haze pass's fragments end, because they hold the GPU's cores. A pass's busy time is the union of its encoders' intervals (tools/README.md, "GPU trace"), so both passes count that interval. The wall-clock GPU time from the end of the reflections to the start of the bloom grows by the haze pass's time and not more. By step 3 of the brief, the branch makes no code change: the one change that removes the extra busy time makes the frame slower. ARCHITECTURE.md ('The atmosphere', the exposure row and the prose after the cost table) states the cause and the numbers.

Method. tools/gpu-trace.sh --no-counters --keep-exports, 200 frames (from frame 20 of a temporary script: mars-air, the camera on above-view or day-view, one film of 230 frames), 1920 x 1080, Release, M1 Max, each trace under the GPU lock. A temporary probe in HazePass.Record skipped the pass on every other call. Each frame without the haze was compared with the mean of its two neighbours with it. The control trace runs the haze in every frame and pairs the odd frames with their even neighbours. A temporary script split the exposure pass's busy time by encoder (clear, meter, adapt), computed its time outside the haze pass's intervals, and the wall-clock span from the reflections' last interval end to the bloom's first interval start after it. The standard errors are bootstrap errors of the median. The probes and scripts were temporary and are not committed. Frames whose trace lost encoders (2 of 201 in one trace) were left out of every pair.

Paired difference, with the haze minus without, median in ms (standard error), for exposure / clear / meter / exposure outside the haze pass / haze / reflections end to bloom start / frame span, and the 1-minute load at the start and the end:
- above, as shipped (load 142.1 to 45.4): +0.102 (0.007) / +0.121 / -0.019 / -0.022 / +0.162 / +0.163 (0.005) / +0.219 (0.031).
- above, control, the haze in every frame (load 42.5 to 34.8): -0.001 (0.001) / -0.002 / +0.000 / +0.000 / -0.000 / +0.008 (0.003) / -0.005 (0.072).
- day, as shipped (load 23.9 to 55.9): +0.114 (0.005) / +0.134 / -0.017 / -0.022 / +0.186 / +0.173 (0.003) / +0.172 (0.015). ter-6vhj's 0.126 against 0.069 ms measured this view, so its exposure row has the same cause.

Candidates, each a temporary change, traced on above with the haze on every other frame:
1. The haze pass's barrier on the exposure buffer (HazePass.cs, barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead)) records nothing: a probe log shows the buffer's state before the call as vertex, fragment, compute and transfer reads, so BarrierBatch.Buffer returns with no barrier. Removed (load 14.5 to 18.3): exposure +0.125 (0.006), clear +0.140, outside the haze -0.022. Not the cause.
2. The HDR image's state after the dual-source blend. The image is already in ColorAttachmentOptimal before the haze pass, so the pass adds no layout change. A probe whose haze.frag writes outColor 0 and transmittance 1 keeps the pass, the blend and the layout, and leaves the image unchanged (load 30.9 to 18.4): exposure -0.011 (0.004), clear +0.010, meter -0.020, haze +0.095 (the probe's fragments are cheap). The extra time goes with the haze's fragment work, not with the image's state. Not the cause.
3a. Shared-memory atomic contention in the meter (a hazed image puts more pixels in fewer bins). A probe meter that keeps the HDR read and binOf but adds each invocation to its own bin, so no two adds share an address (load 28.2 to 20.7): exposure +0.148 (0.001), clear +0.151, meter -0.002. Not the cause.
3b. A probe haze that writes one constant color over every surface pixel (load 16.1 to 13.5): exposure +0.060, clear +0.075, haze +0.107. The clear's growth follows the haze pass's length.
3c. The clear running beside the haze pass. A probe memory barrier before the clear, from the color attachment output stage and its writes to the transfer stage and its writes, makes the clear wait for the haze pass (load 27.1 to 18.2): exposure -0.015 (0.001), clear -0.000, meter -0.016, haze +0.174, reflections end to bloom start +0.185 (0.004), frame span +0.294 (0.025). This removes the extra busy time and brings the exposure pass below the control's noise, so it is the cause. A first try of this probe with an image barrier to the compute stage did not hold back the clear, because the clear is a transfer command. That trace is not counted.

Decision. Within each trace, the wall-clock span from the reflections to the bloom grows by the haze pass's time plus 0.001 ms as shipped, and plus 0.011 ms when the clear waits. So the overlap is free parallel work, and the probe barrier would cost the frame time. The renderer keeps its barriers and its pass order. No code changed, so the brief's pixel comparison, validation run, demo-exit deck trace and test runs do not apply. The meter runs 0.016 to 0.020 ms faster after the haze pass in every trace. Why is not established (one guess, not tested: the haze pass just wrote the HDR image, so the meter's reads find it in the GPU's cache).

Observation for the tools, not done here: tools/gpu-trace.py reports each pass's busy time as the union of its own intervals, so an encoder in flight beside another pass counts in both. A column of each pass's time outside the other passes' intervals would show such an overlap directly.

**2026-10-31T21:34:29Z**

Review fix (builder, 2026-10-31). This note corrects the 'Decision' paragraph of the note above. No new trace ran: a temporary script read the kept exports in /tmp/ter-0y0z/traces again. The window is the wall-clock GPU time from the end of the reflections' last interval to the start of the bloom's first interval after it.

Correction. The note above compared the window with the haze pass's busy time. That busy time also holds a short interval of the haze pass's render encoder, 5.7 ms (above) to 7.5 ms (day) before the window, beside the G-buffer and the ambient light. So the two numbers did not measure the same thing. Paired medians in ms (standard error), for the haze inside the window / the haze's early interval / the window / the idle time inside the window / the window minus the haze inside it:
- above, as shipped: +0.145 (0.003) / +0.016 (0.0005) / +0.163 (0.005) / +0.040 (0.002) / +0.016 (0.003).
- above, control: +0.000 (0.0005) / -0.001 / +0.008 (0.003) / +0.004 (0.004) / +0.006 (0.003).
- day, as shipped: +0.162 (0.005) / +0.018 / +0.173 (0.003) / +0.035 (0.001) / +0.011 (0.001).
- above, the clear waits (probe): +0.155 (0.007) / +0.018 / +0.185 (0.004) / +0.050 (0.001) / +0.033 (0.001).
- Other probes, the window minus the haze inside it: haze barrier removed +0.013 (0.002), constant haze color +0.018 (0.002), meter with its own bin per invocation +0.031 (0.001), identity haze -0.024 (0.003).
So as shipped, the window grows by the haze's work inside it plus about 0.010 ms beyond the control, which is below ter-bgpv's 0.02 ms threshold. Its cause is not established. The clear-waits probe grows the window by 0.033 ms beyond the haze, against 0.016 ms as shipped, so it costs the frame about 0.017 ms (the earlier 0.011 ms against 0.001 ms was wrong, and so was the commit message of 11c641f1). The decision stands, with stronger support: keep the barriers and the pass order.

Timing facts, medians over the hazed frames: the clear starts 0.007 ms after the haze pass's interval inside the window starts (not after the haze pass's first interval). The clear's interval ends 0.0006 ms (above) to 0.0009 ms (day) before the haze pass's interval ends. That the haze's fragments hold the GPU's cores is the likely reason, not a measured one. One fact fits it: in the identity probe, whose fragments are cheap, the clear ends 0.062 ms before the haze pass ends.

Control parity. In every alternating trace, each frame without the haze has an odd index in the frame order, and the control also pairs the odd frames with their even neighbors. So the control measures the same frame slots, and any alternation between the slots is in the control's noise.

The brief's test 'down to the control's noise'. With the probe, the clear's extra time is -0.000 ms, at the control's -0.002 ms. The exposure pass's -0.015 ms is below the control because the meter's speed-up after the haze (0.016 to 0.019 ms) remains. That speed-up is a saving, not an extra cost, and its reason is still not established.

ter-6vhj's size. Its difference, 0.126 - 0.069 = 0.057 ms, is about half of the paired +0.114 ms in day. Two facts can explain the gap. Its runs were not paired, and separate runs drift by about 13 % in every pass. Its haze pass took 0.126 ms, against 0.186 ms now, and the clear's extra time follows the haze's work inside the window (shipped +0.121 for +0.145, day +0.134 for +0.162, constant color +0.075 for +0.089). No measurement splits the gap between the two.

Tool work, filed as tickets under ter-pq5y: ter-9hie (passes.md shows each pass's time alone, and tools/README.md says that the busy time minus the time alone is shared time) and ter-e91p (a paired mode in tools/gpu-trace.py, after ter-9hie). The temporary scripts stay out of the commits, as the brief says.

ARCHITECTURE.md, 'The atmosphere': the table now gives the haze inside the window, the window, and the window minus the haze inside it. The prose states the 0.010 ms residual, the probe's 0.017 ms cost, the clear's start after the haze's fragment work, the core occupancy as the likely reason, the meter and the clear as the reasons why the exposure pass is faster outside the haze, and the two possible reasons for ter-6vhj's smaller size. The new prose spells 'neighbors'.
