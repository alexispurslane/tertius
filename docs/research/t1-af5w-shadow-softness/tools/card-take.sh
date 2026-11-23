#!/usr/bin/env bash
# Tool code of t0-t2oq (the card's take of the lean wide half, 2026-11-23), run from the job's work folder:
#   bash lean/probe/card/take.sh
# It builds the Release demo of the trees main (the clean main) and lean (the lean form: shared lookup, 25 gathers),
# with the analyzers off in both (the lean tree's scratch export file does not pass them; they change no code). For
# each view it runs one discarded warm-up per side, then 5 rounds whose side order alternates (main lean, lean main,
# ...). Each run writes out/<view>/<side>/run-<n>/gpu-timings.json. out/card.csv names the card and driver, and
# out/clocks.csv logs the clocks every 500 ms.
set -u
for t in main lean; do
    echo "building $t"
    (cd "$t" && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false | tail -3) || exit 1
done
mkdir -p out
nvidia-smi --query-gpu=name,driver_version,vbios_version --format=csv > out/card.csv 2>&1
nvidia-smi --query-gpu=timestamp,clocks.sm,clocks.mem,temperature.gpu,power.draw --format=csv -lms 500 > out/clocks.csv 2>&1 &
clocks=$!
trap 'kill "$clocks" 2> /dev/null || true' EXIT
here=$(pwd)
run() { # side view label
    local side=$1 view=$2 label=$3 args=()
    case $view in
        exit-deck) args=(--script "$here/lean/probe/card/exit-deck.json" --exposure 0.7238405) ;;
        exit-cabin) args=(--script "$here/lean/probe/card/exit-cabin.json" --exposure -0.44716394 --render-scale 0.96) ;;
    esac
    local env=(); [ "$side" = lean ] && env=(TERTIUS_X_PYRAMID=1095)
    mkdir -p "out/$view/$side"
    (cd "$side" && env ${env[@]+"${env[@]}"} src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo "${args[@]}" --headless \
        --out "$here/out/$view/$side/$label" --size 1920x1080 --video none) > "out/$view/$side/$label.log" 2>&1 \
        || { echo "$side $view $label failed"; tail -20 "out/$view/$side/$label.log"; exit 1; }
    rm -f "out/$view/$side/$label"/*.png
    echo "$(date +%T) $view $side $label: $(grep -h 'GPU frame span' "out/$view/$side/$label.log" || echo 'no frame span')"
}
for view in exit-deck exit-cabin; do
    run main "$view" warm-up
    run lean "$view" warm-up
    for round in 1 2 3 4 5; do
        if ((round % 2 == 1)); then run main "$view" "run-$round"; run lean "$view" "run-$round"
        else run lean "$view" "run-$round"; run main "$view" "run-$round"; fi
    done
done
