#!/usr/bin/env bash
# One untimed take under the shared GPU lock: take.sh <name> <bin dir> <script json> <stops> [ENV=VALUE ...]
set -euo pipefail
NAME=$1 BIN=$2 SCRIPT=$3 STOPS=$4; shift 4
OUT=${TAKES:-/private/tmp/t1-3er4/takes}/$NAME
free_g=$(/bin/df -g / | awk 'NR==2{print $4}')
if [ "$free_g" -lt 21 ]; then echo "DISK_FLOOR: $free_g GiB" >&2; exit 3; fi
mkdir -p "$OUT"
LOCK=/Users/alexispurslane/.t3/worktrees/tertius/t3-88afe8ff-7637aee74917d336/tools/gpu-lock.sh
env "$@" bash "$LOCK" --shared "$OUT/lock.log" "$BIN/Tertius.Demo" --script "$SCRIPT" --headless --out "$OUT" \
  --size 1920x1080 --render-scale 0.6667 --exposure "$STOPS" --video "$OUT/film.mkv" --fps 60 > "$OUT/run.log" 2>&1
echo "$NAME done $(du -sh "$OUT/film.mkv" | cut -f1)"
