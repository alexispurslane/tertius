#!/usr/bin/env bash
# Tool code of t1-yi11. yitime.sh <out folder>: the timing of the closed-gate comparison (2026-11-24). Frozen bay takes as
# bayrun.sh makes them (660 draws, 1920 x 1080 at the render scale 2/3, -4.089 stops), but under the GPU lock's EXCLUSIVE
# mode, with no PNG kept (the keep list acts only with TERTIUS_PROBE_SERIES, so the take also measures the series, as
# every bayrun.sh take does), no ring log and no length dump, so that no readback waits on the device. Columns, in the order
# A B C D E, then again E D C B A: yh-b (build yh, cap 128: today's test without build yi's 72-draw ring and Welch
# statistics), b, g128r, w36 and hyst (build yj with the knobs of films.sh's byj, g128ryj, w36yj and hx). Before each take
# it waits until the 1-minute load is under 8 and records the load. Each take writes <out>/<round>-<column>/
# gpu-timings.json. tools/yitime.py summarizes them (frame span and the sum of the passes
# "accumulate indirect diffuse" and "filter indirect diffuse", the region test's pass and its neighbor: MoltenVK's single
# pass times are not valid alone). Run it with bash from the checkout's root.
set -uo pipefail
out=$1; mkdir -p "$out"
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd); B=/private/tmp/t1-yi11
declare -A build=([yhb]=yh [b]=yj [g128r]=yj [w36]=yj [hyst]=yj)
declare -A knobs=([yhb]="TERTIUS_X_DIFFUSE_CAP=128" [b]="TERTIUS_X_DIFFUSE_CAP=128"
                  [g128r]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_RANK=1"
                  [w36]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_CLOSED=w36"
                  [hyst]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_CLOSED=hyst")
python3 -I - > "$out/script.json" <<'PY'
import json, sys
steps = [{"do": "camera", "entity": "bay-camera"}, {"do": "run", "ticks": 420}]
steps += [{"do": "capture", "name": f"f-{n:06d}"} for n in range(660)]
json.dump({"scene": "demo-surface", "window": {"width": 1920, "height": 1080}, "steps": steps}, sys.stdout, indent=1)
PY
load() { sysctl -n vm.loadavg | awk '{print $2}'; }
round=0
for col in yhb b g128r w36 hyst hyst w36 g128r b yhb; do
  round=$((round + 1)); dir="$out/$(printf %02d $round)-$col"; mkdir -p "$dir"
  while awk -v l="$(load)" 'BEGIN {exit !(l >= 8)}'; do sleep 20; done
  echo "$(date +%T) $dir: ${build[$col]} ${knobs[$col]}, load $(load)" | tee -a "$out/takes.log"
  cd "$R"
  # shellcheck disable=SC2086
  env TERTIUS_PROBE_EXPOSURE=-4.089 TERTIUS_PROBE_KEEP=none TERTIUS_PROBE_SERIES="$dir/frames.series" TERTIUS_X_FIRST_FILMED=1 TERTIUS_X_FROZEN=1 ${knobs[$col]} \
    bash tools/gpu-lock.sh "$dir/lock.log" $B/${build[$col]}/probe-bin/Tertius.Demo \
    --script "$out/script.json" --headless --out "$dir" --size 1920x1080 > "$dir/run.log" 2>&1
  echo "$(date +%T) $dir exit $?, load $(load), $(command ls "$dir"/*.png 2>/dev/null | wc -l) pictures" | tee -a "$out/takes.log"
done
