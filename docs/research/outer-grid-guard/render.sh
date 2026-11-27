#!/usr/bin/env bash
# Research tool (issue #25): renders the hallway walk for main and the guarded outer grid on this machine.
#   bash docs/research/outer-grid-guard/render.sh <work folder> [variant]... [-- demo option...]
# Run it from the repository root, inside the dev shell. For each variant it makes a detached worktree under
# <work folder>/wt/<variant> at the variant's commit, builds the Release demo and renders scripts/hallway-walk.json of
# this checkout into <work folder>/film/<variant>/walk.mkv: 1280 x 720, 60 frames per second, lossless, gray box, fixed
# exposure 3.79 stops and the renderer's other defaults. Options after -- go to every run, for example
# `-- --environment-map off` with EXPOSURE=-3 for the diagnostic films. Without variants it renders base and guard.
# A film takes about 626 MB. compose.sh and analyze.py read the films from <work folder>/film.
set -euo pipefail

work=$(cd "$1" && pwd)
shift
variants=()
while [[ $# -gt 0 && $1 != -- ]]; do variants+=("$1"); shift; done
[[ $# -gt 0 ]] && shift
extra=("$@")
if [[ ${#variants[@]} -eq 0 ]]; then variants=(base guard); fi
root=$(pwd)

commit_of() {
    case $1 in
        base) echo b0f8bbf1b ;;
        guard) echo "${GUARD_COMMIT:-HEAD}" ;;
        *) echo "$1" ;;
    esac
}

for variant in "${variants[@]}"; do
    tree=$work/wt/$variant
    out=$work/film/$variant
    if [[ ! -d $tree ]]; then git worktree add --detach "$tree" "$(commit_of "$variant")"; fi
    (cd "$tree" && dotnet build src/Tertius.Demo -c Release -nologo -v:q | tail -2)
    mkdir -p "$out"
    (cd "$tree" && bash "$root/tools/gpu-lock.sh" --shared "$out/lock.log" \
        src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo --script "$root/scripts/hallway-walk.json" --headless \
        --gray-box --out "$out" --video "$out/walk.mkv" --fps 60 --exposure "${EXPOSURE:-3.79}" "${extra[@]}")
    echo "$variant: $out/walk.mkv"
done
