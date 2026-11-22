#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-mac2.sh: the Mac's takes after queue-mac.sh, into /private/tmp/t0-5tyj.
#   - Builds: xlog (X with ../scratch-region-log.patch, the per-anchor log of the region's test from t0-9fo0 part B),
#     and tm and tx, plain Release demos of M and X without a probe, for the screenshots and the frame time.
#   - Take 6, the census on X: the survey's census-still part for the four stills, one run per still on xlog, with the
#     region log on (TERTIUS_X_REGION_LOG), so that one render gives the evidence census and the region test's fires.
#   - The X stills of group 6 that failed at 07:44 with vkCreateInstance ErrorIncompatibleDriver, taken again as s7-X.
#     stilltable.py pairs them with s6-M by case name.
#   - The remedy under the dynamic render scale (t4f-71qp): builds x0 (631cc61cc, the line without the remedy, with
#     that tree's probe) and xdyn (X with the region log and ../scratch-dynamic-scale.patch). The switch cabin at the
#     fixed scale 2/3 on M, X0 and X; on xdyn at the fixed scale (dfix-X, the logs' control) and under the dynamic scale
#     with the game's default target of 16.67 ms (d16-X) and with 8 ms (d8-X), with the size log and the region log.
#   - Take T19: headless screenshots of the real renderer, M and X, of scripts/rect-lamps.json and scripts/rect-bay.json.
T=$(cd "$(dirname "$0")" && pwd); D=$(cd "$T/.." && pwd); R=$(cd "$T/../../../.." && pwd)
export BASE=/private/tmp/t0-5tyj
X=$(git -C "$R" rev-parse HEAD)
PROBE=$BASE/x/probe-bin bash "$T/take.sh" s7-X still walk,push-side,push-astronaut,windows-earth,windows-cabin,danube-hold,bay-sunbeam
[ -e $BASE/xlog/probe-bin/Tertius.Demo ] || bash "$T/build.sh" xlog "$X" "$X:tools/renderer-survey/probe.patch" "$D/scratch-region-log.patch"
[ -e $BASE/tm/probe-bin/Tertius.Demo ] || bash "$T/build.sh" tm c4c1f1f2f
[ -e $BASE/tx/probe-bin/Tertius.Demo ] || bash "$T/build.sh" tx "$X"
[ -e $BASE/x0/probe-bin/Tertius.Demo ] || bash "$T/build.sh" x0 631cc61cc 631cc61cc:tools/renderer-survey/probe.patch
[ -e $BASE/xdyn/probe-bin/Tertius.Demo ] || bash "$T/build.sh" xdyn "$X" "$X:tools/renderer-survey/probe.patch" "$D/scratch-region-log.patch" "$D/scratch-dynamic-scale.patch"
PROBE=$BASE/m/probe-bin bash "$T/take.sh" w-cab-M switch cabin
PROBE=$BASE/x0/probe-bin bash "$T/take.sh" w-cab-X0 switch cabin
PROBE=$BASE/x/probe-bin bash "$T/take.sh" w-cab-X switch cabin
PROBE=$BASE/xdyn/probe-bin bash "$T/take.sh" dfix-X switch cabin TERTIUS_X_SIZE_LOG=$BASE/dynlog/dfix TERTIUS_X_REGION_LOG=$BASE/dynlog/dfix
PROBE=$BASE/xdyn/probe-bin bash "$T/take.sh" d16-X switch cabin TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_LOG=$BASE/dynlog/d16 TERTIUS_X_REGION_LOG=$BASE/dynlog/d16
PROBE=$BASE/xdyn/probe-bin bash "$T/take.sh" d8-X switch cabin TERTIUS_X_FRAME_TIME_TARGET=8 TERTIUS_X_SIZE_LOG=$BASE/dynlog/d8 TERTIUS_X_REGION_LOG=$BASE/dynlog/d8
for still in rect-lamps exit-cabin docking-bay station-cabin; do
  PROBE=$BASE/xlog/probe-bin bash "$T/take.sh" "n-$still" census-still "$still" TERTIUS_X_REGION_LOG=$BASE/regionlog/$still
done
for side in M X; do
  bin=$BASE/t${side,,}/probe-bin/Tertius.Demo
  for script in rect-lamps rect-bay; do
    out=$BASE/shots/$side-$script; [ -d "$out" ] && continue; mkdir -p "$out"
    echo "$(date +%T) screenshot $side $script"
    (cd "$R" && bash tools/gpu-lock.sh --shared "$out/lock.log" "$bin" --script "scripts/$script.json" --headless --out "$out" > "$out/run.log" 2>&1)
  done
done
echo "$(date +%T) queue-mac2 done"
