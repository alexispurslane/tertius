#!/usr/bin/env bash
# Tool code of ter-yglw: one timed headless run of the probe demo (tools/apply-face.py, built in Release) on a script,
# for one variant of the environment face, under the GPU lock alone (tools/gpu-lock.sh) and beside gpu-clock
# (tools/gpu-clock.sh), as tools/demo-timing.sh times the deck, but without its build: the probe builds with its own
# flags. The faces come in every second drawn frame only (TERTIUS_PROBE_FACE_EVERY=2), so that each odd frame is the
# same frame without a face. Writes into <out>: run.log (with the uptime at the start and the end), faces.log (one line
# per drawn frame, FaceProbe.cs), frames.log (one line per timed frame, FrameTimeProbe.cs) and gpu-timings.json with the
# GPU clock of the timed span.
# Usage (in the dev shell, from the checkout root): bash time.sh <out> <script.json> <face size> <cull 0|1> [demo options]
set -uo pipefail
out=$(realpath -m "$1"); script=$(realpath "$2"); size=$3; cull=$4; shift 4
mkdir -p "$out"
env TERTIUS_PROBE_FACE_SIZE="$size" TERTIUS_PROBE_FACE_CULL="$cull" TERTIUS_PROBE_FACE_EVERY=2 \
    TERTIUS_PROBE_FACE_LOG="$out/faces.log" TERTIUS_PROBE_FRAME_TIMES="$out/frames.log" \
    bash tools/gpu-lock.sh "$out/run.log" bash tools/gpu-clock.sh src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo \
    --script "$script" --headless --out "$out" --video none "$@"
code=$?
[[ "$code" -ne 0 ]] && { tail -20 "$out/run.log" >&2; exit "$code"; }
python3 -I tools/gpu_clock.py record "$out/run.log" "$out/gpu-timings.json" > /dev/null
grep -E '^GPU ' "$out/run.log" | tail -3
