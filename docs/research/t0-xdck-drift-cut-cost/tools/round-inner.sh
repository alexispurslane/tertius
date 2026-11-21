#!/usr/bin/env bash
# Tool code of t0-xdck. round-inner.sh <round> <side...>: runs under round.sh's GPU lock. For each side, one headless run
# of the film rect-bay at 1920 x 1080 with two frames in flight and --video none, beside gpu-clock, with the side's
# switches from vars.sh. M runs main's build (OUT_M), every other side this ticket's probe. Every side runs at the fixed
# exposure TERTIUS_PROBE_EXPOSURE=$STOPS, the survey's film exposure of main's image take (t-M-rb), and with
# TERTIUS_X_SPAN_DUMP, the scratch switch of both builds that writes each frame's GPU span (spans.txt) and each frame's
# sum of each requested pass range (spans.txt.<first>..<last>). The ranges: "ray plan..diffuse rays" (the ray plan and
# the two ray passes) and "ray plan..accumulate indirect specular" (also the resolve and the accumulation and filter
# passes). The record of the clock goes into each side's gpu-timings.json (tools/gpu_clock.py record).
set -uo pipefail
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144
D=$R/docs/research/t0-xdck-drift-cut-cost/tools
STOPS=-0.648333463
source $D/vars.sh
round=$1; shift
for side in "$@"; do
  out=/private/tmp/t0-xdck/timing/$round-$side
  rm -rf "$out"; mkdir -p "$out"
  if [ "$side" = M ]; then bin=$OUT_M/Tertius.Demo; vars=""; else bin=/private/tmp/t0-xdck/out-x/probe-bin/Tertius.Demo; vars=${!side}; fi
  echo "$side $vars TERTIUS_PROBE_EXPOSURE=$STOPS" > "$out/take.txt"
  echo "$(date +%T) $side $(uptime)"
  env $vars TERTIUS_PROBE_EXPOSURE=$STOPS TERTIUS_X_SPAN_DUMP="$out/spans.txt" bash $R/tools/gpu-clock.sh "$bin" \
    --script /private/tmp/t0-xdck/rect-bay-film.json --headless --out "$out" --video none --size 1920x1080 --in-flight 2 \
    --gpu-run "ray plan..diffuse rays" --gpu-run "ray plan..accumulate indirect specular" > "$out/run.log" 2>&1
  echo "$(date +%T) $side exit $?"
  [ -f "$out/gpu-timings.json" ] && python3 -I $R/tools/gpu_clock.py record "$out/run.log" "$out/gpu-timings.json"
done
