#!/usr/bin/env bash
# Tool code of ter-fe63's stage 2. The untimed takes of unpatched main and the production branch, at 1920 x 1080 and a
# render scale of 0.6667 (1280 x 720), each under the shared GPU lock, with a fixed exposure and a lossless film.
# Usage: bash takes.sh <main binary> <branch binary> <scripts dir> <out dir>
set -euo pipefail
MAIN=$1 BRANCH=$2 SCRIPTS=$3 OUT=$4
LOCK=/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh
mkdir -p "$OUT"

check_disk() {
  local free_g
  free_g=$(/bin/df -g / | awk 'NR==2{print $4}')
  if [ "$free_g" -lt 22 ]; then
    echo "DISK_FLOOR: $free_g GiB free, under 22" >&2
    exit 1
  fi
}

take() {
  local name=$1 bin=$2 script=$3 stops=$4 video=$5
  check_disk
  local dir="$OUT/$name"
  mkdir -p "$dir"
  local args=(--script "$SCRIPTS/$script.json" --headless --out "$dir" --size 1920x1080 --render-scale 0.6667
    --exposure "$stops")
  if [ "$video" = video ]; then
    args+=(--video "$dir/film.mkv" --fps 60)
  fi
  echo "=== $name $(date +%H:%M:%S) load $(uptime | sed -E 's/.*load averages?: ([0-9.]+).*/\1/')"
  bash "$LOCK" --shared "$dir/lock.log" "$bin" "${args[@]}" > "$dir/run.log" 2>&1
}

for build in main branch; do
  bin=$MAIN
  [ "$build" = branch ] && bin=$BRANCH
  take "plate-$build" "$bin" plate-still 0.73 still
  take "nose-film-$build" "$bin" nose-film -1.7507 video
  take "lamps-film-$build" "$bin" lamps-film 3.83 video
  take "cut-nose-$build" "$bin" real-cut-nose -1.7507 video
  take "cut-lamps-$build" "$bin" real-cut-lamps 3.83 video
  take "pan-$build" "$bin" pan-hall -1.7507 video
  take "walk-$build" "$bin" walk-hall -1.7507 video
done
# A second run of the branch's walk, for determinism.
take walk-branch-2 "$BRANCH" walk-hall -1.7507 video
du -sh "$OUT"
echo TAKES_DONE
