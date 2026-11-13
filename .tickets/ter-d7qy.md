---
id: ter-d7qy
status: closed
deps: []
links: []
created: 2026-11-12T23:21:46Z
type: task
priority: 2
assignee: Alexis Purslane
---
# The survey's switch, lag and walk films measure each frame without writing it as a PNG

Measured on 2026-11-12 (Design A, job r-bay-off): building the probe took 45 s, and one 460-frame switch run took 545 s on the RTX 2000 Ada worker, about 1.2 s per frame, while the GPU draws a frame in about 9 ms. The Mac takes about 3.5 minutes for the same run. The time goes to CPU work: switch_script films with every=1, so the renderer reads back and writes every frame as a full-size PNG (f-*.png), and probe_series then opens each PNG in Python to take the region means. So a cloud job pays GPU-worker prices for PNG encoding, and six of nine settle jobs hit the 800 s limit. Fix: the probe computes the per-frame region means itself (on the GPU or from the readback in C#) and writes only that series, a few numbers per frame; PNGs only for the few frames the report keeps (SWITCH_OFF - 1, SWITCH_OFF + 8, SWITCH_ON - 1, SWITCH_ON + 8). Acceptance: the same settle counts as today's path on one switch case, bit for bit or within the series' rounding, and the run time per frame near the GPU time.


## Notes

**2026-11-13T03:27:11Z**

Done. The probe computes each capture's region means from its readback (probe.patch, PngCaptureSink) and writes series.txt; PNGs only for TERTIUS_PROBE_KEEP. Acceptance on bay-sun-off: (1) Mac, every frame both ways from the same run (final probe): the PNG path and the series give the same settle counts, offDraws 49, onDraws 36, whole 27 and 20; the largest difference of a region mean is 3.1e-13 of it, from the order of summation. (2) Cloud GPU worker, series only: the same counts 49 and 36; 14.6 ms per capture against a GPU frame span of 10.0 ms (was about 1.2 s per frame); the whole case, three runs, in 116 s. Cloud cost 0.087 USD. The lag and walk parts read the series too.
