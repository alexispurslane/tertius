#!/usr/bin/env bash
# Tool code of ter-yglw: the card's timed runs, on the worker, in the job's work folder (tools/runpod/WORKBENCH.md).
#   bash <tree>/docs/research/environment-face-cost/tools/card.sh <tree> <rounds>
# It applies the probe (apply-face.py) to the tree, builds its Release demo, makes one discarded warm-up run (t1-80jd),
# and then <rounds> rounds of the four variants (face size : cull) in a rotated order on the deck script (deck-240.json:
# demo-exit's deck camera from frame 399, 240 frames, 1280 x 720, two frames in flight), as queue-mac.sh does on the
# Mac. Each run writes out/r<round>-w<size>-c<cull>/ with gpu-timings.json, faces.log, frames.log and run.log. The
# card's clocks go to out/clocks.csv every 500 ms.
set -euo pipefail
tree=$1; rounds=$2
tools=$tree/docs/research/environment-face-cost/tools
python3 -I "$tools/apply-face.py" "$tree"
(cd "$tree" && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false \
    -p:TreatWarningsAsErrors=false | tail -3)
demo=$tree/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
script=$(realpath "$tools/deck-240.json")
mkdir -p out
nvidia-smi --query-gpu=timestamp,clocks.sm,clocks.mem,temperature.gpu,power.draw --format=csv -lms 500 \
    > out/clocks.csv 2>&1 &
clocks=$!
trap 'kill "$clocks" 2> /dev/null || true' EXIT
run() { # name size cull
    mkdir -p "out/$1"
    env TERTIUS_PROBE_FACE_SIZE="$2" TERTIUS_PROBE_FACE_CULL="$3" TERTIUS_PROBE_FACE_EVERY=2 \
        TERTIUS_PROBE_FACE_LOG="out/$1/faces.log" TERTIUS_PROBE_FRAME_TIMES="out/$1/frames.log" \
        "$demo" --script "$script" --headless --out "out/$1" --video none --size 1280x720 --in-flight 2 \
        > "out/$1/run.log" 2>&1 || { tail -20 "out/$1/run.log" >&2; exit 1; }
    echo "$1: $(grep -h 'GPU frame span' "out/$1/run.log" || echo 'no frame span')"
}
run warm-up 128 0
rm -rf out/warm-up
variants=(128:0 128:1 32:0 32:1)
for ((r = 0; r < rounds; r++)); do
    for ((k = 0; k < 4; k++)); do
        v=${variants[$(((k + r) % 4))]}
        run "r$r-w${v%%:*}-c${v#*:}" "${v%%:*}" "${v#*:}"
    done
done
