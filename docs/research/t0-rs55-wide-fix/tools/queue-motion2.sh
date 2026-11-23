#!/usr/bin/env bash
# Tool code of t0-rs55. queue-motion2.sh: the motion take's video runs on the Mac (the line orchestrator's order of
# 2026-11-23, after the early result): the turn and the cabin walk, today (m0v) | the wide fix (m1v), each side playing back
# its own card log of round 1 (job motion1, cardwide.sh under FrameTimeTarget(16.67, 0.5, 1)), through motionrun.sh: every
# draw of the window as a PNG, and the band's history dumps. Then band.py, the videos (videos.py) and the PNGs' deletion is
# left to the caller. A run starts at 20 GiB of free disk or more and at 21 or more when it keeps more than 0.5 GB (the
# line orchestrator's limits); the queue stops under that.
T=$(cd "$(dirname "$0")" && pwd); C=/private/tmp/t0-gbgj/cloud-rs55/motion1/out/card
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for film in turn walk; do
  for side in today:m0v:main wide:m1v:wide; do
    IFS=: read code build card <<< "$side"
    [ "$(freegb)" -lt 21 ] && { echo "under 21 GiB, stop"; exit 1; }
    REPLAY=$(ls $C/$film-$card-r1/size-*.txt) bash "$T/motionrun.sh" $build $film $film-$code
  done
done
echo "$(date +%T) queue-motion2.sh done"
