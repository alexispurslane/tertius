#!/usr/bin/env bash
# Tool code of t0-gbgj. wintest.sh <build name> <side label> <W>x<H>...: wintest.py on $W/bins/<build>/probe-bin for each
# window, 40 draws each, at 4.08 stops with the default tone curve and bloom, under the GPU lock's shared mode, into
# $W/runs/win-<side>-<W>x<H>; prints one line per window. Run from anywhere.
. "$(dirname "$0")/vars.sh"
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
build=$1; side=$2; shift 2
cd "$R"
for size in "$@"; do w=${size%x*}; h=${size#*x}; out=$W/runs/win-$side-$size
  if [ ! -e "$out/frames/f-000039.png" ]; then
    /bin/rm -rf "$out"; mkdir -p "$out"
    nix develop --command python3 -I "$T/wintest.py" prepare "$out" $w $h
    env TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_REPLAY="$out/replay.txt" \
      TERTIUS_X_SIZE_LOG="$out" TERTIUS_PROBE_SERIES="$out/frames.series" TERTIUS_PROBE_KEEP="$(cat "$out/keep.txt")" \
      nix develop --command bash tools/gpu-lock.sh --shared "$out/lock.log" "$W/bins/$build/probe-bin/Tertius.Demo" \
      --script "$out/script.json" --headless --out "$out/frames" --size ${w}x${h} > "$out/run.log" 2>&1 || echo "$side $size render failed: $(grep -m1 -o 'ErrorIncompatibleDriver\|Exception.*' "$out/run.log")"
  fi
  echo "$side $(nix develop --command python3 -I "$T/wintest.py" measure "$out" $w $h 2>/dev/null | tail -1)  sizes logged: $(cut -d' ' -f2,3 "$out"/size-*.txt 2>/dev/null | sort -u | tr '\n' ';')"
done
