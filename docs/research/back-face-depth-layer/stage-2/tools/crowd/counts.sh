#!/usr/bin/env bash
# Throwaway (t1-kiaw): the draw counts of the back-face pass in real views. Each view runs its script once with the
# pass on (mode ten) and TERTIUS_BACK_FACE_COUNT, which writes one line of counts per drawn frame.
# Run inside the dev shell from the worktree's root, under the GPU lock in shared mode.
set -u
out=${1:-/private/tmp/t1-kiaw/counts}
bin=/private/tmp/t1-kiaw/bin/Tertius.Demo
mkdir -p "$out"
count() { # <name> <script> [env...]
  local name=$1 script=$2; shift 2
  rm -rf "$out/$name"; mkdir -p "$out/$name"
  env TERTIUS_BACK_FACE_PROBE=ten TERTIUS_BACK_FACE_COUNT="$out/$name/counts.txt" "$@" "$bin" --script "$script" \
    --headless --out "$out/$name/run" --video none > "$out/$name/run.log" 2>&1
  echo "$name exit $? lines $(wc -l < "$out/$name/counts.txt")"
  find "$out/$name/run" -name '*.png' -delete
}
count deck scripts/temporal-upscale-timing.json
count cabin scripts/ter-kh4g-cabin-timing.json
count rect-lamps scripts/ter-kh4g-rect-lamps-timing.json
count station scripts/station.json
count one-person scripts/ter-kh4g-crowd-timing.json TERTIUS_CROWD_COUNT=1
count ten-persons scripts/ter-kh4g-crowd-timing.json TERTIUS_CROWD_COUNT=10
count crowd scripts/ter-kh4g-crowd-timing.json
count demo-docking scripts/demo-docking.json
