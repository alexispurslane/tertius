#!/usr/bin/env bash
# Research tool (issue #42): four crossing films in a 2 x 2 grid with labels, and a sheet of the frames around the
# 0.25 m grid's step (frames 96 to 107, the camera at x = 0.12 to 0.13 m on the way out).
#   bash compose-grid.sh <out.mp4> <film> <label> <film> <label> <film> <label> <film> <label>
# The order is top left, top right, bottom left, bottom right. Writes <out.mp4> and <out>-sheet.png.
set -euo pipefail
out=$1
shift
font=${FONT:-/System/Library/Fonts/Supplemental/Arial.ttf}
work=$(mktemp -d)
inputs=()
filters=""
for i in 0 1 2 3; do
    inputs+=(-i "$1")
    printf '%s' "$2" > "$work/$i.txt"
    filters+="[$i:v]drawtext=fontfile=$font:textfile=$work/$i.txt:x=12:y=10:fontsize=18:fontcolor=white:box=1:boxcolor=black@0.6:boxborderw=6[v$i];"
    shift 2
done
ffmpeg -y -loglevel error "${inputs[@]}" -filter_complex \
    "${filters}[v0][v1][v2][v3]xstack=inputs=4:layout=0_0|w0_0|0_h0|w0_h0" -c:v libx264 -crf 12 -pix_fmt yuv420p "$out"
ffmpeg -y -loglevel error -i "$out" -vf "select='between(n\,96\,107)',scale=640:-1,tile=3x4" -frames:v 1 "${out%.mp4}-sheet.png"
rm -rf "$work"
