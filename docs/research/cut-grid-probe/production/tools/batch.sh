#!/usr/bin/env bash
# batch.sh <script name> <stops> <name>:<bin>:<env;env> ...
set -euo pipefail
T=/private/tmp/t1-3er4
WT=/Users/alexispurslane/.t3/worktrees/tertius/t3-88afe8ff-7637aee74917d336
SCRIPT=$WT/docs/research/reflection-mixture-adaptive/production/scripts/$1.json STOPS=$2; shift 2
for spec in "$@"; do
  IFS=: read -r name bin envs <<< "$spec"
  IFS=';' read -ra envarr <<< "${envs:-X=1}"
  bash $T/tools/take.sh "$name" "$T/$bin" "$SCRIPT" "$STOPS" "${envarr[@]}"
  python3 -I $T/tools/extract.py "${TAKES:-$T/takes}/$name"
  rm -f "${TAKES:-$T/takes}/$name/film.mkv"
done
echo BATCH_DONE; /bin/df -g / | awk 'NR==2{print "free GiB", $4}'
