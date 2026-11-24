#!/usr/bin/env bash
set -euo pipefail
OUT=/private/tmp/t1-wl7c/fe63-d1
CENSUS=/private/tmp/t1-wl7c/tools/rayend/census.py
ROOT=/private/tmp/t1-wl7c/probe-head
G=/private/tmp/t1-wl7c/guide

# Tuned Design 1 parameters: Config B (ShareStep=1/128, ShareGuard=1/16, MaxRatio=2.0)
export TERTIUS_PROBE_ADAPTIVE_SHARE=1
export TERTIUS_PROBE_RAMP=0.1,0.38
export TERTIUS_PROBE_SHARE_STEP=0.0078125
export TERTIUS_PROBE_SHARE_GUARD=0.0625
export TERTIUS_PROBE_SHARE_MAX_RATIO=2.0

for ceil in 0.25 0.5; do
  tag=$(echo "$ceil" | tr -d '.')
  export TERTIUS_PROBE_REFLECTION_SHARE=$ceil

  # 1. Mirror plate (20 captures)
  CENSUS_EXPOSURE=0.73 CENSUS_KEEP_ALL=0 CENSUS_DRAWS=0-0 \
    python3 -I "$CENSUS" "$ROOT" mirror-plate 0.6667 "$OUT/plate-d1-$tag"
  cmp -s "$OUT/plate-d1-$tag/c-s19.png" /private/tmp/t1-wl7c/fe63/plate-main/c-s19.png \
    && echo "PLATE_${tag}_BIT_IDENTICAL=YES" || echo "PLATE_${tag}_BIT_IDENTICAL=NO"

  # 2. Sunlit nose (20 captures, keep all PNGs + 65-byte census keep of ceiling box + share dump)
  CENSUS_EXPOSURE=-1.7507 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=240-640 \
    TERTIUS_PROBE_CENSUS_KEEP_REFLECTION=0 \
    TERTIUS_PROBE_SHARE_DUMP="$OUT/nose-d1-$tag/d" TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-660 \
    python3 -I "$CENSUS" "$ROOT" sunlit-nose 0.6667 "$OUT/nose-d1-$tag" "750,0,1170,330"
  rm -f "$OUT/nose-d1-$tag/census.sums" "$OUT/nose-d1-$tag/census.slow"

  # 3. Frozen bay (20 captures + share dump, and 100-capture ref -> npy)
  CENSUS_EXPOSURE=-4.089 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-0 \
    TERTIUS_PROBE_SHARE_DUMP="$OUT/bay-d1-$tag/d" TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-660 \
    python3 -I "$CENSUS" "$ROOT" bay-sunbeam 0.6667 "$OUT/bay-d1-$tag"
  CENSUS_EXPOSURE=-4.089 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-0 CENSUS_CAPTURES=100 \
    python3 -I "$CENSUS" "$ROOT" bay-sunbeam 0.6667 "$OUT/bay-d1-$tag-ref"
  mkdir -p "$OUT/daub-bay-d1-$tag"
  python3 -I "$G/reference.py" "$OUT/bay-d1-$tag-ref" "$OUT/daub-bay-d1-$tag"
  rm -rf "$OUT/bay-d1-$tag-ref"

  # 4. rect-lamps glossy floor (20 captures + share dump, and 100-capture ref -> npy)
  CENSUS_EXPOSURE=3.83 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-0 \
    TERTIUS_PROBE_SHARE_DUMP="$OUT/lamps-d1-$tag/d" TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-660 \
    python3 -I "$CENSUS" "$ROOT" rect-lamps-f62 0.6667 "$OUT/lamps-d1-$tag"
  CENSUS_EXPOSURE=3.83 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-0 CENSUS_CAPTURES=100 \
    python3 -I "$CENSUS" "$ROOT" rect-lamps-f62 0.6667 "$OUT/lamps-d1-$tag-ref"
  mkdir -p "$OUT/daub-lamps-d1-$tag"
  python3 -I "$G/reference.py" "$OUT/lamps-d1-$tag-ref" "$OUT/daub-lamps-d1-$tag"
  rm -rf "$OUT/lamps-d1-$tag-ref"

  # 5. Clamp / fast-history trace on bay (draws 600-616) for window-2 / clamp-fighting check
  CENSUS_EXPOSURE=-4.089 CENSUS_KEEP_ALL=0 CENSUS_DRAWS=600-616 \
    TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace \
    TERTIUS_PROBE_TRACE_DUMP=600-616 \
    python3 -I "$CENSUS" "$ROOT" bay-sunbeam 0.6667 "$OUT/trace-bay-d1-$tag"
  rm -f "$OUT/trace-bay-d1-$tag/census.sums" "$OUT/trace-bay-d1-$tag/census.slow"

  /bin/df -g / | tail -1
done

echo "HOLD1_DONE"
