#!/usr/bin/env bash
# Throwaway (t1-kiaw): the image controls of the remedies that must not change the image. Under the chosen model
# (TERTIUS_BACK_FACE_PROBE=layer), one capture of the crowd and of the cabin per side, with the probe's dump of the back
# share and the depth pyramid's finest level. The pass of stage 1b runs twice, as the control of run to run.
# Run inside the dev shell from the worktree's root, under the GPU lock in shared mode.
set -u
out=${1:-/private/tmp/t1-kiaw/controls}
bin=/private/tmp/t1-kiaw/bin/Tertius.Demo
for view in crowd cabin; do
  for side in pass pass-again cards pulled packed; do
    env=(TERTIUS_BACK_FACE_PROBE=layer)
    case $side in
      cards) env+=(TERTIUS_BACK_FACE_PASS=cards) ;;
      pulled|packed) env+=(TERTIUS_BACK_FACE_FETCH=$side) ;;
    esac
    dir=$out/$view/$side; rm -rf "$dir"; mkdir -p "$dir/dump"
    env "${env[@]}" TERTIUS_BACK_FACE_DUMP="$dir/dump" "$bin" --script "/private/tmp/t1-kiaw/scripts/$view-one.json" \
      --headless --out "$dir" --video none > "$dir/run.log" 2>&1
    echo "$view $side exit $?"
  done
  for side in pass-again cards pulled packed; do
    # The runs write no capture image under --video none. The run of 2026-11-15 still named the glob *.png, so its
    # log holds one line "*.png DIFFERS" per side, which compares no file.
    for f in "$out/$view/pass"/dump/*; do
      rel=${f#$out/$view/pass/}
      if cmp -s "$f" "$out/$view/$side/$rel"; then echo "$view $side $rel equal"; else echo "$view $side $rel DIFFERS"; fi
    done
  done
done
