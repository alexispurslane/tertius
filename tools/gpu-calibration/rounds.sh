#!/usr/bin/env bash
# Tool code (tools/README.md, "GPU calibration"): one round of the calibration runs. Each run takes the GPU lock alone
# through tools/gpu-lock.sh, which records uptime at its start and its end, and runs beside gpu-clock through
# tools/gpu-clock.sh, which samples the GPU's clock every 100 ms and prints the clock over the run's timed span. The
# order of the runs rotates by one run per round, so that the load of the machine and the warmth of the GPU fall on
# every run alike. Run it from the worktree root, in the dev shell, after the builds of
# tools/README.md, "GPU calibration", which put the binaries into $GPU_CALIBRATION_BIN, by default
# captures/gpu-calibration/bin, and the deck's script into captures/gpu-calibration/deck-120.json:
#   nix develop --command bash tools/gpu-calibration/rounds.sh <round> <out folder>
# It writes <out>/<round>-<run>.log for each run, and one line per run to stdout, which analyze.py does not need.
set -uo pipefail

round=${1:?round number}
out=${2:?out folder}
bin=${GPU_CALIBRATION_BIN:-captures/gpu-calibration/bin}
tests=tests/Tertius.Rendering.Tests/bin/Release/net11.0/Tertius.Rendering.Tests
demo=src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
deck=captures/gpu-calibration/deck-120.json
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
    bash tools/gpu-lock.sh "$log" "$@"
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
        cold-metal) run "$name" bash -c "sleep 10 && bash tools/gpu-clock.sh $bin/probes cold 30" ;;
        cold-vulkan) run "$name" bash -c "sleep 10 && bash tools/gpu-clock.sh $tests -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.GpuCalibrationTests.TheFmaProbeReportsEachDispatchFromTheFirstAfterIdle" ;;
        demo) run "$name" bash tools/gpu-clock.sh "$demo" --script "$deck" --headless --out "$out/$round-demo" \
            --size 1280x720 --video none ;;
        demo-pulse) run "$name" bash tools/gpu-clock.sh bash -c \
            "$(declare -f demo_run demo_with_pulse); bin=$bin demo=$demo deck=$deck demo_with_pulse $out/$round-demo-pulse" ;;
        sustained-metal) run "$name" bash tools/gpu-clock.sh "$bin/probes" sustain 8 30 ;;
        sustained-vulkan) run "$name" bash tools/gpu-clock.sh "$tests" -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.GpuCalibrationTests.TheFmaProbeReportsItsTimeAfterSecondsOfFullLoad ;;
        probes-metal) run "$name" bash tools/gpu-clock.sh bash -c \
            "for p in fma fma-rolled texture-cached texture-stream bandwidth; do $bin/probes \$p 30 || exit 1; done" ;;
        probes-vulkan) run "$name" bash tools/gpu-clock.sh "$tests" -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.GpuCalibrationTests.EachProbeReportsItsGpuTimeAndItsShareOfThePeak ;;
        probes-vulkan-fastmath-never) run "$name" env MVK_CONFIG_FAST_MATH_ENABLED=0 bash tools/gpu-clock.sh "$tests" \
            -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.GpuCalibrationTests.EachProbeReportsItsGpuTimeAndItsShareOfThePeak ;;
        probes-vulkan-no-argument-buffers) run "$name" env MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS=0 bash tools/gpu-clock.sh \
            "$tests" -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.GpuCalibrationTests.EachProbeReportsItsGpuTimeAndItsShareOfThePeak ;;
        metalfx) run "$name" bash tools/gpu-clock.sh "$bin/metalfx" matched ;;
        fsr) run "$name" bash tools/gpu-clock.sh "$tests" -noColor -showLiveOutput \
            -method Tertius.Rendering.Tests.Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTime ;;
    esac
done
