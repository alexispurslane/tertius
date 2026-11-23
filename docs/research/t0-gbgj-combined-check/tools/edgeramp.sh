#!/usr/bin/env bash
# Tool code of t0-5tyj, copied from sizeramp.sh of b53c037f1 into t0-gbgj's record as edgeramp.sh with one change: it runs
# this record's copy of sizeramp.py with TERTIUS_RAMP_PAIRS=1, so it keeps the last draw of every size and the draw 10
# before it, and deletes no frame. edgeramp.py then tracks the window edges in them.
# The original: sizeramp.sh <probe build folder> <out folder> [N]: the size ramp of sizeramp.py on one build.
# The build must carry the survey's probe patch and ../scratch-dynamic-scale.patch (TERTIUS_X_SIZE_REPLAY), as
# playbuild.sh makes them. It prepares the script, the replay and the kept frames into <out folder>, renders them at
# 1920 x 1080 with the default ACES curve and bloom and the fixed exposure of 4.08 stops, the lamps on, under the GPU
# lock's shared mode (a played-back run does not depend on the frame time), then measures the jambs per size into
# <out folder>/ramp.txt and ramp.png. It deletes the frames of the sizes that do not leak.
set -euo pipefail
bin=$1; out=$2; n=${3:-40}
T=$(cd "$(dirname "$0")" && pwd)
R=$(cd "$T/../../../.." && pwd)
mkdir -p "$out"
cd "$R"
TERTIUS_RAMP_PAIRS=1 nix develop --command python3 -I "$T/sizeramp.py" prepare "$out" "$n"
echo "$(date +%T) ramp render $bin"
env TERTIUS_PROBE_EXPOSURE=4.08 TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_REPLAY="$out/replay.txt" \
  TERTIUS_X_SIZE_LOG="$out" TERTIUS_PROBE_SERIES="$out/frames.series" TERTIUS_PROBE_KEEP="$(cat "$out/keep.txt")" \
  nix develop --command bash tools/gpu-lock.sh --shared "$out/lock.log" "$bin/Tertius.Demo" --script "$out/script.json" \
  --headless --out "$out/frames" --size 1920x1080 > "$out/run.log" 2>&1
echo "$(date +%T) ramp render exit $?"
cmp <(cut -d' ' -f2,3 "$out/replay.txt") <(cut -d' ' -f2,3 "$out/size-none.txt") && echo "played-back sizes equal the replay" || echo "played-back sizes DIFFER from the replay"
nix develop --command python3 -I "$T/sizeramp.py" measure "$out" "$out/frames" "$out/ramp"
