#!/usr/bin/env bash
# Tool code of t58-rcd6: one diagnosis run of a film in a plain build copy with apply-cut.py's probe, untimed, under the
# GPU lock's shared mode (tools/gpu-lock.sh), so that it never runs beside a timed run that holds the lock alone.
# Headless at 1920 x 1080 with the default constant render scale of 2/3, the exposure fixed at 4.08 stops, one frame in
# flight (so that the log's reads after Render see only that frame's work), and the captures as PNGs, whose means
# means.py takes. Writes <out>/cut.log (one line per drawn frame), <out>/run.log and the PNGs.
# Usage (in the dev shell): bash run-cut.sh <copy root> <out> <script.json> <variant: 0 main, 1 refresh at the reset>
set -uo pipefail
copy=$(realpath "$1"); out=$(realpath -m "$2"); script=$(realpath "$3"); variant=$4
mkdir -p "$out"
env TERTIUS_PROBE_CUT_LOG="$out/cut.log" TERTIUS_PROBE_CUT_REFRESH="$variant" \
    bash "$copy/tools/gpu-lock.sh" --shared "$out/run.log" "$copy/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo" \
    --script "$script" --headless --out "$out" --size 1920x1080 --exposure 4.08 --in-flight 1
code=$?
[[ "$code" -ne 0 ]] && tail -20 "$out/run.log" >&2
echo "$(wc -l < "$out/cut.log") log lines, $(ls "$out"/*.png 2>/dev/null | wc -l) PNGs"
exit "$code"
