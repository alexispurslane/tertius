#!/usr/bin/env bash
# Tool code (tools/README.md, "GPU calibration"): one run of the Release demo in a window, under the GPU lock through
# tools/gpu-lock.sh and beside gpu-clock, with MoltenVK's performance log of the presented frames. A window opens on the display
# for the length of the run. A sampler beside them reads the GPU's busy share, "Device Utilization %" of the I/O
# registry's AGXAccelerator, every 0.5 s. Run it from the worktree root, in the dev shell, after the builds of
# tools/README.md, "GPU calibration":
#   nix develop --command bash tools/gpu-calibration/window.sh <script> <width>x<height> <out folder> <name>
# It writes <out>/<name>.log, the busy samples into <out>/<name>.busy and the script's captures into <out>/<name>/, and
# prints one summary line of the run, from `analyze.py window`:
#   swapchain: the size of the swapchain images, which macOS can make smaller than the window that the script asks for.
#   run_mhz: gpu-clock's mean clock over the active time of the whole run.
#   steady_samples, steady_mhz, steady_active: the count of samples, the mean clock and the mean share of each sample
#     that the GPU was not off, over the steady span. The steady span runs from 5 s after the start, after the window
#     opened and the governor settled, to MoltenVK's last log of presented frames, before the demo's teardown.
#   steady_busy: the mean busy share, in percent, over the busy samples of the steady span.
#   fps: the presented frames per second between MoltenVK's first and last log of every 60 frames.
#   frame_ms, drawable_ms: MoltenVK's mean frame interval and mean wait for a drawable over the whole run.
# The binaries of gpu-clock come from $GPU_CALIBRATION_BIN, by default captures/gpu-calibration/bin.
set -uo pipefail

script=${1:?usage: window.sh <script> <width>x<height> <out folder> <name>}
size=${2:?usage: window.sh <script> <width>x<height> <out folder> <name>}
out=${3:?usage: window.sh <script> <width>x<height> <out folder> <name>}
name=${4:?usage: window.sh <script> <width>x<height> <out folder> <name>}
log="$out/$name.log"
busy="$out/$name.busy"
mkdir -p "$out"

# The command under the lock, with the sampler of the busy share beside it until the command ends.
measured_run() {
    local busy=$1
    shift
    local start=$EPOCHREALTIME
    while :; do
        printf 'busy t=%s percent=%s\n' "$(awk -v now="$EPOCHREALTIME" -v start="$start" 'BEGIN { printf "%.3f", now - start }')" \
            "$(ioreg -r -c AGXAccelerator -d 1 -w 0 | grep -o '"Device Utilization %"=[0-9]*' | cut -d= -f2)"
        sleep 0.5
    done > "$busy" &
    local sampler=$!
    "$@"
    local code=$?
    kill "$sampler"
    wait "$sampler" 2> /dev/null
    return "$code"
}

bin=${GPU_CALIBRATION_BIN:-captures/gpu-calibration/bin}
bash tools/gpu-lock.sh "$log" bash -c "$(declare -f measured_run); measured_run \"\$@\"" window "$busy" \
    "$bin/gpu-clock" 100 env MVK_CONFIG_PERFORMANCE_TRACKING=1 \
    MVK_CONFIG_PERFORMANCE_LOGGING_FRAME_COUNT=60 MVK_CONFIG_LOG_LEVEL=3 \
    src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo --script "$script" --out "$out/$name" --size "$size"
code=$?

echo "$name exit=$code $(python3 -I tools/gpu-calibration/analyze.py window "$log" "$busy")"
exit "$code"
