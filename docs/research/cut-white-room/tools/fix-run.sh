#!/usr/bin/env bash
# Tool code of t58-rcd6: one run of the fix's checks with a Release binary folder of Tertius.Demo that holds the
# frame-time probe (frame-times.patch) and the render scale's probe (apply-scale.py). Run it in the dev shell from the
# worktree's root. Headless at 1920 x 1080 with the exposure fixed at 4.08 stops.
#   film   <bin> <out> <script>: the PNG of every capture, under the GPU lock's shared mode; the render scale is the
#          headless default, a constant 2/3
#   timing <bin> <out> <script>: no PNG (--video none), the GPU lock alone, TERTIUS_PROBE_FRAME_TIMES=<out>/frames.txt
#          (one line per timed frame: frame number, GPU span in ms, then each pass); the 1-minute load at the start goes
#          into <out>/load.txt, and a run starts only below 8
#   scale  <bin> <out> <script>: as timing, under the dynamic render scale FrameTimeTarget(16.7 ms, 0.5, 1)
#          (TERTIUS_X_TARGET_MS), with TERTIUS_X_SCALE_LOG=<out>/scale.txt (apply-scale.py has its fields)
# Usage: bash fix-run.sh film|timing|scale <bin folder> <out> <script.json>
set -uo pipefail
mode=$1; bin=$(realpath "$2"); out=$(realpath -m "$3"); script=$(realpath "$4")
mkdir -p "$out"
free=$(/bin/df -g / | awk 'NR==2{print $4}')
[[ "$free" -lt 18 ]] && { echo "disk $free GiB: stop"; exit 2; }
load() { sysctl -n vm.loadavg | awk '{print $2}'; }
args=(--script "$script" --headless --out "$out" --size 1920x1080 --exposure 4.08)
case $mode in
  film)
    bash tools/gpu-lock.sh --shared "$out/run.log" "$bin/Tertius.Demo" "${args[@]}" ;;
  timing|scale)
    until awk -v l="$(load)" 'BEGIN{exit !(l < 8)}'; do echo "$(date +%H:%M) load $(load), waiting"; sleep 60; done
    echo "$(date +%H:%M) load $(load)" | tee "$out/load.txt"
    extra=(TERTIUS_PROBE_FRAME_TIMES="$out/frames.txt")
    [[ $mode == scale ]] && extra+=(TERTIUS_X_TARGET_MS=16.7 TERTIUS_X_SCALE_LOG="$out/scale.txt")
    env "${extra[@]}" bash tools/gpu-lock.sh "$out/run.log" "$bin/Tertius.Demo" "${args[@]}" --video none ;;
  *) echo "no mode $mode"; exit 1 ;;
esac
code=$?
[[ $code -ne 0 ]] && tail -20 "$out/run.log" >&2
echo "$mode $(basename "$out"): exit $code, load at the end $(load)"
exit $code
