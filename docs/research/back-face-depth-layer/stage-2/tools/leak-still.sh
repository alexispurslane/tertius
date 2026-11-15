#!/usr/bin/env bash
# Throwaway (t1-3j2j): one still of the probe on the Mac, under the shared GPU lock, with the screen tracer off.
# Run inside the dev shell:
#   [NOSUN=1] [TERTIUS_SHADOW_*=...] bash leak-still.sh <binary folder> <tree> <script> <stops> <out> [demo arguments]
# It keeps frame 239 of the film at 1920 x 1080 with --bloom off and the fixed exposure <stops>, as <out>/s-000239.png,
# and deletes every other frame. NOSUN=1 sets TERTIUS_NO_SUN=1. The TERTIUS_SHADOW_* switches pass through
# (ShadowProbe.cs in stage-2/probe-shadow-leak.patch).
set -uo pipefail
bin=$1; tree=$2; script=$3; stops=$4; out=$5; shift 5
lock=/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh
mkdir -p "$out"
(cd "$tree" && TERTIUS_NO_SUN=${NOSUN:-0} TERTIUS_BACK_FACE_PROBE=layer bash "$lock" --shared "$out/run.log" "$bin/Tertius.Demo" \
  --script "$script" --headless --out "$out" --size 1920x1080 --bloom off --screen-trace off --exposure "$stops" "$@" \
  > /dev/null 2>&1)
status=$?
find "$out" -name 's-*.png' ! -name s-000239.png -delete
echo "exit $status" | tee -a "$out/run.log"
