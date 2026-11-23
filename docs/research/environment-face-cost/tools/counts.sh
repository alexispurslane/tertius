#!/usr/bin/env bash
# Tool code of ter-yglw: what the size cull leaves out, and a picture of each map's faces, per variant, in two views:
# the deck (deck-40.json, demo-exit's deck camera from frame 399) and one cabin view (cabin-40.json, demo-exit's cabin
# camera from frame 100), 40 frames each. Untimed, so the runs take the GPU lock in its shared mode. A still's first
# draw refreshes every face of every map, so the first line of faces.log counts all six faces of each map. At the end
# the renderer dumps level 0 of each map's radiance cube (FaceProbe.Dump), which faceview.py draws.
# VARIANTS (env) replaces the four variants; a variant size:cull:parts (t1-6tn2) runs with TERTIUS_PROBE_FACE_PARTS=parts
# and the name suffix -parts.
# Usage (in the dev shell, from the checkout root): bash counts.sh <out root>
set -uo pipefail
root=$1
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
for view in deck cabin; do
  for v in ${VARIANTS:-128:0 128:1 32:0 32:1}; do
    size=${v%%:*}; rest=${v#*:}; cull=${rest%%:*}; parts=$([[ "$rest" == *:* ]] && echo "${rest#*:}")
    out="$root/$view-w$size-c$cull${parts:+-$parts}"
    mkdir -p "$out"
    env TERTIUS_PROBE_FACE_PARTS="$parts" TERTIUS_PROBE_FACE_SIZE="$size" TERTIUS_PROBE_FACE_CULL="$cull" TERTIUS_PROBE_FACE_LOG="$out/faces.log" \
        TERTIUS_PROBE_FACE_DUMP="$out/dump" \
        bash tools/gpu-lock.sh --shared "$out/run.log" src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo \
        --script "$here/$view-40.json" --headless --out "$out" --video none --size 1280x720 || tail -5 "$out/run.log"
    echo "$view $size $cull $parts: $(head -1 "$out/faces.log" | wc -w) faces in the first draw"
  done
done
