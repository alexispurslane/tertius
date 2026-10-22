---
id: ter-zzyr
status: open
deps: []
links: []
created: 2026-10-11T20:46:11Z
type: feature
priority: 2
assignee: Alexis Purslane
tags: [rendering, long-term]
---
# Add automatic exposure that adapts over time, like an eye entering and leaving the dark

The user asked for eye adaptation in the HDR to SDR pass. Today the exposure is fixed: VulkanRenderer.Exposure (default 1) multiplies the HDR image in tonemap.frag before the ACES fitted curve, and nothing changes it from frame to frame.

Wanted: an exposure that follows the scene's brightness over time. When the camera enters the dark, for example the orbit scene's night pass or a dim ship interior, the exposure rises slowly, so dark things become distinguishable. When the camera leaves into bright light, the image is blown out at first, and then the exposure falls until it settles. Eyes adapt faster to light than to dark, so the two directions can use different time constants.

Design to decide (two designs, then the user chooses): how to measure the scene's brightness (for example a log-luminance histogram or an average from a compute pass or a mip chain of the HDR image, with the brightest and darkest percentiles ignored, so that the sun's disc alone does not set it); how to smooth it over real time (an exponential approach with separate up and down rates, frame-rate independent); the clamp on the exposure range; how it combines with the manual Exposure value as a bias; and whether the measurement is read back to the CPU or stays on the GPU. Related: ter-63wr (bloom), which also works on the HDR image before tone mapping.

Per the project rules, it is a temporal effect, so the deterministic switch in RendererOptions turns it off for snapshot tests, and its cost per frame is measured and recorded.

## Acceptance Criteria

With automatic exposure on, a camera that moves from the orbit scene's day side into Earth's shadow sees the image brighten over a few seconds of real time until the dark side shows detail. Back in sunlight, it sees the image overexposed at first, then settle. The rates and the exposure range are settings, the effect is switchable and off in deterministic tests, and its cost is measured.


## Notes

**2026-10-22T00:09:45Z**

The lights design (ter-bpvl, docs/plans/local-light-shadows.md, "Exposure and the HDR image" and "Reach") depends on this ticket and adds two needs. First, lights move to SI units, so scene brightness spans from about 1,361 W/m² in sunlight down to about 0.01 W/m² in dim rooms. The HDR and emissive targets are half floats (largest value 65,504) and the Sun's disc is about 2e7 W/(m²·sr), so every pass that writes them writes pre-exposed radiance: radiance times the exposure of the previous rendered frame, clamped to 65,504. This ticket owns that exposure value and gives it to those passes, and tone mapping then multiplies by no exposure. Second, the lower clamp of the exposure sets E_cut, the irradiance below which a light is culled: a white matte surface at the brightest allowed exposure turns E_cut into 1/256 of output white.
