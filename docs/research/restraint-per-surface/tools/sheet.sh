#!/usr/bin/env bash
# Research tool (issue #42): one labeled row of stills of the same capture name from several variant folders.
#   bash sheet.sh <out.png> <capture name> <folder>=<label>...
# Example: bash sheet.sh evidence/warm-white.png warm-outside captures/issue42/v0-g1.0='today' ...
set -euo pipefail
out=$1
name=$2
shift 2
font=${FONT:-/System/Library/Fonts/Supplemental/Arial.ttf}
tiles=()
for pair in "$@"; do
    folder=${pair%%=*}
    label=${pair#*=}
    tile=$(mktemp -t sheet.XXXXXX).png
    magick "$folder/$name/$name.png" -alpha off -font "$font" -pointsize 20 -fill white -undercolor '#000000a0' \
        -gravity northwest -annotate +10+8 " $label " "$tile"
    tiles+=("$tile")
done
magick "${tiles[@]}" +append "$out"
rm -f "${tiles[@]}"
