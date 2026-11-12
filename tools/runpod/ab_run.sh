#!/usr/bin/env bash
# Tool code (tools/runpod/WORKBENCH.md): the A/B runs of a GPU job, on the worker, in the job's work folder.
#   bash main/tools/runpod/ab_run.sh <script> <runs> <tree>...
# It builds the Release demo of each tree in this one command, so that dotnet's build server stays warm between the
# builds. Then it runs each tree's demo <runs> times on its own copy of <script>, a path inside the tree, and writes
# out/<tree>/run-<n>/ with gpu-timings.json and out/<tree>/run-<n>.log. The order of the trees reverses in every
# round (A B, B A, A B, ...), so that a drift of the card's clock or heat over the job falls on both sides alike.
# Before the rounds, one run of the first tree warms the card and is not kept: the first run on a fresh worker was
# 0.5 ms slower than the rest in three jobs of 2026-11-12, and the first tree is always the base.
# out/clocks.csv logs the card's clocks every 500 ms during the runs.
# DEMO_ARGS replaces the demo's options after --headless and --out; the default makes a timing run without images.
# For stills, give the options without --video none, for example DEMO_ARGS='--size 1920x1080'.
set -euo pipefail

if [[ $# -lt 3 ]]; then
    echo "usage: ab_run.sh <script> <runs> <tree>..." >&2
    exit 2
fi
script=$1
runs=$2
shift 2
trees=("$@")
demo_args=${DEMO_ARGS:---size 1920x1080 --video none}

for tree in "${trees[@]}"; do
    echo "building $tree"
    (cd "$tree" && dotnet build src/Tertius.Demo -c Release -nologo -v:q | tail -3)
done

mkdir -p out
nvidia-smi --query-gpu=timestamp,clocks.sm,clocks.mem,temperature.gpu,power.draw --format=csv -lms 500 \
    > out/clocks.csv 2>&1 &
clocks=$!
trap 'kill "$clocks" 2> /dev/null || true' EXIT

echo "warming up with ${trees[0]}"
# shellcheck disable=SC2086 # DEMO_ARGS holds several options.
(cd "${trees[0]}" && src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo --script "$script" --headless \
    --out ../out/warm-up $demo_args) > out/warm-up.log 2>&1 || { tail -20 out/warm-up.log >&2; exit 1; }
rm -rf out/warm-up

for ((round = 1; round <= runs; round++)); do
    order=("${trees[@]}")
    if ((round % 2 == 0)); then
        order=()
        for ((i = ${#trees[@]} - 1; i >= 0; i--)); do order+=("${trees[i]}"); done
    fi
    for tree in "${order[@]}"; do
        mkdir -p "out/$tree"
        log="out/$tree/run-$round.log"
        # shellcheck disable=SC2086 # DEMO_ARGS holds several options.
        if ! (cd "$tree" && src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo --script "$script" --headless \
            --out "../out/$tree/run-$round" $demo_args) > "$log" 2>&1; then
            echo "the run $round of $tree failed:" >&2
            tail -20 "$log" >&2
            exit 1
        fi
        echo "$tree run $round: $(grep -h 'GPU frame span' "$log" || echo 'no frame span')"
    done
done
