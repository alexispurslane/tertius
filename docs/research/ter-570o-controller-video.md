# ter-570o: real demo video with the production resolution controller

Captured on November 5, 2026, on the Apple M1 Max development Mac. The first bounded attempt delivered the requested ten-second video. No second attempt ran.

## Deliverable and scope

The video contains 300 frames at 30 playback frames per second. Its actual output stays at 1920x1080 pixels. The production controller changes the internal dimensions 111 times across 25 distinct sizes. Of those changes, 92 occur after the first second of playback. These are not only startup changes.

The artifact root is this ticket worktree's `captures/ter-570o/evidence/`. Its absolute path is `/Users/alexispurslane/.t3/worktrees/tertius/ter-570o-fsr-controller-video/captures/ter-570o/evidence/`.

| Artifact | Location under the artifact root |
|---|---|
| User-facing video | `ter-570o-fsr-controller.mp4` |
| One image-paired observation per rendered frame | `frames.jsonl` |
| Dimensions, frame pairing, clocks, overhead and controller evidence | `analysis.json` |
| Actual renderer, controller, framebuffer and encoder configuration | `configuration.json` |
| Script selection and startup policy | `selection.json` |
| Decoded frame count, dimensions, codec and duration | `ffprobe.json` |
| Source, binary and observer SHA-256 hashes | `provenance.txt` and `artifact-sha256.txt` |
| Seventeen original frames, before the overlay | `source/frame-*.png` |
| Fifteen frames decoded from the delivered video | `encoded/frame-*.png`, with `encoded/frame-map.json` |
| Adjacent source and decoded motion samples | `source-motion-sheet.png` and `encoded-motion-sheet.png` |
| Samples across the full clip | `full-clip-motion-sheet.png` |
| Charts and three captioned decoded stills | `presentation.html` |
| Build, capture and inspection queue logs | `build.log`, `capture-attempt-1.log`, `inspection.log`, `motion-inspection.log` |

The video's SHA-256 hash is `ff206007a37955ddf15c8b59b3b331185d9e31b842c680fa5cb156088465a0a8`. Its encoded size is 4,914,051 bytes. The full assignment is preserved verbatim in [ter-570o-capture-assignment.md](ter-570o-capture-assignment.md).

This branch changes only ticket notes and reports. It does not change production or test code, import prerequisites, run suites, merge, push or close a ticket. The parent owns integration.

## Source and executable identity

The T3-bound worktree starts clean at `23d603077ac306722cd1c4e55f8459480fc8b1e8`, on branch `ter-570o-fsr-controller-video`. The working code/build-input tree is `ba5127acef21489cc12c01efc090351788f7c0ef`, from `tools/code-tree.sh`.

The reused Release demo binaries come from `/Users/alexispurslane/.t3/worktrees/tertius/ter-s7c6-fsr31-dynamic-resolution/src/Tertius.Demo/bin/Release/net11.0/`. Their committed-code stamp names the same build-input tree. Every copied DLL was compared byte for byte with that read-only input. No command writes or builds in that checkout.

| Assembly | SHA-256 |
|---|---|
| `Tertius.Core.dll` | `3b1978d2e619a1991ea7cb0cb6c2e83de3d4330fb533aecf1b511eca7c04695c` |
| `Tertius.Demo.dll` | `d87f2781025fbf5237e5bbfea88ec8edf50391f3de7e6cf5945aa94a72657506` |
| `Tertius.Host.dll` | `8ff40fa7f5208f293def02f28b4a312f7b7bb8687cca6c91352caf8529688353` |
| `Tertius.Physics.dll` | `17273df6c135263cca03327517824ab84e4036f811001518faf5128abc5c20fe` |
| `Tertius.Rendering.dll` | `fc0aac6ebe592b7752a2ab96ab977c754bbbf9ea7bfd4844b1daab02c63db0ba` |
| `Tertius.Simulation.dll` | `23611a20dd2e246e4a6613331a60c90c2a4acbfbb5af637e6647cf8a94fea8e6` |

The small observer lives at `captures/ter-570o/observer/Program.cs`, with its project beside it. It references existing DLLs, not production projects. It uses the existing benchmark assembly's internal observation access. Reflection reads the controller's recorded scale, last measured frame and the production render clock. It never writes controller state or calls its decision methods.

