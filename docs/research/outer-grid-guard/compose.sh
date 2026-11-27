#!/usr/bin/env bash
# Research tool (issue #25): encodes hallway films into one labeled MP4 with the camera's distance to the nose wall.
#   bash docs/research/outer-grid-guard/compose.sh <films folder> <out.mp4> <variant>...
# <films folder> holds <variant>/walk.mkv from render.sh, each 1280 x 720 at 60 frames per second. One variant makes a
# film of 1280 x 720, two a row of 2560 x 720, left to right in the order given. H.264, CRF 12, yuv420p, so that
# differences of one 8-bit level stay visible.
set -euo pipefail
films=$1
out=$2
shift 2
variants=("$@")
font=${FONT:-/System/Library/Fonts/Supplemental/Arial.ttf}

# Camera z in the ship's frame: 29.18 m during the first hold, then 0.05 m per tick from frame 31 on, down to -25.72 m.
# The inner face of the nose wall lies at z = -29.5 m (docs/research/hallway-light-grid/analyze.py).
distance="if(lt(n\,31)\,58.68\,max(3.78\,58.68-0.05*(n-31)))"

title_of() {
    case $1 in
        base) echo 'main b0f8bbf1b: outer grid of 16 cells at 1 m, fade from the snapped grid' ;;
        guard) echo 'Issue 25: outer grid of 18 cells at 1 m, fade from the camera' ;;
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
    graph+="[$i:v]drawtext=fontfile=$font:textfile=$labels/$i.txt:x=16:y=14:fontsize=26:fontcolor=white:box=1:"
    graph+="boxcolor=black@0.6:boxborderw=8[v$i];"
    stack+="[v$i]"
done
case ${#variants[@]} in
    1) layout="null" ;;
    *) layout="hstack=inputs=${#variants[@]}" ;;
esac
graph+="$stack$layout,drawtext=fontfile=$font:text='nose wall %{eif\:$distance\:d}.%{eif\:mod($distance*10\,10)\:d} m':"
graph+="x=(w-tw)/2:y=h-th-20:fontsize=32:fontcolor=white:box=1:boxcolor=black@0.6:boxborderw=10[v]"

ffmpeg -hide_banner -loglevel error -y "${inputs[@]}" -filter_complex "$graph" \
    -map '[v]' -c:v libx264 -preset slow -crf 12 -pix_fmt yuv420p -r 60 -movflags +faststart "$out"
