#!/usr/bin/env bash
# Research tool (issue #22): renders one still of a real-art script on the fix at HEAD and on the baseline (the engine
# change reversed), with the probe's readback of the volume buffer, the HDR image, the depth and the camera.
#   nix develop --command bash docs/research/fine-grid-outgoing-light/tools/art-pair.sh <script> <name> <demo option>...
# Writes captures/issue22/<name>/{fix,baseline}/. Needs no change under src/, and restores it on exit.
set -euo pipefail
script=$1
name=$2
shift 2
scope="$PWD/captures/issue22/$name"
tools="$(cd "$(dirname "$0")" && pwd)"
base=d3123fe715dea1fd141649271c315096712d72a2
content=${TERTIUS_CONTENT_ROOT:?set TERTIUS_CONTENT_ROOT to the content folder of the art}
if [[ -n $(git status --short --untracked-files=no -- src) ]]; then
    echo "The art pair needs no change under src/."
    exit 1
fi
mkdir -p "$scope"
engine="$scope/engine.patch"
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
    dir="$scope/$variant"
    mkdir -p "$dir"
    env TERTIUS_CONTENT_ROOT="$content" TERTIUS_LIGHT_VOLUME=1 TERTIUS_ISSUE22_VOLUME_DUMP="$dir/volume.bin" \
        TERTIUS_ISSUE22_PIXEL_DUMP=1 bash tools/gpu-lock.sh --shared "$dir/render.log" \
        dotnet run --project src/Tertius.Demo -c Release --no-build -- --script "$script" --headless --out "$dir" "$@"
done
