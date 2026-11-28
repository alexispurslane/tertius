#!/usr/bin/env bash
# Research tool (issue #22): runs every reader on the captures of render-evidence.sh and red-only.sh and writes their
# output to docs/research/fine-grid-outgoing-light/evidence/measurements.txt.
#   bash docs/research/fine-grid-outgoing-light/tools/measurements.sh
set -euo pipefail
scope=captures/issue22
tools="$(cd "$(dirname "$0")" && pwd)"
out="$tools/../evidence/measurements.txt"
{
    echo "# Readbacks of the volume buffer and the HDR image, from $scope."
    echo "# Fix: $(cat "$scope/commit.txt"), the commit that render-evidence.sh rendered. Baseline: that commit with the engine change reversed, equal to main d3123fe71 in the volume pass and its shaders."
    for scene in deck-gray deck-art; do
        for variant in candidate baseline; do
            echo
            echo "## $scene, $variant: transfer between grids in the last frame"
            flag=()
            if [[ $variant == baseline ]]; then flag=(--baseline); fi
            python3 -I "$tools/measure-volume.py" "$scope/$variant/$scene/volume.bin" "${flag[@]}"
        done
    done
    # The probe scene: the -Z face of the 0.5 m cell in front of the red slab. The camera's x picks the cell.
    for entry in candidate:outside:0.05 candidate:across:0.2 baseline:outside:0.05 baseline:across:0.2 \
        candidate:shell-only:0.2 candidate:red-outside:0.05 candidate:red-across:0.2; do
        IFS=: read -r variant view x <<< "$entry"
        echo
        echo "## probe, $variant, $view, camera x = $x m"
        flag=()
        if [[ $variant == baseline ]]; then flag=(--baseline); fi
        python3 -I "$tools/measure-volume.py" "$scope/$variant/$view/volume.bin" --camera-x "$x" "${flag[@]}"
    done
    for view in outside across; do
        echo
        echo "## probe, $view: receiver center pixel, scene-linear W/(m² sr), baseline line first"
        python3 -I "$tools/receiver-pixels.py" "$scope/baseline/$view" "$scope/candidate/$view"
    done
    echo
    echo "## whole-frame means"
    python3 -I "$tools/frame-means.py" "$scope"/baseline/{outside,across} "$scope"/candidate/{outside,across,red-outside,red-across} \
        "$scope"/{baseline,candidate}/deck-art
} > "$out"
rm -rf "$tools/__pycache__"
echo "wrote $out"