The native libraries and all copied DLLs have hashes in `provenance.txt`. The owned observer, analysis and presentation sources remain with the ignored capture artifacts. Their hashes are retained too.

## Actual production configuration

The observer creates a real Vulkan window with `VulkanRenderer.CreateForWindow`. It builds the real `DemoScenes` scene and runs the real `ScriptRunner`. It does not use a headless renderer or reproduce the controller.

| Setting | Actual value |
|---|---|
| Temporal upscaling | `Fsr3Upscaling.Default`, AMD FSR 3.1.4 |
| Scale policy | Production `FrameTimeTarget.Default` |
| Requested GPU frame time | 16.666666 milliseconds |
| Controller budget | 90% of that target, approximately 15 milliseconds |
| Allowed scale per axis | 0.5 through 1.0 |
| FSR sharpness | 0, unchanged default |
| Frames in flight | 2 |
| Captures in flight | False, unchanged default. Each captured frame waits for its fence and readback. |
| Presentation | VSync enabled, unchanged default |
| Logical window | 1920x1080 pixels as reported by the window library |
| Actual framebuffer and captured image | 1920x1080 pixels in every recorded frame |
| Impostors | Disabled, matching the normal scripted-window host |
| Screen tracing | Unchanged default: roughness limit 0.5, reflection walk limit 256, diffuse walk limit 62 |
| Lighting, exposure, bloom, environment, shadows and history | Unchanged production defaults |
| Validation layers | Disabled, unchanged production default. This is not a validation-layer run. |

The window's logical size was adjusted only during initial setup to obtain the requested actual framebuffer. The renderer was created afterward. Neither output dimensions nor internal dimensions were scripted during capture. No load was added to provoke controller changes. Capture and evidence overhead still affect the run, as described below.

## Script section, clocks and history

The input is the committed `scripts/demo-exit.json`, SHA-256 `78b477be40f6585e8e37a868c5d6e4bc93701cfa01e54cfec49cee6ac9a5368d`. Its original cabin-camera selection, `LeaveShip` event and first 399 simulation ticks run without drawing. The original deck-camera selection follows. The observer then captures the first 600 deck ticks, once every two ticks.

The simulation rate remains 60 ticks per second. Captured ticks run from 401 through 999. Snapshot times advance from 6.683333333333334 through 16.65 simulation seconds. The selected section spans ten simulation seconds, including the interval before its first captured image.

No rendered warm-up was discarded. The first captured draw starts the normal window history and controller. History resets only on frame 0. Internal-size changes cause no further recorded resets. The option `WarmUpFrames=32` remains unchanged, but the production still-warm-up rule does not add draws to a window.

The renderer retains its production `WallClock`, not the headless `FilmClock`. Per-frame records retain its preceding wall-clock value, monotonic completion time, snapshot time, simulation tick, draw index, reset and exposure reading. The monotonic clock has 1,000,000,000 ticks per second.

## Controller and frame-pairing evidence

Each of the 300 images has the same submitted frame number, GPU-feedback frame number and last accepted controller measurement number. Each image has that draw's actual internal dimensions and the production controller's recorded scale. Every next draw uses the preceding recorded post-measurement scale. The FSR upscaling stage appears in every frame's GPU timings.

| Rendered frame number | Internal dimensions, pixels | Selected production scale | GPU-feedback frame number |
|---|---|---|---|
| 0 | 1920x1080 | 1.0 | 0 |
| 1 | 1680x944 | 0.875 | 1 |
| 2 | 1496x840 | 0.78125 | 2 |

The smallest observed internal dimensions are 968x544 pixels. The largest are 1920x1080 pixels. The selected scale ranges from 0.5069065 to 1.0. Internal dimensions continue to move after startup. `analysis.json` lists all 111 changes, including their video times and preceding GPU-feedback frame numbers.

Each record keeps every pass's diagnostic name, production stage classification and GPU milliseconds. These retain the classifications and frame numbers used by the real controller. The recorded GPU span is not the FPS counter. The inspection script also reconstructs all displayed FPS values from the saved completion timestamps.

## FPS definition and capture overhead

Render FPS means observed completed captured frames per real wall-clock second. For each image, its completion timestamp is taken at the capture sink's handover, after its fence and readback complete. The counter counts timestamps in the preceding one-second interval, including that image. Before one second elapses, its denominator is the elapsed capture time instead.

