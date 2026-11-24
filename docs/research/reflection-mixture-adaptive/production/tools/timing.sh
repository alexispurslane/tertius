#!/usr/bin/env bash
# Tool code of ter-fe63's stage 2. The GPU frame time of unpatched main and the production branch on stage 1's three
# timing views, under one exclusive GPU lock, at 1920 x 1080 and a render scale of 0.6667, two frames in flight, pass
# timestamps off. Each round runs main, branch, branch, main, and the second round starts with the branch, so the order
# is balanced. Each run waits for a 1-minute load under 8 first.
# Usage: bash timing.sh <main binary> <branch binary> <scripts dir> <out dir>
set -euo pipefail
MAIN=$1 BRANCH=$2 SCRIPTS=$3 OUT=$4
TOOLS=/Users/alexispurslane/Development/tertius/tools
mkdir -p "$OUT"
export MAIN BRANCH SCRIPTS OUT TOOLS

bash "$TOOLS/gpu-lock.sh" "$OUT/exclusive-lock.log" bash -c '
set -euo pipefail
load() { uptime | sed -E "s/.*load averages?: ([0-9.]+).*/\1/"; }
quiet() {
  local waited=0
  until awk -v l="$(load)" "BEGIN { exit !(l < 8.0) }"; do
    [ "$waited" -ge 300 ] && { echo "LOAD_STILL_HIGH $(load)"; return 1; }
    sleep 5; waited=$((waited + 5))
  done
}
run() {
  local tag=$1 bin=$2 script=$3 stops=$4
  local dir="$OUT/$tag"
  mkdir -p "$dir"
  quiet
  local before; before=$(load)
  bash "$TOOLS/gpu-clock.sh" "$bin" --script "$SCRIPTS/$script.json" --headless --out "$dir" --video none \
    --size 1920x1080 --render-scale 0.6667 --in-flight 2 --pass-timestamps off --exposure "$stops" > "$dir/run.log" 2>&1
  printf "load_before=%s\nload_after=%s\n" "$before" "$(load)" > "$dir/load.txt"
  python3 -I "$TOOLS/gpu_clock.py" record "$dir/run.log" "$dir/gpu-timings.json" > /dev/null
  echo "$tag $(grep -E "^GPU " "$dir/run.log" | head -1) load $before"
}
run warmup "$MAIN" time-nose -1.7507
for spec in nose:time-nose:-1.7507 bay:time-bay:-4.089 lamps:time-lamps:3.83; do
  IFS=: read -r view script stops <<< "$spec"
  run "$view-r0-main-a" "$MAIN" "$script" "$stops"
  run "$view-r0-branch-a" "$BRANCH" "$script" "$stops"
  run "$view-r0-branch-b" "$BRANCH" "$script" "$stops"
  run "$view-r0-main-b" "$MAIN" "$script" "$stops"
  run "$view-r1-branch-a" "$BRANCH" "$script" "$stops"
  run "$view-r1-main-a" "$MAIN" "$script" "$stops"
  run "$view-r1-main-b" "$MAIN" "$script" "$stops"
  run "$view-r1-branch-b" "$BRANCH" "$script" "$stops"
done
'
echo TIMING_DONE
