#!/usr/bin/env bash
# Research tool (issue #21): renders the hallway walk for each renderer variant on this machine.
#   bash docs/research/hallway-light-grid/render.sh <work folder> [variant]...
# Run it from the repository root, inside the dev shell. For each variant it makes a detached worktree under
# <work folder>/wt/<variant> at the variant's base commit, applies the variant's patch from this folder, builds the
# Release demo and renders scripts/hallway-walk.json of this checkout into <work folder>/film/<variant>/walk.mkv:
# 1280 x 720, 60 frames per second, lossless, gray box, fixed exposure 3.79 stops and the renderer's other defaults.
# Without variants it renders legacy, merged, outer, four and tiles. compose.sh and analyze.py read the films from
# <work folder>/film.
set -euo pipefail

work=$(cd "$1" && pwd)
shift
variants=("$@")
if [[ ${#variants[@]} -eq 0 ]]; then variants=(legacy merged outer four tiles); fi
root=$(pwd)
here=$root/docs/research/hallway-light-grid

base_of() {
    case $1 in
        legacy) echo 860685c71 ;;
        *) echo 90a6be90d ;;
    esac
}

# Each patch holds the variant's commits on top of its base, in order (git format-patch --stdout). legacy takes the
# orbit scene of 90a6be90d, so every variant renders the same scene.
patch_of() {
    case $1 in
        legacy) echo "$here/legacy-scene.patch" ;;
        outer) echo "$here/outer-2m.patch" ;;
        four) echo "$here/four-level.patch" ;;
        tiles) echo "$here/four-level.patch $here/coarse-patches.patch" ;;
        *) echo "" ;;
    esac
}

for variant in "${variants[@]}"; do
    tree=$work/wt/$variant
    out=$work/film/$variant
    if [[ ! -d $tree ]]; then
        git worktree add --detach "$tree" "$(base_of "$variant")"
        for patch in $(patch_of "$variant"); do git -C "$tree" am -q "$patch"; done
    fi
    (cd "$tree" && dotnet build src/Tertius.Demo -c Release -nologo -v:q | tail -2)
    mkdir -p "$out"
    (cd "$tree" && bash "$root/tools/gpu-lock.sh" --shared "$out/lock.log" \
        src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo --script "$root/scripts/hallway-walk.json" --headless \
        --gray-box --out "$out" --video "$out/walk.mkv" --fps 60 --exposure 3.79)
    echo "$variant: $out/walk.mkv"
done
