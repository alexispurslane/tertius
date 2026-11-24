#!/usr/bin/env bash
# Step 2 Batch A: Sunlit nose and rect-lamps 60 fps lossless films (main, Design 1 0.25, Design 1 0.50) under shared GPU lock.
set -euo pipefail

OUT=/private/tmp/t1-wl7c/fe63-d1/films
BIN=/private/tmp/t1-wl7c/probe-head/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
LOCK=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7/tools/gpu-lock.sh
FE63=/private/tmp/t1-wl7c/fe63
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
  local env_args=(
    "TERTIUS_PROBE_EXPOSURE=$exp"
  )
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

run_film nose-main   "$FE63/nose-film.json"  -1.7507 main 0    360
run_film nose-d1-025 "$FE63/nose-film.json"  -1.7507 d1   0.25 360
run_film nose-d1-050 "$FE63/nose-film.json"  -1.7507 d1   0.5  360
run_film lamps-main   "$FE63/lamps-film.json" 3.83    main 0    360
run_film lamps-d1-025 "$FE63/lamps-film.json" 3.83    d1   0.25 360
run_film lamps-d1-050 "$FE63/lamps-film.json" 3.83    d1   0.5  360

du -sh "$OUT"
check_disk
echo "STEP2_BATCH_A_DONE"
