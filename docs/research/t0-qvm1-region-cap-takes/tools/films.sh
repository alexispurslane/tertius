#!/usr/bin/env bash
# Tool code of the region cap's takes (t0-qvm1). films.sh: the frames of the two side-by-side videos (film.py), main's
# code (pm) before the cap (pc): the frozen bay at -4.24 stops (the exposure that the bay's stills settled at by draw
# 890), and rect-bay at the fixed exposure that the survey's film of main chose (runs/film-pm). One run at a time, each
# after the one-minute load falls under LOAD (default 12), and only at 20 GiB free or more.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd)
B=/private/tmp/t0-qvm1c
freegb() { df -Pk /System/Volumes/Data | awk 'NR == 2 {print int($4 / 1048576)}'; }
quiet() {
  [ "$(freegb)" -lt 20 ] && { echo "$(date +%T) only $(freegb) GiB free: stop"; exit 2; }
  until awk -v l="${LOAD:-12}" '{exit !($1 < l)}' <(sysctl -n vm.loadavg | tr -d '{}'); do sleep 30; done
}
stops=$(python3 -c "import json,glob; print(json.load(open(glob.glob('$B/runs/film-pm/results/motion/rect-bay.json')[0]))['stops'])")
cd "$R"
for film in still-bay:-4.24 rect-bay:$stops; do
  for build in pm pc; do
    quiet; echo "$(date +%T) film $build ${film%%:*} at ${film#*:} stops"
    nix develop --command python3 -I "$T/film.py" $build ${film%%:*} ${film#*:} > "$B/films-$build-${film%%:*}.log" 2>&1 \
      || { echo "$(date +%T) failed: $B/films-$build-${film%%:*}.log"; exit 1; }
    tail -1 "$B/films-$build-${film%%:*}.log"
  done
done
echo "$(date +%T) films done"
