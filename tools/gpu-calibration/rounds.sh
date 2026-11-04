#!/usr/bin/env bash
# Tool code (tools/README.md, "GPU calibration"): one round of the calibration runs. Each run takes the GPU lock alone
# through locked.sh, which records uptime at its start and its end, and runs beside gpu-clock, which samples the GPU's
# clock every 100 ms. The order of the runs rotates by one run per round, so that the load of the machine and the
# warmth of the GPU fall on every run alike. Run it from the worktree root, in the dev shell, after the builds of
# tools/README.md, "GPU calibration":
#   nix develop --command bash tools/gpu-calibration/rounds.sh <round> <out folder>
# It writes <out>/<round>-<run>.log for each run, and one line per run to stdout, which analyze.py does not need.
set -uo pipefail

round=${1:?round number}
out=${2:?out folder}
bin=captures/3ug8/bin
tests=tests/Tertius.Rendering.Tests/bin/Release/net11.0/Tertius.Rendering.Tests
demo=src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
deck=captures/3ug8/deck-120.json
mkdir -p "$out"

# The demo's headless run of 120 frames of the deck at 1280 x 720, as the spikes' timed runs ran it.
demo_run() {
    "$demo" --script "$deck" --headless --out "$1" --size 1280x720 --video none
}

# The demo's run, with the short FMA probe every 20 ms beside it until the demo ends.
demo_with_pulse() {
    "$bin/probes" pulse 20 600 > "$1.pulse" 2>&1 &
    local pulse=$!
    demo_run "$1"
    local code=$?
    kill "$pulse"
    wait "$pulse" 2> /dev/null
    return "$code"
}

run() {
    local name=$1
    shift
    local log="$out/$round-$name.log"
    bash tools/gpu-calibration/locked.sh "$log" "$@"
    echo "round=$round run=$name exit=$? $(head -1 "$log")"
}

export TERTIUS_GPU_CALIBRATION=1 TERTIUS_FSR_TIMING=1
names=(cold-metal cold-vulkan demo demo-pulse sustained-metal sustained-vulkan probes-metal probes-vulkan
    probes-vulkan-fastmath-never probes-vulkan-no-argument-buffers metalfx fsr)
n=${#names[@]}
for ((i = 0; i < n; i++)); do
    name=${names[$(((i + round) % n))]}
    case $name in
        # The cold runs wait 10 s with the lock held, so that no other run wakes the GPU first.
        cold-metal) run "$name" bash -c "sleep 10 && $bin/gpu-clock 100 $bin/probes cold 30" ;;
        cold-vulkan) run "$name" bash -c "sleep 10 && $bin/gpu-clock 100 $tests -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.GpuCalibrationTests.TheFmaProbeReportsEachDispatchFromTheFirstAfterIdle" ;;
        demo) run "$name" "$bin/gpu-clock" 100 "$demo" --script "$deck" --headless --out "$out/$round-demo" \
            --size 1280x720 --video none ;;
        demo-pulse) run "$name" "$bin/gpu-clock" 100 bash -c \
            "$(declare -f demo_run demo_with_pulse); bin=$bin demo=$demo deck=$deck demo_with_pulse $out/$round-demo-pulse" ;;
        sustained-metal) run "$name" "$bin/gpu-clock" 100 "$bin/probes" sustain 8 30 ;;
        sustained-vulkan) run "$name" "$bin/gpu-clock" 100 "$tests" -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.GpuCalibrationTests.TheFmaProbeReportsItsTimeAfterSecondsOfFullLoad ;;
        probes-metal) run "$name" bash -c "for p in fma fma-rolled texture-cached texture-stream bandwidth; do $bin/probes \$p 30 || exit 1; done" ;;
        probes-vulkan) run "$name" "$tests" -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.GpuCalibrationTests.EachProbeReportsItsGpuTimeAndItsShareOfThePeak ;;
        probes-vulkan-fastmath-never) run "$name" env MVK_CONFIG_FAST_MATH_ENABLED=0 "$tests" -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.GpuCalibrationTests.EachProbeReportsItsGpuTimeAndItsShareOfThePeak ;;
        probes-vulkan-no-argument-buffers) run "$name" env MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS=0 "$tests" -noColor \
            -showLiveOutput -method Tertius.Rendering.Tests.GpuCalibrationTests.EachProbeReportsItsGpuTimeAndItsShareOfThePeak ;;
        metalfx) run "$name" "$bin/metalfx" matched ;;
        fsr) run "$name" "$tests" -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTime ;;
    esac
done
