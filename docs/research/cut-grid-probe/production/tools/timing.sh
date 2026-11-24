#!/usr/bin/env bash
# Tool code of t1-3er4: one exclusive GPU hold that times main against production. Cut-burst scripts repeat 8 cuts, and
# each films the far camera's first draw and the person camera's first 4 draws, so every timed draw runs the short-history
# fix (d0..d3). Steady scripts film 40 draws from a start, whose median is a settled draw (d10..d39). Order per scene:
# main, production, production, main for the cut bursts, and production, main for the steady runs. Each run waits for
# a 1-minute load under 8. 1920 x 1080, render scale 0.6667, two frames in flight, pass timestamps on.
set -euo pipefail
T=/private/tmp/t1-3er4; OUT=$T/timing/runs; mkdir -p "$OUT"
LOCK=/Users/alexispurslane/.t3/worktrees/tertius/t3-88afe8ff-7637aee74917d336/tools/gpu-lock.sh
export T OUT
bash "$LOCK" "$OUT/exclusive-lock.log" bash -c '
set -euo pipefail
load() { uptime | sed -E "s/.*load averages?: ([0-9.]+).*/\1/"; }
quiet() { local w=0; until awk -v l="$(load)" "BEGIN{exit !(l<8.0)}"; do [ $w -ge 60 ] && { echo LOAD_HIGH $(load); return 0; }; sleep 3; w=$((w+3)); done; }
run() {
  local tag=$1 bin=$2 script=$3 stops=$4 d=$OUT/$1
  mkdir -p $d; quiet; local before=$(load)
  $T/$bin/Tertius.Demo --script $T/timing/scripts/$script.json --headless --out $d --video none --size 1920x1080 \
    --render-scale 0.6667 --in-flight 2 --exposure $stops --gpu-run "accumulate indirect specular..composite" > $d/run.log 2>&1
  echo "$tag load $before $(python3 -c "import json;d=json.load(open(\"$d/gpu-timings.json\"));print(d[\"frame\"][\"medianMs\"], d[\"runs\"][0][\"timing\"][\"medianMs\"])")"
}
for scene in nose:-1.7507 lamps:3.83; do
  IFS=: read -r v s <<< "$scene"
  run cut-$v-main-a main-bin cut-burst-$v $s
  run cut-$v-prod-a prod-bin cut-burst-$v $s
  run cut-$v-prod-b prod-bin cut-burst-$v $s
  run cut-$v-main-b main-bin cut-burst-$v $s
  run steady-$v-prod prod-bin steady-$v $s
  run steady-$v-main main-bin steady-$v $s
done'
