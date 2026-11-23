#!/usr/bin/env bash
# Tool code of the region cap's takes (t0-qvm1). queue.sh <cap commit> [stills] [switch] [film]: the takes in order, one
# heavy command at a time, main's code (pm) before the cap (pc) in each pair:
#   builds:  builds.sh;
#   stills:  still.py on the four views;
#   switch:  the survey's fair cabin switch (part switch, case cabin; the glow goes off with the light) through the
#            acceptance record's take.sh, into /private/tmp/t0-qvm1c/runs/switch-<build>;
#   film:    the survey's motion film rect-bay the same way, into /private/tmp/t0-qvm1c/runs/film-<build>.
# A run with a result is kept, so that a stopped queue resumes. A run starts at 20 GiB free or more and after the
# one-minute load falls under LOAD (default 12).
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd)
A=$R/docs/research/design-x-acceptance-2026-11-22/tools
CAP=$1; shift
parts=${@:-stills switch film}
export BASE=/private/tmp/t0-qvm1c
freegb() { df -Pk /System/Volumes/Data | awk 'NR == 2 {print int($4 / 1048576)}'; }
quiet() {
  [ "$(freegb)" -lt 20 ] && { echo "$(date +%T) only $(freegb) GiB free: stop"; exit 2; }
  until awk -v l="${LOAD:-12}" '{exit !($1 < l)}' <(sysctl -n vm.loadavg | tr -d '{}'); do sleep 30; done
}
bash "$T/builds.sh" "$CAP" || exit 1
cd "$R"
for part in $parts; do
  case $part in
    stills)
      for view in lamp cabin bay cabinoff; do
        for build in pm pc; do
          compgen -G "$BASE/runs/$build-$view/dump/history-890-*" > /dev/null && continue
          quiet; echo "$(date +%T) still $build $view"
          nix develop --command python3 -I "$T/still.py" $build $view > "$BASE/runs/still-$build-$view.log" 2>&1 \
            || echo "$(date +%T) failed: still $build $view"
        done
      done ;;
    switch) for build in pm pc; do quiet; PROBE=$BASE/$build/probe-bin bash "$A/take.sh" switch-$build switch cabin; done ;;
    film) for build in pm pc; do quiet; PROBE=$BASE/$build/probe-bin bash "$A/take.sh" film-$build motion rect-bay; done ;;
  esac
done
echo "$(date +%T) queue done"
