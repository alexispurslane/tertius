#!/usr/bin/env bash
# Tool code (tools/README.md, "GPU calibration"): one run of the Release demo in a window, under the GPU lock through
# locked.sh and beside gpu-clock, with MoltenVK's performance log of the presented frames. A window opens on the display
# for the length of the run. A sampler beside them reads the GPU's busy share, "Device Utilization %" of the I/O
# registry's AGXAccelerator, every 0.5 s. Run it from the worktree root, in the dev shell, after the builds of
# tools/README.md, "GPU calibration":
#   nix develop --command bash tools/gpu-calibration/window.sh <script> <width>x<height> <out folder> <name>
# It writes <out>/<name>.log, the busy samples into <out>/<name>.busy and the script's captures into <out>/<name>/, and
# prints one summary line of the run:
#   swapchain: the size of the swapchain images, which macOS can make smaller than the window that the script asks for.
#   run_mhz: gpu-clock's mean clock over the active time of the whole run.
#   steady_mhz, steady_active: the mean clock and the mean share of each sample that the GPU was not off, over the
#     samples from 5 s after the start, after the window opened and the governor settled.
#   steady_busy: the mean busy share, in percent, over the busy samples from 5 s after the start.
#   fps: the presented frames per second between MoltenVK's first and last log of every 60 frames.
#   frame_ms, drawable_ms: MoltenVK's mean frame interval and mean wait for a drawable over the whole run.
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

bash tools/gpu-calibration/locked.sh "$log" bash -c "$(declare -f measured_run); measured_run \"\$@\"" window "$busy" \
    captures/3ug8/bin/gpu-clock 100 env MVK_CONFIG_PERFORMANCE_TRACKING=1 \
    MVK_CONFIG_PERFORMANCE_LOGGING_FRAME_COUNT=60 MVK_CONFIG_LOG_LEVEL=3 \
    src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo --script "$script" --out "$out/$name" --size "$size"
code=$?

awk -v name="$name" -v code="$code" '
    /Created [0-9]+ swapchain images with size/ { match($0, /\([0-9]+, [0-9]+\)/); swapchain = substr($0, RSTART + 1, RLENGTH - 2); sub(", ", "x", swapchain) }
    /^run seconds=/ { for (i = 1; i <= NF; i++) if ($i ~ /^mhz=/) run_mhz = substr($i, 5) }
    /^sample t=/ {
        for (i = 1; i <= NF; i++) {
            if ($i ~ /^t=/) t = substr($i, 3)
            if ($i ~ /^active=/) active = substr($i, 8)
            if ($i ~ /^mhz=/) mhz = substr($i, 5)
        }
        if (t >= 5) { samples++; active_sum += active; weighted += active * mhz }
    }
    /Performance statistics reporting every: 60 frames/ {
        match($0, /elapsed time: [0-9.]+/); elapsed = substr($0, RSTART + 14, RLENGTH - 14)
        if (logs == 0) first = elapsed
        last = elapsed; logs++
    }
    /Frame interval +avg:/ { frame_ms = $5 }
    /Retrieve a CAMetalDrawable +avg:/ { drawable_ms = $6 }
    /^busy t=/ {
        split($2, time, "="); split($3, percent, "=")
        if (time[2] >= 5 && percent[2] != "") { busy_samples++; busy_sum += percent[2] }
    }
    END {
        printf "%s exit=%s swapchain=%s run_mhz=%s steady_mhz=%.1f steady_active=%.3f steady_busy=%.1f fps=%.1f frame_ms=%s drawable_ms=%s\n",
            name, code, swapchain, run_mhz, (active_sum > 0 ? weighted / active_sum : 0), (samples > 0 ? active_sum / samples : 0),
            (busy_samples > 0 ? busy_sum / busy_samples : 0), (logs > 1 ? 60 * (logs - 1) / (last - first) : 0), frame_ms,
            drawable_ms
    }
' "$log" "$busy"
exit "$code"
