#!/usr/bin/env bash
# Tool code of t0-5tyj (ticket t4f-71qp). queue-mac4.sh: the Mac's takes after queue-mac3.sh, asked by the line
# orchestrator: main under the same dynamic render scale. Build mdyn is main c4c1f1f2f with its own survey probe and
# ../scratch-dynamic-scale.patch (main has no region ring, so no region log). d16-M runs the switch cabin on it at the
# game's default target of 16.67 ms with the size log; d16-X2 repeats X's run d16-X, to show how much the controller's
# path varies from run to run. Then take T19, the screenshots, again: queue-mac2.sh ran the demo outside the dev shell,
# where it finds no .NET.
T=$(cd "$(dirname "$0")" && pwd); D=$(cd "$T/.." && pwd)
export BASE=/private/tmp/t0-5tyj
[ -e $BASE/mdyn/probe-bin/Tertius.Demo ] || bash "$T/build.sh" mdyn c4c1f1f2f c4c1f1f2f:tools/renderer-survey/probe.patch "$D/scratch-dynamic-scale.patch"
PROBE=$BASE/mdyn/probe-bin bash "$T/take.sh" d16-M switch cabin TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_LOG=$BASE/dynlog/d16M
PROBE=$BASE/xdyn/probe-bin bash "$T/take.sh" d16-X2 switch cabin TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_LOG=$BASE/dynlog/d16X2 TERTIUS_X_REGION_LOG=$BASE/dynlog/d16X2
R=$(cd "$T/../../../.." && pwd)
for side in M X; do
  bin=$BASE/t${side,,}/probe-bin/Tertius.Demo
  for script in rect-lamps rect-bay; do
    out=$BASE/shots/$side-$script; mkdir -p "$out"
    echo "$(date +%T) screenshot $side $script"
    (cd "$R" && nix develop --command bash tools/gpu-lock.sh --shared "$out/lock.log" "$bin" --script "scripts/$script.json" --headless --out "$out" > "$out/run.log" 2>&1)
    echo "$(date +%T) screenshot $side $script exit $? $(ls "$out"/*.png 2>/dev/null | wc -l) images"
  done
done
echo "$(date +%T) queue-mac4 done"
