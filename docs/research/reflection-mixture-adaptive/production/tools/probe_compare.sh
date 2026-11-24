#!/usr/bin/env bash
# Times unpatched main, the stage 1 probe binary with its probe off, the probe at Config B, and the production branch on
# the nose view, under one exclusive GPU lock, with the GPU clock.
set -euo pipefail
S=$1
export GPU_CALIBRATION_BIN=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7/captures/gpu-calibration/bin
TOOLS=/Users/alexispurslane/Development/tertius/tools
MAIN=$S/main-56feef7d5/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
PROBE=/private/tmp/t1-wl7c/probe-head/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
BRANCH=$S/branch-bin/Tertius.Demo
ON="TERTIUS_PROBE_ADAPTIVE_SHARE=1 TERTIUS_PROBE_REFLECTION_SHARE=0.25 TERTIUS_PROBE_RAMP=0.1,0.38 TERTIUS_PROBE_SHARE_STEP=0.0078125 TERTIUS_PROBE_SHARE_GUARD=0.0625 TERTIUS_PROBE_SHARE_MAX_RATIO=2.0 TERTIUS_PROBE_SHARE_RESET_ON_CUT=1"
export S TOOLS MAIN PROBE BRANCH ON
bash $TOOLS/gpu-lock.sh $S/probe-compare/lock.log bash -c '
run() {
  local tag=$1 bin=$2 envs=$3 d=$S/probe-compare/$1
  mkdir -p $d
  until awk -v l="$(uptime | sed -E "s/.*load averages?: ([0-9.]+).*/\1/")" "BEGIN{exit !(l<8)}"; do sleep 5; done
  env $envs bash $TOOLS/gpu-clock.sh $bin --script $S/prod-scripts/time-nose.json --headless --out $d --video none --size 1920x1080 --render-scale 0.6667 --in-flight 2 --pass-timestamps off --exposure -1.7507 > $d/run.log 2>&1
  echo "$tag $(grep "^GPU frame" $d/run.log) | $(grep "GPU clock over" $d/run.log)"
}
for r in 0 1; do
  run main-$r $MAIN ""
  run probeoff-$r $PROBE ""
  run probeon-$r $PROBE "$ON"
  run branch-$r $BRANCH ""
done'
