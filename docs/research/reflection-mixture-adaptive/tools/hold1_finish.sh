#!/usr/bin/env bash
set -euo pipefail
OUT=/private/tmp/t1-wl7c/fe63-d1
EV=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/fe63-d1
CENSUS=/private/tmp/t1-wl7c/tools/rayend/census.py
ROOT=/private/tmp/t1-wl7c/probe-head
G=/private/tmp/t1-wl7c/guide
F=/private/tmp/t1-wl7c/fe63
mkdir -p "$EV"

check_disk() {
  local avail_mb
  avail_mb=$(/bin/df -m / | awk 'NR==2 {print $4}')
  echo "[DISK] ${avail_mb} MiB free before $1"
  if [ "$avail_mb" -lt 20992 ]; then
    echo "DISK_GUARD_STOP: ${avail_mb} MiB < 20992 MiB (20.5 GiB) before $1" >&2
    exit 99
  fi
}

export TERTIUS_PROBE_ADAPTIVE_SHARE=1
export TERTIUS_PROBE_RAMP=0.1,0.38
export TERTIUS_PROBE_SHARE_STEP=0.0078125
export TERTIUS_PROBE_SHARE_GUARD=0.0625
export TERTIUS_PROBE_SHARE_MAX_RATIO=2.0

# --- Remaining 0.25 takes ---
export TERTIUS_PROBE_REFLECTION_SHARE=0.25

# 1. lamps-d1-025-ref (100 captures -> npy -> verify -> clean disposable ref PNGs)
check_disk "lamps-d1-025-ref"
CENSUS_EXPOSURE=3.83 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-0 CENSUS_CAPTURES=100 \
  python3 -I "$CENSUS" "$ROOT" rect-lamps-f62 0.6667 "$OUT/lamps-d1-025-ref"
test "$(ls "$OUT/lamps-d1-025-ref"/c-s*.png | wc -l)" -eq 100
mkdir -p "$OUT/daub-lamps-d1-025"
python3 -I "$G/reference.py" "$OUT/lamps-d1-025-ref" "$OUT/daub-lamps-d1-025"
python3 -I "$G/parts.py" "$F/lamps-boxes.json" "$F/daub-lamps-main/reference-mean.npy" \
  "main=$F/lamps-main/c-s19.png:$F/daub-lamps-main/reference-mean.npy" \
  "exact025=$F/lamps-exact/c-s19.png:$F/daub-lamps-exact/reference-mean.npy" \
  "d1_025=$OUT/lamps-d1-025/c-s19.png:$OUT/daub-lamps-d1-025/reference-mean.npy" > "$OUT/parts-lamps-d1-025.json"
rm -rf "$OUT/lamps-d1-025-ref"

# 2. trace-bay-d1-025 (control-trace draws 600-616 -> fasttrace -> clean disposable f32s)
check_disk "trace-bay-d1-025"
CENSUS_EXPOSURE=-4.089 CENSUS_KEEP_ALL=0 CENSUS_DRAWS=600-616 \
  TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace \
  TERTIUS_PROBE_TRACE_DUMP=600-616 \
  python3 -I "$CENSUS" "$ROOT" bay-sunbeam 0.6667 "$OUT/trace-bay-d1-025"
test "$(ls "$OUT/trace-bay-d1-025"/census.t*.trace-3.f32 | wc -l)" -eq 16
test "$(ls "$OUT/trace-bay-d1-025"/census.t*.trace-6.f32 | wc -l)" -eq 16
python3 -I "$F/fasttrace.py" 600-616 1280x720 "d1_025=$OUT/trace-bay-d1-025" > "$OUT/fasttrace-d1-025.json"
rm -f "$OUT/trace-bay-d1-025"/census.t*.trace-*.f32 "$OUT/trace-bay-d1-025/census.sums" "$OUT/trace-bay-d1-025/census.slow"

# --- Ceiling 0.5 column ---
export TERTIUS_PROBE_REFLECTION_SHARE=0.5

# 3. plate-d1-050
check_disk "plate-d1-050"
CENSUS_EXPOSURE=0.73 CENSUS_KEEP_ALL=0 CENSUS_DRAWS=0-0 \
  python3 -I "$CENSUS" "$ROOT" mirror-plate 0.6667 "$OUT/plate-d1-050"
cmp -s "$OUT/plate-d1-050/c-s19.png" "$F/plate-main/c-s19.png" && echo "PLATE_050_BIT_IDENTICAL=YES" || echo "PLATE_050_BIT_IDENTICAL=NO"

# 4. nose-d1-050
check_disk "nose-d1-050"
CENSUS_EXPOSURE=-1.7507 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=240-640 \
  TERTIUS_PROBE_CENSUS_KEEP_REFLECTION=0 \
  TERTIUS_PROBE_SHARE_DUMP="$OUT/nose-d1-050/d" TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-660 \
  python3 -I "$CENSUS" "$ROOT" sunlit-nose 0.6667 "$OUT/nose-d1-050" "750,0,1170,330"
