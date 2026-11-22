#!/usr/bin/env bash
# Tool code (ter-ukw6): the sill census of one view, before (c4c1f1f2f) against after (821481a5c), bright samples and
# every sample. Run from the worktree's root, inside the dev shell, under the GPU lock's shared mode:
#   bash tools/gpu-lock.sh --shared <log> bash <this> <out> <script> <boxes> <stops> [<from> <to>]
# The binaries are the census builds in /private/tmp/ter-ukw6/bin-before-census and bin-after-census: census-port.patch
# applied to each side. Each run films the script at 1920 x 1080 with bloom off and no video, at the fixed exposure
# <stops>, and counts the draws <from> up to, not including, <to> (200 and 600 by default). It writes
# <out>/<side>-<mode>/census.txt, where <mode> is bright (luminance 50 or more) or all (every sample). MODES picks
# the modes, both by default.
set -uo pipefail
out=$1; script=$2; boxes=$3; stops=$4; from=${5:-200}; to=${6:-600}
mkdir -p "$out"
for mode in ${MODES:-bright all}; do
  for side in before after; do
    dir=$out/$side-$mode
    rm -rf "${dir:?}" "$out/run-$side-$mode"; mkdir -p "$dir"
    all=0; [[ $mode == all ]] && all=1
    TERTIUS_PROBE_SILL_CENSUS=1 TERTIUS_PROBE_SILL_ALL=$all TERTIUS_PROBE_CENSUS_BOXES="$boxes" \
      TERTIUS_PROBE_CENSUS_OUT="$dir" TERTIUS_PROBE_CENSUS_FROM=$from TERTIUS_PROBE_CENSUS_TO=$to \
      /private/tmp/ter-ukw6/bin-$side-census/Tertius.Demo --script "$script" --headless --out "$out/run-$side-$mode" \
      --size 1920x1080 --bloom off --video none --exposure "$stops" > "$dir/run.log" 2>&1
    echo "$side $mode exit $? $(head -1 "$dir/census.txt" 2>/dev/null) load $(uptime | sed 's/.*averages*: //')" | tee -a "$out/summary.txt"
    # The film's frames are not needed: the census is the record.
    find "$out/run-$side-$mode" -name '*.png' -delete 2>/dev/null
  done
done
