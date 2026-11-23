#!/usr/bin/env bash
# Tool code of ter-yglw: the card's timed runs, on the worker, in the job's work folder (tools/runpod/WORKBENCH.md).
#   bash <tree>/docs/research/environment-face-cost/tools/card.sh <tree> <rounds> [<every> <faces per face frame>]...
# Each pair after <rounds> adds a set of runs with TERTIUS_PROBE_FACE_EVERY and TERTIUS_PROBE_FACES_PER_FRAME (default
# one set, 2 1), named <every>x<faces>-r<round>-w<size>-c<cull>; VARIANTS (env) replaces the four variants. A variant
# size:cull:parts (t1-6tn2) runs with TERTIUS_PROBE_FACE_PARTS=parts and the name suffix -parts.
# It applies the probe (apply-face.py) to the tree, builds its Release demo, makes one discarded warm-up run (t1-80jd),
# and then <rounds> rounds of the four variants (face size : cull) in a rotated order on the deck script (deck-240.json:
# demo-exit's deck camera from frame 399, 240 frames, 1280 x 720, two frames in flight), as queue-mac.sh does on the
# Mac. Each run writes out/r<round>-w<size>-c<cull>/ with gpu-timings.json, faces.log, frames.log and run.log. The
# card's clocks go to out/clocks.csv every 500 ms.
set -euo pipefail
tree=$1; rounds=$2; shift 2
sets=("$@"); [[ ${#sets[@]} -eq 0 ]] && sets=(2 1)
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
run() { # name size cull every faces [parts]
    mkdir -p "out/$1"
    env TERTIUS_PROBE_FACE_PARTS="${6:-}" TERTIUS_PROBE_FACE_SIZE="$2" TERTIUS_PROBE_FACE_CULL="$3" TERTIUS_PROBE_FACE_EVERY="${4:-2}" \
        TERTIUS_PROBE_FACES_PER_FRAME="${5:-1}" \
        TERTIUS_PROBE_FACE_LOG="out/$1/faces.log" TERTIUS_PROBE_FRAME_TIMES="out/$1/frames.log" \
        "$demo" --script "$script" --headless --out "out/$1" --video none --size 1280x720 --in-flight 2 \
        > "out/$1/run.log" 2>&1 || { tail -20 "out/$1/run.log" >&2; exit 1; }
    echo "$1: $(grep -h 'GPU frame span' "out/$1/run.log" || echo 'no frame span')"
}
run warm-up 128 0
rm -rf out/warm-up
read -r -a variants <<< "${VARIANTS:-128:0 128:1 32:0 32:1}"
nv=${#variants[@]}
for ((s = 0; s < ${#sets[@]}; s += 2)); do
    every=${sets[s]}; faces=${sets[s + 1]}
    prefix=$([[ "$every $faces" == "2 1" ]] || echo "${every}x$faces-")
    # VARIANTS_<every>x<faces> (env), when set, replaces VARIANTS for that set (t1-6tn2).
    vname="VARIANTS_${every}x$faces"
    read -r -a variants <<< "${!vname:-${VARIANTS:-128:0 128:1 32:0 32:1}}"
    nv=${#variants[@]}
    for ((r = 0; r < rounds; r++)); do
        for ((k = 0; k < nv; k++)); do
            v=${variants[$(((k + r) % nv))]}; size=${v%%:*}; rest=${v#*:}; cull=${rest%%:*}
            parts=$([[ "$rest" == *:* ]] && echo "${rest#*:}")
            run "${prefix}r$r-w$size-c$cull${parts:+-$parts}" "$size" "$cull" "$every" "$faces" "$parts"
        done
    done
done
