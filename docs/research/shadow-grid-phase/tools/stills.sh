#!/usr/bin/env bash
# Tool code (t1-gout, step 3). stills.sh <bin> <out of stills.py prepare> <run folder>: renders each cut script of
# stills.py at 1920 x 1080, the headless render scale 2/3, the automatic exposure and no bloom (as the survey's still
# part), one run per script under the GPU lock's shared mode ($GPU_LOCK), into <run folder>/<script name>. It then
# deletes every frame that no case names. A finished script leaves <script name>.done, so a stopped run goes on.
set -u
bin=$1; out=$2; run=$3
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
mkdir -p "$run"
for s in "$out"/scripts/*.json; do
  n=$(basename "$s" .json)
  [ -e "$run/$n.done" ] && continue
  /bin/rm -rf "$run/$n"
  nix develop "$R" --command bash "${GPU_LOCK:-/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh}" --shared \
    "$run/$n.lock.log" "$bin" --script "$s" --headless --out "$run/$n" --size 1920x1080 --bloom off > "$run/$n.log" 2>&1 \
    || echo "$n failed: $(tail -1 "$run/$n.log")"
  keep=$(awk -v s="$n.json" '$2 == s { printf "%s.png ", $4 }' "$out/cases.txt")
  for f in "$run/$n"/*.png; do
    case " $keep " in *" $(basename "$f") "*) ;; *) /bin/rm -f "$f" ;; esac
  done
  touch "$run/$n.done"
  echo "$(date +%T) $n"
done
