# Renderer backlog from the second Blue Danube cut

The reference is the 1080p, 60 fps second cut in `~/Movies/Blue Danube/blue-danube-second-cut.mp4`. The release render used commit `d392c3ae4`. Film times below include the five-second opening card. The [script](../../scripts/blue-danube.json) names each shot. The original render conditions, master digest, GPU timings and export checks are in `master-second-cut/` beside the MP4.

## Work in priority order

| Order | What the viewer sees | How to isolate it and judge a change |
|---|---|---|
| 1. [Cabin and daylight exposure](../../.tickets/ter-fl8r.md) | At 0:28.60 the cut from the seat to the exterior washes out the hull and glazing. At 0:29.35 the cabin goes dark. During `move-out` (5:47.55 to 6:39.73), the lit hub can fill the windscreen for seconds. | Match the camera cuts and first frames. Compare inherited and settled exposure, then the image before and after bloom. Check the pilot, window frames and sunlit hull at the same poses. The sustained windscreen shot tests whether correcting the cut alone suffices. |
| 2. [White discs on approach](../../.tickets/ter-2erh.md) | At 3:48 to 3:50, several large white discs cross the port in `glide-window`. Their size and position change between samples 0.12 s apart. | Identify the producing draw or emitter with matched on/off captures. Check size, depth, brightness and bloom while the dock stays in frame. Preserve the appearance of the launch exhaust. |
| 3. [Berth deck grain](../../.tickets/ter-xzj7.md) | At 7:27 the shaded floor fills the lower half of `deck`. Speckle is conspicuous near the ramp and cargo. | Compare the original uncompressed renderer output with isolated direct, diffuse and reflected light and surface normals. Follow the same deck patch and its reflection through consecutive frames. Keep the panel joints and compare the settled brightness as well as the variation. |

These are three independent reproductions. The order expresses visual priority. The respective tickets own the cause and the fix once measured. A camera, authored material or emitter can own a defect seen on screen.

## Measurements to repeat

The timing report covers 32,189 shown source frames. At 1920 × 1080 output and 1280 × 720 internal resolution on a Linux RTX 2000 Ada, GPU rendering from deformation through outlines took 11.03 ms at the median and 17.46 ms at the 95th percentile. The run averaged 11.75 recorded frames per wall-clock second. Compare GPU time over the same passes with readback excluded. Report wall time separately. The earlier [capture profile](../research/t88-hjjm-film-profile/README.md) found that time-lapse warm-up draws accounted for most of the recording time in the first cut.

Review sampled the edited film at six-second intervals and checked shorter sequences at the cuts, approach, berth and lift. In 12 frames beginning at 28.65 s, 48.8% of the decoded pixels reached grayscale level 250 or higher on a 0–255 scale after resizing to 320 × 180. At 28.15 s the share was 2.2%. These values describe the displayed shots. The different camera compositions and compressed output cannot identify the responsible pass. Use original frames for renderer acceptance. The [material comparisons](../research/blue-danube-materials/README.md) give controls for surface detail.

## Work outside the film queue

| Responsibility | Open work |
|---|---|
| Supported Vulkan devices and window sizes | [Linux test failures](../../.tickets/t70-0pr5.md), [nonfinite history samples](../../.tickets/ter-dbzx.md), [descriptor pool counts](../../.tickets/ter-byim.md), [recovery after a failed frame](../../.tickets/ter-nua7.md), [fixed window dimensions and framing](../../.tickets/t10-yb2s.md) |
| Capture and edit | [Time-lapse warm-up cost](../../.tickets/t88-hjjm.md), [editor input frame rate](../../.tickets/ter-onkj.md) |
| Build and verification | [GPU test output collisions](../../.tickets/ter-ddk8.md), [GPU lock waits counted as mutant kills](../../.tickets/tc-5azv.md), [Mac Vulkan packaging](../../.tickets/t2-q0uk.md), [repeatable normal baking](../../.tickets/ter-b4k3.md) |

The [film review epic](../../.tickets/ter-buzm.md) tracks the three visible defects. The closed research tickets retain the earlier results, including rejected candidates and work that stopped at a branch checkpoint. Reopen a technique when a current reproduction identifies the signal it would change.
