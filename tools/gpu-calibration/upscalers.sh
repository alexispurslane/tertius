#!/usr/bin/env bash
# Tool code (tools/README.md, "GPU calibration"): one round of the upscaler runs, each under the GPU lock through
# tools/gpu-lock.sh and beside gpu-clock through tools/gpu-clock.sh, in an order that rotates by one run per round. Each
# run prints the markers of its timed span:
#   fsr          FSR's timing test, Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTime, which waits for
#                each frame.
#   fsr-batch    FSR's batched measurement, 8 frames per submission, which prints the markers of its timed span.
#   metalfx      MetalFX on the same inputs, each frame in its own command buffer.
#   metalfx-gap  The same with a gap of 0.6 ms after each frame, so that the GPU idles as in FSR's timing test.
# Run it from the worktree root, in the dev shell, after the builds of tools/README.md, "GPU calibration":
#   nix develop --command bash tools/gpu-calibration/upscalers.sh <round> <out folder>
# It writes <out>/<round>-<run>.log, and prints one line per run. analyze.py upscalers <out folder> prints the table.
set -uo pipefail

round=${1:?round number}
out=${2:?out folder}
bin=${GPU_CALIBRATION_BIN:-captures/gpu-calibration/bin}
tests=tests/Tertius.Rendering.Tests/bin/Release/net11.0/Tertius.Rendering.Tests
mkdir -p "$out"

names=(fsr fsr-batch metalfx metalfx-gap)
n=${#names[@]}
for ((i = 0; i < n; i++)); do
    name=${names[$(((i + round) % n))]}
    case $name in
        fsr) command=(env TERTIUS_FSR_TIMING=1 bash tools/gpu-clock.sh "$tests" -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTime) ;;
        fsr-batch) command=(env TERTIUS_FSR_TIMING=1 bash tools/gpu-clock.sh "$tests" -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTimePerFrameWhenEightFramesShareASubmission) ;;
        metalfx) command=(bash tools/gpu-clock.sh "$bin/metalfx" matched) ;;
        metalfx-gap) command=(bash tools/gpu-clock.sh "$bin/metalfx" matched 0.6) ;;
    esac
    log="$out/$round-$name.log"
    bash tools/gpu-lock.sh "$log" "${command[@]}"
    echo "round=$round run=$name exit=$? $(grep -o 'median [0-9.]* ms' "$log" | head -1) $(grep '^GPU clock over the timed span' "$log")"
done