The counter includes simulation, CPU preparation, GPU work, presentation waits, readback and preceding overlay, image-save, pipe and journal work. It is not 30-fps encoded playback, inverse GPU milliseconds or uncaptured live-game performance.

The 300th completion occurs 14.472135 real seconds after capture starts. The full capture, including the final evidence save and encoder finish, takes 15.179683 seconds. Playback remains exactly ten seconds. The captured completion rate is therefore distinct from playback speed.

| Measured quantity | Result |
|---|---|
| Displayed one-second Render FPS | Minimum 2, median 45, maximum 47 |
| GPU span, not Render FPS | Median 14.589208 milliseconds, maximum 65.369834 milliseconds |
| Overlay and image copy | Median 1.118042 milliseconds per frame |
| Encoder pipe write | Median 1.858104 milliseconds per frame |
| Seventeen representative original PNG saves | 7.830282 seconds total |

The original PNG saves cause pronounced dips in the displayed counter near the saved samples. This is retained, not hidden. Synchronous capture leaves the GPU idle between draws. This clip is not an uncaptured performance benchmark. Its production exposure clock also follows those real pauses, not encoded playback time.

The encoder reads RGBA images at 1920x1080 pixels. It writes H.264 with `libx264`, `veryfast`, CRF 18, two encoder threads, `yuv420p` and MP4 fast-start metadata. `ffprobe` reads back 300 frames, 30/1 playback FPS and exactly 10.000000 seconds. No audio is added.

## Image and motion inspection

Inspection opened the original and decoded frames at the start, midpoint and end. It also opened adjacent-frame sheets for frames 0-4, 148-152 and 295-299. A decoded sheet samples the whole clip every ten frames. This is a source/decoded frame-sequence inspection, not a claim of a separate human real-time playback review.

- The passenger moves from the glass enclosure across the deck. Glass framing, narrow roof edges and floor reflections remain visible.
- The first frame is darker and more patterned than later frames. Exposure rises from 0 to about 1.6 stops during startup. The source already contains this change.
- The sampled passenger edges show no obvious long detached trail. Fine edges remain soft at the smaller internal sizes. The samples do not establish freedom from all brief motion defects.
- No blank frame, output-size change or history reset appears at the recorded internal-size changes. Sampled glass framing does not show a large size-change discontinuity.
- A strong blue-white patch enters the upper-left opening near the end. The floor becomes much noisier and brighter. Both original and decoded samples show it. Its cause was not diagnosed or changed in this capture task.
- H.264 preserves those visible limitations. The white counter text remains legible over the translucent panel. This is the production main pipeline, not the independent noise prototype.

## Resource limits and command record

One assigned heavy allocation was used. Every build, render and encode ran through `nix develop` and the unmodified `tools/gpu-lock.sh` queue. Commands ran one at a time and completed before handback. No queue or allocation was bypassed.

The observer's only build took about one second and rebuilt no production project. The capture queue wait was 14 seconds. The build, capture and primary inspection logs retain the queue's start and end load records.

Free disk was about 6.67 GiB before the build. The guard required at least 5 GiB after queue acquisition and before every captured frame. Disk stayed above the guard. New evidence, including the small observer build, stays below 0.1 GiB, far below the 1 GiB limit. No other worker's files were removed.

The full-clip sheet was written successfully before an optional chart-print command failed with a Python quoting error. A separate saved presentation script then produced the charts. No new renderer attempt or media re-encode was needed for that correction. All command exit states are retained honestly in the logs.

The replay commands use the existing owned files:

```bash
nix develop --command bash tools/gpu-lock.sh captures/ter-570o/evidence/build.log dotnet build captures/ter-570o/observer/CaptureObserver.csproj -c Release
nix develop --command bash tools/gpu-lock.sh captures/ter-570o/evidence/capture-attempt-1.log dotnet captures/ter-570o/observer/bin/Release/net11.0/Tertius.Benchmarks.dll "$PWD" "$PWD/captures/ter-570o/evidence"
nix develop --command python3 -I captures/ter-570o/analyze.py captures/ter-570o/evidence
```

The actual shell wrappers also checked free disk after the queue wait. Do not rerun capture without a new allocation. These commands overwrite the owned outputs.

No full suites, coverage, mutation runs, review cycle, children or workflows ran. Prior integration validation is input provenance, not a claim that this worker repeated it. This Mac capture does not establish Linux behavior or uninstrumented performance. `ter-570o` remains in progress for parent integration and closure.
