#!/bin/bash
# ter-fe63 stage 1: rect-bay's left wall, the survey's motion film rect-bay at the fixed render scale 2/3 and the fixed
# exposure -0.359 stops (t0-qvm1's film), probe-head with the census's records kept in the wall's box (render
# 0,53,240,320) over draws 125 to 135 and dumped at 130. The caller's environment picks main or the mixture
# (TERTIUS_PROBE_REFLECTION_SHARE, TERTIUS_PROBE_RAMP). Usage: wall.sh <out name>
set -e
out=/private/tmp/t1-wl7c/fe63/$1
rm -rf "$out"; mkdir -p "$out"
cp /private/tmp/t1-wl7c/wall/film.json "$out/script.json"
cd /private/tmp/t1-wl7c/probe-head
/bin/df -g / | tail -1; uptime
env TERTIUS_PROBE_EXPOSURE=-0.359 TERTIUS_PROBE_CENSUS="$out/census" TERTIUS_PROBE_CENSUS_DRAWS=125-136 \
  TERTIUS_PROBE_CENSUS_KEEP=0,53,240,320 TERTIUS_PROBE_CENSUS_DUMP_AT=130 TERTIUS_PROBE_CENSUS_KEEP_REFLECTION=0 \
  bash /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7/tools/gpu-lock.sh --shared "$out/lock.log" \
  src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo --script "$out/script.json" --headless --out "$out" \
  --size 1920x1080 --render-scale 0.6667 > "$out/run.log" 2>&1
echo "$1 exit $?"; ls "$out" | wc -l
