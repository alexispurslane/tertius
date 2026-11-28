#!/usr/bin/env bash
# Research tool (issue #42): puts two crossing films side by side with labels, and writes a sheet of frames around the
# 0.25 m grid's step (frames 94 to 105, the camera at x = 0.1175 to 0.13 m on the way out).
#   bash compose.sh <left.mkv> <left label> <right.mkv> <right label> <out.mp4>
# Writes <out.mp4> and <out>-sheet.png.
set -euo pipefail
font=${FONT:-/System/Library/Fonts/Supplemental/Arial.ttf}
work=$(mktemp -d)
printf '%s' "$2" > "$work/a.txt"
printf '%s' "$4" > "$work/b.txt"
label() { echo "drawtext=fontfile=$font:textfile=$work/$1.txt:x=12:y=10:fontsize=18:fontcolor=white:box=1:boxcolor=black@0.6:boxborderw=6"; }
ffmpeg -y -loglevel error -i "$1" -i "$3" -filter_complex "[0:v]$(label a)[a];[1:v]$(label b)[b];[a][b]hstack=inputs=2" \
    -c:v libx264 -crf 12 -pix_fmt yuv420p "$5"
ffmpeg -y -loglevel error -i "$5" -vf "select='between(n\,94\,105)',scale=640:-1,tile=3x4" -frames:v 1 "${5%.mp4}-sheet.png"
rm -rf "$work"
