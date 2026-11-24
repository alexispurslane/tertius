#!/bin/bash
# ter-fe63 stage 1: a film of real frames at 60 a second from a film script, at the fixed render scale 2/3 and a fixed
# exposure, probe-head. The caller's environment picks main, the coin or the exact count.
# Usage: film2.sh <script.json> <exposure stops> <out name>
set -e
out=/private/tmp/t1-wl7c/fe63/$3
rm -rf "$out"; mkdir -p "$out"
cp "$1" "$out/script.json"
cd /private/tmp/t1-wl7c/probe-head
env TERTIUS_PROBE_EXPOSURE=$2 \
  bash /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7/tools/gpu-lock.sh --shared "$out/lock.log" \
  src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo --script "$out/script.json" --headless --out "$out" \
  --size 1920x1080 --render-scale 0.6667 > "$out/run.log" 2>&1 < /dev/null
echo "$3 exit $?"; ls "$out" | wc -l
