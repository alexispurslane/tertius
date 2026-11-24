#!/usr/bin/env bash
# Step 3: Exclusive-lock frame GPU time (main vs Design 1 0.25 vs Design 1 0.50)
# Fair exclusive queue via tools/gpu-lock.sh; waits for 1-min load < 8 once acquired; records load before & after every row.
set -euo pipefail

OUT=/private/tmp/t1-wl7c/fe63-d1/timing
BIN=/private/tmp/t1-wl7c/probe-head/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
WT=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7
LOCK="$WT/tools/gpu-lock.sh"
CLOCK="$WT/tools/gpu-clock.sh"
CLOCKPY="$WT/tools/gpu_clock.py"
mkdir -p "$OUT"

free_g=$(/bin/df -g / | awk 'NR==2{print $4}')
echo "FREE_GIB=$free_g"
if [ "$free_g" -lt 22 ]; then
  echo "DISK_FLOOR_BREACH: $free_g < 22 GiB" >&2
  exit 1
fi

# Generate 300-frame timing scripts (1920x1080, scale 0.6667 = 1280x720, matching Hold 1)
python3 - <<'PY'
import json
from pathlib import Path
out = Path("/private/tmp/t1-wl7c/fe63-d1/timing")
(out / "time-nose.json").write_text(json.dumps({
    "scene": "orbit", "window": {"width": 1920, "height": 1080},
    "steps": [{"do": "camera", "entity": "person"}, {"do": "run", "ticks": 277003},
              {"do": "film", "name": "t", "frames": 300, "every": 1}]
}))
(out / "time-bay.json").write_text(json.dumps({
    "scene": "demo-surface", "window": {"width": 1920, "height": 1080},
    "steps": [{"do": "camera", "entity": "bay-camera"},
              {"do": "film", "name": "t", "frames": 300, "every": 1}]
}))
(out / "time-lamps.json").write_text(json.dumps({
    "scene": "orbit", "window": {"width": 1920, "height": 1080},
    "steps": [{"do": "camera", "entity": "person"},
              {"do": "film", "name": "t", "frames": 300, "every": 1}]
}))
PY

wait_quiet() {
  local waited=0
  while true; do
    local l1
    l1=$(uptime | sed -E 's/.*load averages?: ([0-9.]+).*/\1/')
    if awk -v l="$l1" 'BEGIN { exit !(l < 8.0) }'; then
      echo "LOAD_OK=$l1 (waited=${waited}s)"
      return 0
    fi
    if [ "$waited" -ge 180 ]; then
      echo "LOAD_STILL_HIGH=$l1 after ${waited}s" >&2
      return 1
    fi
    echo "WAITING_FOR_LOAD: $l1 >= 8.0 (${waited}s)"
    sleep 5
    waited=$((waited + 5))
  done
}

run_one() {
  local tag=$1 scene=$2 script=$3 exp=$4 mode=$5 ceil=$6 extra_flag=$7
  local dir="$OUT/$tag"
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
    )
  fi
  local l_before l_after
  l_before=$(uptime | sed -E 's/.*load averages?: ([0-9.]+).*/\1/')
  echo "=== $tag (load_before=$l_before, $(date +%H:%M:%S)) ==="
  if [ "$extra_flag" = "notimestamps" ]; then
    env "${env_args[@]}" bash "$CLOCK" "$BIN" --script "$script" --headless --out "$dir" \
      --video none --size 1920x1080 --render-scale 0.6667 --in-flight 2 --pass-timestamps off > "$dir/run.log" 2>&1
  else
    env "${env_args[@]}" bash "$CLOCK" "$BIN" --script "$script" --headless --out "$dir" \
      --video none --size 1920x1080 --render-scale 0.6667 --in-flight 2 \
      --gpu-run "ray plan..accumulate indirect diffuse" > "$dir/run.log" 2>&1
  fi
  l_after=$(uptime | sed -E 's/.*load averages?: ([0-9.]+).*/\1/')
  printf 'load_before=%s\nload_after=%s\n' "$l_before" "$l_after" > "$dir/load.txt"
  python3 -I "$CLOCKPY" record "$dir/run.log" "$dir/gpu-timings.json" > /dev/null
  grep -E '^GPU ' "$dir/run.log" || true
  echo "  load_after=$l_after"
}

export -f wait_quiet run_one
export OUT BIN WT CLOCK CLOCKPY

bash "$LOCK" "$OUT/exclusive-lock.log" bash -c '
set -euo pipefail
wait_quiet
# Short warm-up run (discarded) to settle GPU clock
run_one warmup nose "$OUT/time-nose.json" -1.7507 main 0 notimestamps

for scene_spec in "nose:$OUT/time-nose.json:-1.7507" "bay:$OUT/time-bay.json:-4.089" "lamps:$OUT/time-lamps.json:3.83"; do
  IFS=: read -r sc sc_json sc_exp <<< "$scene_spec"
  wait_quiet
  # Round 0 (--pass-timestamps off): main -> d1-025 -> d1-050
  run_one "${sc}-r0-main-noff"   "$sc" "$sc_json" "$sc_exp" main 0    notimestamps
  run_one "${sc}-r0-d1-025-noff" "$sc" "$sc_json" "$sc_exp" d1   0.25 notimestamps
  run_one "${sc}-r0-d1-050-noff" "$sc" "$sc_json" "$sc_exp" d1   0.5  notimestamps
  # Round 1 (--pass-timestamps off, reversed): d1-050 -> d1-025 -> main
  run_one "${sc}-r1-d1-050-noff" "$sc" "$sc_json" "$sc_exp" d1   0.5  notimestamps
  run_one "${sc}-r1-d1-025-noff" "$sc" "$sc_json" "$sc_exp" d1   0.25 notimestamps
  run_one "${sc}-r1-main-noff"   "$sc" "$sc_json" "$sc_exp" main 0    notimestamps
  # Round 2 (--gpu-run "ray plan..accumulate indirect diffuse"): main -> d1-025 -> d1-050
  run_one "${sc}-r2-main-rng"    "$sc" "$sc_json" "$sc_exp" main 0    range
  run_one "${sc}-r2-d1-025-rng"  "$sc" "$sc_json" "$sc_exp" d1   0.25 range
  run_one "${sc}-r2-d1-050-rng"  "$sc" "$sc_json" "$sc_exp" d1   0.5  range
done
'

echo "STEP3_TIMING_DONE"