test "$(ls "$OUT/nose-d1-050"/c-s*.png | wc -l)" -eq 20
rm -f "$OUT/nose-d1-050/census.sums" "$OUT/nose-d1-050/census.slow"

# 5. bay-d1-050 + bay-d1-050-ref
check_disk "bay-d1-050"
CENSUS_EXPOSURE=-4.089 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-0 \
  TERTIUS_PROBE_SHARE_DUMP="$OUT/bay-d1-050/d" TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-660 \
  python3 -I "$CENSUS" "$ROOT" bay-sunbeam 0.6667 "$OUT/bay-d1-050"
test "$(ls "$OUT/bay-d1-050"/c-s*.png | wc -l)" -eq 20

check_disk "bay-d1-050-ref"
CENSUS_EXPOSURE=-4.089 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-0 CENSUS_CAPTURES=100 \
  python3 -I "$CENSUS" "$ROOT" bay-sunbeam 0.6667 "$OUT/bay-d1-050-ref"
test "$(ls "$OUT/bay-d1-050-ref"/c-s*.png | wc -l)" -eq 100
mkdir -p "$OUT/daub-bay-d1-050"
python3 -I "$G/reference.py" "$OUT/bay-d1-050-ref" "$OUT/daub-bay-d1-050"
python3 -I "$G/parts.py" "$G/daub/boxes.json" "$G/daub/reference-mean.npy" \
  "main=$G/stills/bay-0.25/c-s19.png:$G/daub/reference-mean.npy" \
  "exact025=$F/bay-exact/c-s19.png:$F/daub-bay-exact/reference-mean.npy" \
  "d1_025=$OUT/bay-d1-025/c-s19.png:$OUT/daub-bay-d1-025/reference-mean.npy" \
  "d1_050=$OUT/bay-d1-050/c-s19.png:$OUT/daub-bay-d1-050/reference-mean.npy" > "$OUT/parts-bay-d1.json"
rm -rf "$OUT/bay-d1-050-ref"

# 6. lamps-d1-050 + lamps-d1-050-ref
check_disk "lamps-d1-050"
CENSUS_EXPOSURE=3.83 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-0 \
  TERTIUS_PROBE_SHARE_DUMP="$OUT/lamps-d1-050/d" TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-660 \
  python3 -I "$CENSUS" "$ROOT" rect-lamps-f62 0.6667 "$OUT/lamps-d1-050"
test "$(ls "$OUT/lamps-d1-050"/c-s*.png | wc -l)" -eq 20

check_disk "lamps-d1-050-ref"
CENSUS_EXPOSURE=3.83 CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-0 CENSUS_CAPTURES=100 \
  python3 -I "$CENSUS" "$ROOT" rect-lamps-f62 0.6667 "$OUT/lamps-d1-050-ref"
test "$(ls "$OUT/lamps-d1-050-ref"/c-s*.png | wc -l)" -eq 100
mkdir -p "$OUT/daub-lamps-d1-050"
python3 -I "$G/reference.py" "$OUT/lamps-d1-050-ref" "$OUT/daub-lamps-d1-050"
python3 -I "$G/parts.py" "$F/lamps-boxes.json" "$F/daub-lamps-main/reference-mean.npy" \
  "main=$F/lamps-main/c-s19.png:$F/daub-lamps-main/reference-mean.npy" \
  "exact025=$F/lamps-exact/c-s19.png:$F/daub-lamps-exact/reference-mean.npy" \
  "d1_025=$OUT/lamps-d1-025/c-s19.png:$OUT/daub-lamps-d1-025/reference-mean.npy" \
  "d1_050=$OUT/lamps-d1-050/c-s19.png:$OUT/daub-lamps-d1-050/reference-mean.npy" > "$OUT/parts-lamps-d1.json"
rm -rf "$OUT/lamps-d1-050-ref"

# 7. trace-bay-d1-050 (window 2 clamp check!)
check_disk "trace-bay-d1-050"
CENSUS_EXPOSURE=-4.089 CENSUS_KEEP_ALL=0 CENSUS_DRAWS=600-616 \
  TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace \
  TERTIUS_PROBE_TRACE_DUMP=600-616 \
  python3 -I "$CENSUS" "$ROOT" bay-sunbeam 0.6667 "$OUT/trace-bay-d1-050"
test "$(ls "$OUT/trace-bay-d1-050"/census.t*.trace-3.f32 | wc -l)" -eq 16
test "$(ls "$OUT/trace-bay-d1-050"/census.t*.trace-6.f32 | wc -l)" -eq 16
python3 -I "$F/fasttrace.py" 600-616 1280x720 "d1_050=$OUT/trace-bay-d1-050" > "$OUT/fasttrace-d1-050.json"
rm -f "$OUT/trace-bay-d1-050"/census.t*.trace-*.f32 "$OUT/trace-bay-d1-050/census.sums" "$OUT/trace-bay-d1-050/census.slow"

echo "HOLD1_FINISH_DONE"
/bin/df -m /
