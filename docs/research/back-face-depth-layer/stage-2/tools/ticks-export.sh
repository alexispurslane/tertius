#!/usr/bin/env bash
# Throwaway (t1-3j2j): the frames of the other line's takes. Run inside the dev shell:
#   bash ticks-export.sh <binary folder> <tree> <out>
# The script ticks-orbit-person.json captures the scene orbit with the person camera at world ticks 60, 300 and 306, at
# 1920 x 1080 under FSR. One run per capture index exports that frame (TERTIUS_SCENE_EXPORT) and dumps its cascades
# (TERTIUS_SHADOW_DUMP) into <out>/t060, <out>/t300 and <out>/t306, under the shared GPU lock.
set -uo pipefail
bin=$1; tree=$2; out=$3
lock=${GPU_LOCK:-$tree/tools/gpu-lock.sh}
for pair in 0:t060 1:t300 2:t306; do
  at=${pair%%:*}; name=${pair#*:}
  mkdir -p "$out/$name"
  (cd "$tree" && TERTIUS_SCENE_EXPORT="$out/$name/export" TERTIUS_SCENE_EXPORT_AT=$at TERTIUS_SHADOW_DUMP="$out/$name/dump" \
    TERTIUS_SHADOW_DUMP_AT=$at TERTIUS_BACK_FACE_PROBE=layer bash "$lock" --shared "$out/$name/run.log" "$bin/Tertius.Demo" \
    --script tools/bf1b/ticks-orbit-person.json --headless --out "$out/$name/frames" --size 1920x1080 --bloom off > /dev/null 2>&1)
  echo "$name exit $?"
done
