---
id: ter-zzyr
status: in_progress
deps: [ter-ptft]
links: []
created: 2026-10-11T20:46:11Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, long-term]
---
# Add automatic exposure that adapts over time, like an eye entering and leaving the dark

Design: docs/plans/auto-exposure.md

The user asked for eye adaptation in the HDR to SDR pass. Today the exposure is fixed: VulkanRenderer.Exposure (default 1) multiplies the HDR image in tonemap.frag before the ACES fitted curve, and nothing changes it from frame to frame.

Wanted: an exposure that follows the scene's brightness over time. When the camera enters the dark, for example the orbit scene's night pass or a dim ship interior, the exposure rises slowly, so dark things become distinguishable. When the camera leaves into bright light, the image is blown out at first, and then the exposure falls until it settles. Eyes adapt faster to light than to dark, so the two directions can use different time constants.

Design to decide (two designs, then the user chooses): how to measure the scene's brightness (for example a log-luminance histogram or an average from a compute pass or a mip chain of the HDR image, with the brightest and darkest percentiles ignored, so that the sun's disc alone does not set it); how to smooth it over real time (an exponential approach with separate up and down rates, frame-rate independent); the clamp on the exposure range; how it combines with the manual Exposure value as a bias; and whether the measurement is read back to the CPU or stays on the GPU. Related: ter-63wr (bloom), which also works on the HDR image before tone mapping.

Per the project rules, it is a temporal effect, so the deterministic switch in RendererOptions turns it off for snapshot tests, and its cost per frame is measured and recorded.

## Acceptance Criteria

With automatic exposure on, a camera that moves from the orbit scene's day side into Earth's shadow sees the image brighten over a few seconds of real time until the dark side shows detail. Back in sunlight, it sees the image overexposed at first, then settle. The rates and the exposure range are settings, the effect is switchable and off in deterministic tests, and its cost is measured.


## Notes

**2026-10-22T00:09:45Z**

The lights design (ter-bpvl, docs/plans/local-light-shadows.md, "Exposure and the HDR image" and "Reach") depends on this ticket and adds two needs. First, lights move to SI units, so scene brightness spans from about 1,361 W/m² in sunlight down to about 0.01 W/m² in dim rooms. The HDR and emissive targets are half floats (largest value 65,504) and the Sun's disc is about 2e7 W/(m²·sr), so every pass that writes them writes pre-exposed radiance: radiance times the exposure of the previous rendered frame, clamped to 65,504. This ticket owns that exposure value and gives it to those passes, and tone mapping then multiplies by no exposure. Second, the lower clamp of the exposure sets E_cut, the irradiance below which a light is culled: a white matte surface at the brightest allowed exposure turns E_cut into 1/256 of output white.

## Design decisions (user, 2026-10-25)

Exposure and the tone curve (ter-2t5o) share one model: radiance, then the exposure, then the tone curve, then the sRGB encoding. Two designs were shown in the chat. The user chose:

- The exposure is decided all on the GPU: a compute pass builds a histogram of log luminance, and a second compute pass moves the exposure in a GPU buffer. The renderer gains compute pipelines for it.
- The shading passes write pre-exposed light now, in this ticket: every pass that writes the HDR image or the emissive target multiplies by the exposure and clamps to 65,504, and the tone map multiplies by no exposure (docs/plans/local-light-shadows.md, "Exposure and the HDR image").

Shared by both designs, and settled with them:

- The exposure setting is a union: `FixedExposure(Stops)` or `AutoExposure(...)`. Today's linear `Exposure` becomes the fixed case. Tests and the coming snapshot tests use the fixed case. One stop doubles the image's brightness.
- The meter counts only surface pixels: a pixel whose G-buffer distance is 0 is sky and does not count, so a ship in front of black space is not blown out and the share of sky in the frame does not move the exposure. A view with no surface keeps its exposure.
- The meter drops the darkest and the brightest shares of the surface pixels, so a lamp or a glint alone does not set the exposure.
- The target maps the trimmed mean of log luminance to middle gray, 0.18.
- The exposure approaches its target in stops, frame-rate independent, with two time constants: into the dark slower (3 s by default), into the light faster (1 s by default). A clamp holds it between a lowest and a highest number of stops. The time constants, the clamp, the trim shares and an exposure compensation in stops are settings.
- The clock is the time between presented frames: the wall clock in a window, and the film's frame time in a headless run, so a headless film gives the same images every run.
- The switch, the cost measured with the GPU timers, and ARCHITECTURE.md, as the epic's rules say.
