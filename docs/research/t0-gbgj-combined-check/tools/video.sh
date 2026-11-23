#!/usr/bin/env bash
# Tool code of t0-gbgj. video.sh <out.mp4> <width> <left label> <left run> <right label> <right run>: a side-by-side video of
# two films whose frames film.py kept in $W/runs/<run>/frames, left and right in one frame, a label at the top left of
# each side, and the film's frame (counted from 0, as the survey counts it) at the bottom right. Every kept frame is one
# video frame at 60 frames a second, in the survey's order of the files (m*-*.png sorted), none skipped or blended. Each
# side is <width> pixels wide (1920: the output's own size; 1280: scaled with Lanczos to 1280 x 720). H.264 in an .mp4,
# yuv420p, CRF 12, preset slow, no denoise filter. ffmpeg comes from nix shell nixpkgs#ffmpeg.
set -euo pipefail
. "$(dirname "$0")/vars.sh"
out=$1; width=$2; ll=$3; lr=$4; rl=$5; rr=$6
height=$((width * 9 / 16)); font=/System/Library/Fonts/Supplemental/Arial.ttf
n_l=$(ls "$W/runs/$lr/frames" | grep -c '^m.*-.*\.png$'); n_r=$(ls "$W/runs/$rr/frames" | grep -c '^m.*-.*\.png$')
[ "$n_l" = "$n_r" ] || { echo "the films differ in length: $n_l and $n_r frames"; exit 1; }
label() { echo "scale=$width:$height:flags=lanczos,drawtext=fontfile=$font:text='$1':x=12:y=10:fontsize=$((height / 30)):fontcolor=white:box=1:boxcolor=black@0.6:boxborderw=6"; }
nix shell nixpkgs#ffmpeg --command ffmpeg -hide_banner -loglevel error -y \
  -framerate 60 -pattern_type glob -i "$W/runs/$lr/frames/m*-*.png" \
  -framerate 60 -pattern_type glob -i "$W/runs/$rr/frames/m*-*.png" \
  -filter_complex "[0:v]$(label "$ll")[a];[1:v]$(label "$rl")[b];[a][b]hstack=inputs=2,drawtext=fontfile=$font:text='film frame %{eif\:n\:d}':x=w-tw-12:y=h-th-12:fontsize=$((height / 30)):fontcolor=white:box=1:boxcolor=black@0.6:boxborderw=6[v]" \
  -map "[v]" -c:v libx264 -preset slow -crf 12 -pix_fmt yuv420p -r 60 -movflags +faststart "$out"
nix shell nixpkgs#ffmpeg --command ffprobe -v error -count_frames -select_streams v:0 \
  -show_entries stream=width,height,nb_read_frames,r_frame_rate,pix_fmt,codec_name -of csv=p=0 "$out"
echo "$out $(du -h "$out" | cut -f1) ($n_l frames)"
