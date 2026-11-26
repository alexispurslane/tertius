# Renderer backlog from the second Blue Danube cut

The reference is the 1080p, 60 fps second cut in `~/Movies/Blue Danube/blue-danube-second-cut.mp4`. The release render used commit `d392c3ae4`. Film times below include the five-second opening card. The [script](../../scripts/blue-danube.json) names each shot. The original render conditions, master digest, GPU timings and export checks are in `master-second-cut/` beside the MP4.

## Work in priority order

| Order | Work | Acceptance evidence |
|---:|---|---|
| 1 | Exposure before bloom | Vary bloom from off to strong. The image halo should change while the meter histogram and exposure stay identical, including on subsequent frames. |
| 2 | Bloom from unclipped source brightness | At fixed exposure, measure the halos of two sources bright enough to clip to the same stored HDR value. Also check a sunlit hull against a genuinely luminous source. |
| 3 | Immediate exposure on a cut | Compare the first displayed frame after each cut to the settled destination view. Use the cut outside at 0:28.60 and back inside at 0:29.35. Track gradual adaptation on a continuous walk through the hatch. |
| 4 | Grain on rough surfaces at grazing incidence | Check multiple cabin and berth angles, including the deck at 7:27. Vary the light angle separately from the viewing angle. Compare direct and indirect lighting at the same exposure. |
| 5 | Distant lights boiling | Identify a distant source whose brightness or footprint fluctuates. Track it through still and moving camera sequences, then isolate the first unstable pass. |
| 6 | Jagged nearby shadow edges from distant lights | Measure the shadow texel size on the nearby receiver and peak GPU memory use on Linux Runpod. Compare the edge quality and memory cost of candidate changes before choosing a shadow resolution. |
| 7 | GPU memory footprint | Measure peak device-local use and its largest contributors. Choose a smaller-GPU budget from the results and verify savings at matched quality. Share the measurements with the shadow ticket. |

## Measurements to repeat

The timing report covers 32,189 shown source frames. At 1920 × 1080 output and 1280 × 720 internal resolution on a Linux RTX 2000 Ada, GPU rendering from deformation through outlines took 11.03 ms at the median and 17.46 ms at the 95th percentile. The run averaged 11.75 recorded frames per wall-clock second. Judge frame time and FPS exclusively on Linux Runpod. Use the Mac for rendering and visual checks. Compare GPU time over the same passes with readback excluded. Report wall time separately. The earlier [capture profile](../research/t88-hjjm-film-profile/README.md) found that time-lapse warm-up draws accounted for most of the recording time in the first cut.

The worker reported 16,380 MiB of GPU memory in total. The film log recorded clocks and utilization. Peak memory use and free headroom still need measurement before sizing the shadow maps.

Review sampled the edited film at six-second intervals and checked shorter sequences at the cuts, approach, berth and lift. In 12 frames beginning at 28.65 s, 48.8% of the decoded pixels reached grayscale level 250 or higher on a 0–255 scale after resizing to 320 × 180. At 28.15 s the share was 2.2%. These values describe the displayed shots. The different camera compositions and compressed output cannot identify the responsible pass. Use original frames for renderer acceptance. The [material comparisons](../research/blue-danube-materials/README.md) give controls for surface detail.

## Work outside the film queue

| Responsibility | Open work |
|---|---|
| Supported Vulkan devices and window sizes | Linux test failures, nonfinite history samples, descriptor pool counts, recovery after a failed frame, fixed window dimensions and framing |
| Capture and edit | Time-lapse warm-up cost, editor input frame rate |
| Build and verification | GPU test output collisions, GPU lock waits counted as mutant kills, Mac Vulkan packaging, repeatable normal baking |

The film review epic tracks the seven priorities above. The closed research tickets retain the earlier results, including rejected candidates and work that stopped at a branch checkpoint. Reopen a technique when a current reproduction identifies the signal it would change.
