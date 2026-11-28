#!/usr/bin/env bash
# Research tool (issue #22): films the mixed probe scene while the camera crosses the 0.25 m grid's boundary and back,
# on the fix at HEAD and on the baseline (the engine change reversed), and puts the two films side by side.
#   nix develop --command bash docs/research/fine-grid-outgoing-light/tools/crossing-video.sh [<base commit>]
# Writes captures/issue22/crossing/{baseline,fix}.mkv and docs/research/fine-grid-outgoing-light/evidence/crossing.mp4.
# Needs a clean checkout, and restores it on exit.
set -euo pipefail
scope="$PWD/captures/issue22"
tools="$(cd "$(dirname "$0")" && pwd)"
base=${1:-d3123fe715dea1fd141649271c315096712d72a2}
font=${FONT:-/System/Library/Fonts/Supplemental/Arial.ttf}
if [[ -n $(git status --short --untracked-files=no -- src) ]]; then
    echo "The crossing film needs a checkout with no change under src/."
    exit 1
fi
mkdir -p "$scope/crossing"
engine="$scope/crossing/engine.patch"
git diff "$base" HEAD -- src/Tertius.Rendering/Passes/LightVolumePass.cs src/Tertius.Rendering/Shaders > "$engine"
flipped=false
restore() {
    if "$flipped"; then git apply "$engine"; fi
    git restore --worktree -- src/Tertius.Demo/DemoScenes.cs src/Tertius.Rendering/VulkanRenderer.cs
    rm -f src/Tertius.Demo/DemoScenes.Issue22Probe.cs
}
trap restore EXIT
python3 -I "$tools/prepare-render.py"
for variant in fix baseline; do
    if [[ "$variant" == baseline ]]; then
        git apply --reverse "$engine"
        flipped=true
    fi
    dotnet build src/Tertius.Demo -c Release -v quiet
    mkdir -p "$scope/crossing/$variant"
    env TERTIUS_LIGHT_VOLUME=1 bash tools/gpu-lock.sh --shared "$scope/crossing/$variant/render.log" \
        dotnet run --project src/Tertius.Demo -c Release --no-build -- \
        --script "$scope/crossing.json" --headless --out "$scope/crossing/$variant" \
        --video "$scope/crossing/$variant.mkv" --fps 60 \
        --exposure 0 --environment-map off --upscaling off --screen-trace off
done
label() {
    printf '%s' "$2" > "$scope/crossing/$1.txt"
    echo "drawtext=fontfile=$font:textfile=$scope/crossing/$1.txt:x=12:y=10:fontsize=18:fontcolor=white:box=1:boxcolor=black@0.6:boxborderw=6"
}
ffmpeg -y -loglevel error -i "$scope/crossing/baseline.mkv" -i "$scope/crossing/fix.mkv" -filter_complex \
    "[0:v]$(label baseline "baseline ${base:0:9}")[a];[1:v]$(label fix "fix $(git rev-parse --short=9 HEAD)")[b];[a][b]hstack=inputs=2" \
    -c:v libx264 -crf 12 -pix_fmt yuv420p "$tools/../evidence/crossing.mp4"
