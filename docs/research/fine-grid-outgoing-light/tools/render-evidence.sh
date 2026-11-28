#!/usr/bin/env bash
set -euo pipefail
scope="$PWD/captures/issue22"
tools="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$scope"
base=d3123fe715dea1fd141649271c315096712d72a2
candidate=$(git rev-parse HEAD)
content=/Users/alexispurslane/Development/tertius/content
if [[ -n $(git status --short --untracked-files=no -- src) ]]; then
    echo "The evidence run needs a clean worktree."
    exit 1
fi

git diff "$base" "$candidate" -- src/Tertius.Rendering/Passes/LightVolumePass.cs src/Tertius.Rendering/Shaders > "$scope/engine.patch"
flipped=false
restore() {
    if "$flipped"; then git apply "$scope/engine.patch"; fi
    git restore --worktree -- src/Tertius.Demo/DemoScenes.cs src/Tertius.Rendering/VulkanRenderer.cs
    rm -f src/Tertius.Demo/DemoScenes.Issue22Probe.cs
}
trap restore EXIT
python3 -I "$tools/prepare-render.py"
git diff -- src/Tertius.Demo/DemoScenes.cs src/Tertius.Rendering/VulkanRenderer.cs > "$scope/probe.patch"
git diff --no-index /dev/null src/Tertius.Demo/DemoScenes.Issue22Probe.cs >> "$scope/probe.patch" || test $? -eq 1

render() {
    local dir=$1
    local script=$2
    shift 2
    mkdir -p "$dir"
    env TERTIUS_LIGHT_VOLUME=1 TERTIUS_ISSUE22_VOLUME_DUMP="$dir/volume.bin" \
        bash tools/gpu-lock.sh --shared "$dir/render.log" \
        dotnet run --project src/Tertius.Demo -c Release --no-build -- \
        --script "$script" --headless --out "$dir" "$@"
}

for variant in candidate baseline; do
    if [[ "$variant" == baseline ]]; then
        git apply --reverse "$scope/engine.patch"
        flipped=true
    fi
    dotnet build src/Tertius.Demo -c Release -v quiet
    for view in outside across; do
        TERTIUS_ISSUE22_PIXEL_DUMP=1 render "$scope/$variant/$view" "$scope/$view.json" \
            --exposure 0 --environment-map off --upscaling off --screen-trace off
    done
    if [[ "$variant" == candidate ]]; then
        render "$scope/$variant/shell-only" "$scope/shell-only.json" \
            --exposure 0 --environment-map off --upscaling off --screen-trace off
    fi
    render "$scope/$variant/deck-gray" scripts/temporal-upscale-timing.json \
        --size 1920x1080 --video none --gray-box --gpu-run deformation..outlines
    TERTIUS_CONTENT_ROOT="$content" TERTIUS_ISSUE22_PIXEL_DUMP=1 \
        render "$scope/$variant/deck-art" docs/research/frame-time-sweep-2026-11-11/acceptance-ter-w1o3/bigscripts/deck-1080.json \
        --size 960x540 --exposure 0 --upscaling off
done
