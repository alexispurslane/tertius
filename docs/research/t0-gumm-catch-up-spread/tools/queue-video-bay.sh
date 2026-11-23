#!/usr/bin/env bash
# Tool code of t0-gumm. queue-video-bay.sh: the Mac runs of the bay start's video, today | none | h16 (fairrun.sh with
# VIDEO=1: every draw 0 to 119), each playing back its own card scale log (logs/card-fair1/start-<code>-r2, the second run
# of card job fair1), then videos.py: pics/bay-start-final.mp4. Each run starts at 20 GiB of free disk or more (the line
# orchestrator's answer 92: runs down to 20 GiB, builds at 22); a run whose 120 pictures are there is kept.
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/.." && pwd); R=$(cd "$N/../../.." && pwd); F=/private/tmp/t0-gbgj/gumm-fair
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
for run in today:xgz:"1 8 0" none:xcz:"0 32 0" h16:xcz:"0.5 16 0 0"; do
  IFS=: read code build shape <<< "$run"
  [ "$(ls $F/v-bay-$code/f-*.png 2>/dev/null | wc -l)" -ge 120 ] && continue
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  VIDEO=1 REPLAY=$N/logs/card-fair1/start-$code-r2/size-none.txt bash "$T/fairrun.sh" $build bay start v-bay-$code "TERTIUS_X_CATCHUP=$shape"
  [ "$(ls $F/v-bay-$code/f-*.png 2>/dev/null | wc -l)" -ge 120 ] || { echo "$(date +%T) v-bay-$code: incomplete"; exit 1; }
done
cd "$R" && nix develop --command python3 "$T/videos.py" "$N/pics/bay-start-final.mp4" start "today (1, 8, 0)|$F/v-bay-today" \
  "none (0, 32, 0)|$F/v-bay-none" "h16 (0.5, 16, 0)|$F/v-bay-h16" 2>&1 | grep -v -i 'warning\|sqlite\|flake'
echo "$(date +%T) queue-video-bay.sh done"
