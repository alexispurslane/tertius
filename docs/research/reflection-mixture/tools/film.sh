#!/bin/bash
# ter-fe63 stage 1: the nose's film, real frames at 60 a second: the orbit scene from tick 277003, 360 frames of one tick
# each from the person camera, at the fixed render scale 2/3 and the fixed exposure -1.7507 stops, probe-head. The
# caller's environment picks main, the coin or the exact count. Usage: film.sh <out name>
set -e
out=/private/tmp/t1-wl7c/fe63/$1
rm -rf "$out"; mkdir -p "$out"
cp /private/tmp/t1-wl7c/fe63/nose-film.json "$out/script.json"
cd /private/tmp/t1-wl7c/probe-head
/bin/df -g / | tail -1; uptime
env TERTIUS_PROBE_EXPOSURE=-1.7507 \
  bash /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7/tools/gpu-lock.sh --shared "$out/lock.log" \
  src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo --script "$out/script.json" --headless --out "$out" \
  --size 1920x1080 --render-scale 0.6667 > "$out/run.log" 2>&1
echo "$1 exit $?"; ls "$out" | wc -l
