# Diagnostic tool code: sheet.sh <out.jpg> <columns> <image> <label> [<image> <label>]... : a labeled contact sheet, each
# image at half size, in rows of <columns>.
set -eu
out=$1; cols=$2; shift 2
font=/System/Library/Fonts/Supplemental/Arial.ttf
tmp=$(mktemp -d)
i=0
while [ $# -gt 1 ]; do
  magick "$1" -resize 50% -font $font -fill white -undercolor '#000c' -pointsize 18 -annotate +8+22 "$2" "$tmp/$(printf %03d $i).png"
  i=$((i + 1)); shift 2
done
rows=()
for ((r = 0; r * cols < i; r++)); do
  files=(); for ((c = 0; c < cols && r * cols + c < i; c++)); do files+=("$tmp/$(printf %03d $((r * cols + c))).png"); done
  magick "${files[@]}" +append "$tmp/row$r.png"; rows+=("$tmp/row$r.png")
done
magick "${rows[@]}" -append -quality 88 "$out"
rm -r "$tmp"
