#!/usr/bin/env bash
# Research tool (issue #21): stacks hallway films into one labeled MP4 with the camera's distance to the nose wall.
#   bash docs/research/hallway-light-grid/compose.sh <films folder> <out.mp4> <variant>...
# <films folder> holds <variant>/walk.mkv from render.sh, each 1280 x 720 at 60 frames per second. Four variants make a
# 2 x 2 film of 2560 x 1440, three a row of 3840 x 720, two a row of 2560 x 720. H.264, CRF 16, yuv420p.
set -euo pipefail
films=$1
out=$2
shift 2
variants=("$@")
font=${FONT:-/System/Library/Fonts/Supplemental/Arial.ttf}

# Camera z in the ship's frame: 29.18 m during the first hold, then 0.05 m per tick from frame 31 on, down to -25.72 m
# (the aft-wall and nose-wall dumps). The inner face of the nose wall lies at z = -29.5 m.
distance="if(lt(n\,31)\,58.68\,max(3.78\,58.68-0.05*(n-31)))"

title_of() {
    case $1 in
        legacy) echo 'Legacy ray tracing (860685c71)' ;;
        merged) echo 'Merged grid: 0.25, 0.5, 1 m levels, 16 m (90a6be90d)' ;;
        outer) echo 'Prototype: 0.25, 0.5, 2 m levels, 32 m' ;;
        four) echo 'Reference: 0.25, 0.5, 1, 2 m levels, 32 m' ;;
        tiles) echo 'Diagnostic: 4 levels, 32 m, 4 m patches at 2 m' ;;
        *) echo "$1" ;;
    esac
}

labels=$(mktemp -d)
trap 'rm -rf "$labels"' EXIT
inputs=()
graph=""
stack=""
for i in "${!variants[@]}"; do
    title_of "${variants[i]}" | tr -d '\n' > "$labels/$i.txt"
    inputs+=(-i "$films/${variants[i]}/walk.mkv")
    graph+="[$i:v]drawtext=fontfile=$font:textfile=$labels/$i.txt:x=16:y=14:fontsize=30:fontcolor=white:box=1:"
    graph+="boxcolor=black@0.6:boxborderw=8[v$i];"
    stack+="[v$i]"
done
case ${#variants[@]} in
    4) layout="xstack=inputs=4:layout=0_0|w0_0|0_h0|w0_h0" ;;
    *) layout="hstack=inputs=${#variants[@]}" ;;
esac
graph+="$stack$layout,drawtext=fontfile=$font:text='nose wall %{eif\:$distance\:d}.%{eif\:mod($distance*10\,10)\:d} m':"
graph+="x=(w-tw)/2:y=h-th-20:fontsize=34:fontcolor=white:box=1:boxcolor=black@0.6:boxborderw=10[v]"

ffmpeg -hide_banner -loglevel error -y "${inputs[@]}" -filter_complex "$graph" \
    -map '[v]' -c:v libx264 -preset slow -crf 16 -pix_fmt yuv420p -r 60 -movflags +faststart "$out"
