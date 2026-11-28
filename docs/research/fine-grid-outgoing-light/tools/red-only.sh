#!/usr/bin/env bash
# Renders the red-only variant of the probe scene on the checked-out engine (the fix at HEAD).
set -euo pipefail
scope="$PWD/captures/issue22"
tools="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$scope"
if [[ -n $(git status --short --untracked-files=no -- src) ]]; then echo "needs a clean worktree"; exit 1; fi
restore() {
    git restore --worktree -- src/Tertius.Demo/DemoScenes.cs src/Tertius.Rendering/VulkanRenderer.cs
    rm -f src/Tertius.Demo/DemoScenes.Issue22Probe.cs
}
trap restore EXIT
python3 -I "$tools/prepare-render.py"
dotnet build src/Tertius.Demo -c Release -v quiet
for view in red-outside red-across; do
    dir="$scope/candidate/$view"
    mkdir -p "$dir"
    env TERTIUS_LIGHT_VOLUME=1 TERTIUS_ISSUE22_VOLUME_DUMP="$dir/volume.bin" TERTIUS_ISSUE22_PIXEL_DUMP=1 \
        bash tools/gpu-lock.sh --shared "$dir/render.log" \
        dotnet run --project src/Tertius.Demo -c Release --no-build -- \
        --script "$scope/$view.json" --headless --out "$dir" \
        --exposure 0 --environment-map off --upscaling off --screen-trace off
done
