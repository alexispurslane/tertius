#!/usr/bin/env bash
# Step 2 Batch B: Slow pan, walk, 180-deg camera cut, and bit-exact determinism repeat under shared GPU lock.
set -euo pipefail

OUT=/private/tmp/t1-wl7c/fe63-d1/films
SCRIPTS=/private/tmp/t1-wl7c/fe63-d1
BIN=/private/tmp/t1-wl7c/probe-head/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
LOCK=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7/tools/gpu-lock.sh
mkdir -p "$OUT"

check_disk() {
  local free_g
  free_g=$(/bin/df -g / | awk 'NR==2{print $4}')
  echo "FREE_GIB=$free_g"
  if [ "$free_g" -lt 22 ]; then
    echo "DISK_FLOOR_BREACH: $free_g < 22 GiB" >&2
    exit 1
  fi
}

run_film() {
  local name=$1 script=$2 exp=$3 mode=$4 ceil=$5 draws=$6
  check_disk
  local dir="$OUT/$name"
  mkdir -p "$dir"
  local env_args=("TERTIUS_PROBE_EXPOSURE=$exp")
  if [ "$mode" = "d1" ]; then
    env_args+=(
      "TERTIUS_PROBE_ADAPTIVE_SHARE=1"
      "TERTIUS_PROBE_REFLECTION_SHARE=$ceil"
      "TERTIUS_PROBE_RAMP=0.1,0.38"
      "TERTIUS_PROBE_SHARE_STEP=0.0078125"
      "TERTIUS_PROBE_SHARE_GUARD=0.0625"
      "TERTIUS_PROBE_SHARE_MAX_RATIO=2.0"
      "TERTIUS_PROBE_SHARE_DUMP=$dir/d"
      "TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-$draws"
    )
  fi
  echo "=== START $name ($(date +%H:%M:%S)) ==="
  env "${env_args[@]}" bash "$LOCK" --shared "$dir/lock.log" \
    "$BIN" --script "$script" --headless --out "$dir" \
    --video "$dir/film.mkv" --fps 60 --size 1920x1080 --render-scale 0.6667
  ls -lh "$dir/film.mkv"
}

run_det_png() {
  local name=$1
  check_disk
  local dir="$OUT/$name"
  mkdir -p "$dir"
  echo "=== START $name ($(date +%H:%M:%S)) ==="
  env \
    TERTIUS_PROBE_EXPOSURE=-1.7507 \
    TERTIUS_PROBE_KEEP=w-000179 \
    TERTIUS_PROBE_ADAPTIVE_SHARE=1 \
    TERTIUS_PROBE_REFLECTION_SHARE=0.25 \
    TERTIUS_PROBE_RAMP=0.1,0.38 \
    TERTIUS_PROBE_SHARE_STEP=0.0078125 \
    TERTIUS_PROBE_SHARE_GUARD=0.0625 \
    TERTIUS_PROBE_SHARE_MAX_RATIO=2.0 \
    TERTIUS_PROBE_SHARE_DUMP="$dir/d" \
    TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-240 \
    bash "$LOCK" --shared "$dir/lock.log" \
    "$BIN" --script "$SCRIPTS/walk-hall.json" --headless --out "$dir" \
    --size 1920x1080 --render-scale 0.6667
  ls -lh "$dir/w-000179.png" "$dir/d.shares.f32"
}

run_film pan-main    "$SCRIPTS/pan-hall.json"  -1.7507 main 0    300
run_film pan-d1-025  "$SCRIPTS/pan-hall.json"  -1.7507 d1   0.25 300
run_film pan-d1-050  "$SCRIPTS/pan-hall.json"  -1.7507 d1   0.5  300

run_film walk-main   "$SCRIPTS/walk-hall.json" -1.7507 main 0    240
run_film walk-d1-025 "$SCRIPTS/walk-hall.json" -1.7507 d1   0.25 240
run_film walk-d1-050 "$SCRIPTS/walk-hall.json" -1.7507 d1   0.5  240

run_film cut-main    "$SCRIPTS/cut-hall.json"  -1.7507 main 0    180
run_film cut-d1-025  "$SCRIPTS/cut-hall.json"  -1.7507 d1   0.25 180
run_film cut-d1-050  "$SCRIPTS/cut-hall.json"  -1.7507 d1   0.5  180

run_det_png det-walk-r1
run_det_png det-walk-r2

echo "=== DETERMINISM CHECK ==="
shasum -a 256 "$OUT/det-walk-r1/w-000179.png" "$OUT/det-walk-r2/w-000179.png"
shasum -a 256 "$OUT/det-walk-r1/d.shares.f32" "$OUT/det-walk-r2/d.shares.f32" "$OUT/walk-d1-025/d.shares.f32"
cmp -s "$OUT/det-walk-r1/w-000179.png" "$OUT/det-walk-r2/w-000179.png" && echo "DET_PNG_IDENTICAL=YES" || echo "DET_PNG_IDENTICAL=NO"
cmp -s "$OUT/det-walk-r1/d.shares.f32" "$OUT/det-walk-r2/d.shares.f32" && echo "DET_SHARES_IDENTICAL=YES" || echo "DET_SHARES_IDENTICAL=NO"

du -sh "$OUT"
check_disk
echo "STEP2_BATCH_B_DONE"
