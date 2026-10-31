---
id: ter-bgpv
status: open
deps: []
links: []
created: 2026-10-31T18:44:05Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# The haze pass skips a view in which no opaque pixel can reach the air

Review finding 4 (low) of ter-uu2y, 2026-10-31. Since ter-uu2y, a camera whose chain of frames holds no planet medium takes the planet medium that is densest at its eye. So every view of a world with a hazed planet has an atmosphere record, and the haze pass (HazePass.cs) runs over every opaque pixel, even when the planet is far away or out of the view and T is 1 everywhere. The direct light also pays the tint's exponential. ter-6vhj measured the haze pass at 0.126 ms and the whole look at 0.35 ms of frame span at 1920 x 1080 (ARCHITECTURE.md, 'The atmosphere'). Measure the cost in such a view first. If it matters for the 12 ms target, skip the haze pass, and give the light no tint, when no opaque pixel can reach the air: for example, when the eye's density share is 0 and the sphere of radius R + 40 H lies outside the view frustum. The rule must change no image in which the air is visible.


## Notes

**2026-10-31T19:51:12Z**

Measurement (builder, 2026-10-31). Result: no shipped view pays more than the noise of the paired-trace statistic below (a control difference of at most 0.007 ms, below the brief's 0.02 ms threshold) for an atmosphere that it cannot show. So, by step 2 of the brief, the branch makes no code change. ARCHITECTURE.md ("The atmosphere") and docs/plans/atmosphere.md ("Not in this ticket") state the result.

Which views carry a record. Only `mars-air` (DemoScenes.MarsAir.cs) paints a haze, and each of its six views looks down at the planet from inside the air's density curve (at most 305 km = 27.5 H up, s(eye) = e^-27.5, not 0), so the air shows in each. demo-exit, the scene of the 12 ms target, holds no planet medium: its Earth is a root frame without one, so its snapshots carry no record and it pays nothing. The scenes with Earth's air (`Air.Earth` in earth-surface: orbit, station, earth-system, demo-surface, mirror-balls and their scripts) carry a record in every view since ter-uu2y. That air paints no look: no haze, so HazePass records nothing, and a tint of 1, but the batcher still marks the sun (`GpuLight.airSun`), so each sunlit pixel computes sunTintAt.

Method. The GPU timer could not resolve 0.02 ms: on this loaded machine, frame-span medians of the same view varied by about 1 ms between runs. So the measurement used Metal System Traces without counters (tools/gpu-trace.sh, 200 frames at 1920 x 1080, Release, M1 Max, under the GPU lock). A temporary probe in SceneBatcher.Prepare dropped the atmosphere (GpuAtmosphere none, no airSun) on every other recorded frame. Each frame without the record was compared with the mean of its two neighbours with it, so slow GPU clock changes cancel. A control trace with the record on every frame gives the noise of the same statistic. In mars-air, the haze pass marks the frames with the record, and it agreed with the frame parity on 200 of 200 frames. The probe and the scripts were temporary and are not committed.

Paired difference, frame with the record minus frame without, median, in ms (control in brackets):
- station, view `watch` (Earth's air, record from the densest air at the eye): direct light +0.003 (+0.007); environment faces +0.014 (+0.003, standard error 0.014); ambient light +0.004 (+0.002); haze pass: no GPU work in any frame.
- orbit, view `chase`: direct light +0.006 (+0.007); environment faces -0.004 (-0.003); ambient light +0.002 (+0.002); haze pass: no GPU work.
- demo-exit, view `deck`: no record, so the probe changes nothing: direct light +0.003, the noise.
- mars-air, view `above` (the air shows, so not a skip candidate): haze pass +0.175, exposure +0.115 (the unexplained exposure cost of ter-6vhj comes with the haze pass), direct light +0.014 (standard error 0.018), frame span +0.22 (standard error 0.05).
The frame span's paired difference is too noisy for 0.02 ms (standard error 0.05 to 0.15 ms, and +0.055 in demo-exit, where the probe changes nothing), so the busy times of the passes that read the air decide.

Shader facts for a later skip, if a scene ever needs one. (1) The haze has no cut-off radius: s(p) = exp(-h/H) reaches 0 only where the float exp underflows, at about 87 H, or 103 H with denormals. A skip test must use the height at which T and the tint round to exactly 1, not R + 40 H. (2) T(p) reads only the eye's and the point's heights and the distance between them (airTransmittance), not the ray's path. A grazing ray with both ends above the air takes almost no haze. (3) The sun's tint models no path to the sun: it is mix(1, SunTint, s(p)) at the lit point. So a station in orbit at sunset gets a tint of almost exactly 1, and the planet out of view leaves the tint at 1 too. (4) For an air without a SunTint, the tint is mix(1, 1, s). That is exactly 1 if the driver computes 1 + s * 0, or (1 - s) + s with round-to-nearest-even, but the Vulkan spec does not require a correctly rounded mix. So a later skip that stops marking airSun must make sure that the image does not change, with a GPU test on each target driver, or compute the tint without mix when SunTint is 1. The measurement shows that this would save nothing measurable.

Screenshots (headless, Release, 1280 x 720): captures/ter-bgpv/station-pods-out-record-no-air.png (the station in orbit, Earth below; the view carries Earth's record and nothing shows of the air), captures/ter-bgpv/mars-air-day-hazed.png (mars-air's day view, hazed), captures/ter-bgpv/mars-air-above-hazed.png (mars-air from 120 km, hazed ground under a black sky).

**2026-10-31T20:05:19Z**

Review fix stage (builder, 2026-10-31). The review passed with three low findings and some gaps. Resolution of each:
1. Design finding, no trigger that re-opens the skip. ARCHITECTURE.md ('The atmosphere') and docs/plans/atmosphere.md ('Not in this ticket') now say that the decision holds while no shipped scene gives a haze to an air whose views can leave the planet out of view. Such a scene re-opens the skip, and this ticket's note holds the shader facts for it.
2. Correctness finding, 'the timer's noise, about 0.02 ms'. That stale phrase named the wrong instrument. Both docs and the first note now name the paired Metal System Trace statistic and its control difference of at most 0.007 ms, below the brief's 0.02 ms threshold.
3. Fulfillment finding, untracked new work. New ticket ter-0y0z (under ter-pq5y) looks for the cause of the 0.115 ms that the exposure pass costs more in a frame that runs the haze pass. The exposure row of the cost table in ARCHITECTURE.md now says that the cost comes with the haze pass and names ter-0y0z.
Gaps:
- Reproducibility. The probe and the trace scripts stay uncommitted, because the user prefers to leave out what is not needed yet. The first note describes the probe (SceneBatcher.Prepare drops GpuAtmosphere and airSun on every other recorded frame), the trace (tools/gpu-trace.sh, 200 frames at 1920 x 1080, Release), the pairing (each frame without the record against the mean of its two neighbours with it) and the control, so a later ticket can rebuild them.
- Frame parity without a marker over Earth's air. Accepted as a limit of the station and orbit traces. Over Earth's air, the haze pass records nothing in either kind of frame, so the probe changes only the sun's tint. The static cost of that tint (one length, one exp and one mix per sunlit pixel) agrees with a difference at the noise level. If the parity were wrong there, the decision would still rest on that static cost.
- Rounding of mix(1, 1, s). Shader fact (4) now says that Vulkan does not require a correctly rounded mix, and what a later skip must do about it. No shipped code depends on the fact.
- ARCHITECTURE.md now spends more than two sentences on the decision, because finding 1 adds the trigger. Accepted.
- The ticket title still names a skip. Retitling is the closer's choice, so this stage leaves the title as it is.
No code changed in this stage either.
