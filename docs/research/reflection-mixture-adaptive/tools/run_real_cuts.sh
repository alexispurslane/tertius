#!/usr/bin/env bash
# Run forward (aft->nose) and reverse (aft->lamps floor) real camera cuts on unpatched main, D1 0.25 reset-off, and D1 0.25 reset-on.
set -euo pipefail

OUT=/private/tmp/t1-wl7c/fe63-d1/real-cuts
WT=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7
MAIN_BIN="$WT/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo"
PROBE_BIN=/private/tmp/t1-wl7c/probe-head/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
LOCK="$WT/tools/gpu-lock.sh"
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

run_case() {
  local name=$1 bin=$2 script=$3 exp=$4 mode=$5
  check_disk
  local dir="$OUT/$name"
  mkdir -p "$dir"
  local env_args=()
  if [ "$mode" = "d1-off" ] || [ "$mode" = "d1-on" ]; then
    env_args+=(
      "TERTIUS_PROBE_ADAPTIVE_SHARE=1"
      "TERTIUS_PROBE_REFLECTION_SHARE=0.25"
      "TERTIUS_PROBE_RAMP=0.1,0.38"
      "TERTIUS_PROBE_SHARE_STEP=0.0078125"
      "TERTIUS_PROBE_SHARE_GUARD=0.0625"
      "TERTIUS_PROBE_SHARE_MAX_RATIO=2.0"
      "TERTIUS_PROBE_SHARE_DUMP=$dir/d"
      "TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-200"
    )
    if [ "$mode" = "d1-on" ]; then
      env_args+=("TERTIUS_PROBE_SHARE_RESET_ON_CUT=1")
    else
      env_args+=("TERTIUS_PROBE_SHARE_RESET_ON_CUT=0")
    fi
  fi
  echo "=== START $name ($mode, $(date +%H:%M:%S)) ==="
  env "${env_args[@]}" bash "$LOCK" --shared "$dir/lock.log" \
    "$bin" --script "$script" --headless --out "$dir" \
    --video "$dir/film.mkv" --fps 60 --size 1920x1080 --render-scale 0.6667 --exposure "$exp"
  ls -lh "$dir/film.mkv"
}

python3 /private/tmp/t1-wl7c/fe63-d1/make_real_cut_scripts.py

# Forward real cut (settled aft -> sunlit nose, -1.7507 stops)
run_case nose-main       "$MAIN_BIN"  "$OUT/real-cut-nose.json"  -1.7507 main
run_case nose-d1-off     "$PROBE_BIN" "$OUT/real-cut-nose.json"  -1.7507 d1-off
run_case nose-d1-on      "$PROBE_BIN" "$OUT/real-cut-nose.json"  -1.7507 d1-on

# Reverse real cut (settled aft -> rect-lamps glossy floor, 3.83 stops)
run_case lamps-main      "$MAIN_BIN"  "$OUT/real-cut-lamps.json" 3.83    main
run_case lamps-d1-off    "$PROBE_BIN" "$OUT/real-cut-lamps.json" 3.83    d1-off
run_case lamps-d1-on     "$PROBE_BIN" "$OUT/real-cut-lamps.json" 3.83    d1-on

du -sh "$OUT"
check_disk
echo "REAL_CUTS_RUN_DONE"
