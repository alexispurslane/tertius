#!/usr/bin/env bash
# Tool code of t1-yi11. films.sh <film> <column>...: yifilm.py's film (rect-bay, rect-bay-settled, switch or switch-film) for each column, with the
# column's build and knobs. The columns: a = ya, no knob; b = ya, cap 128; c = yc, cap 128; d = ya, cap 128 and region
# evidence off; e = ya, cap 64 and region evidence off. The probe's columns of build yg (scratch-yg.patch), at cap 128,
# each with the ring log in its film's folder: g = the cause gate of 128 draws, r = the signed rank test, gr = both (gate
# 36), g128r = both with a gate of 128 draws (the selected rule), bl = today's test with the log (draws what b draws). TURN=<degrees> runs the probe's columns with build yh and its
# fixed turn of the camera (TERTIUS_X_YAW_DEGREES), into folders <prefix>t-<column>. KEEP=none keeps no frame of the switch
# film's switch run. Run it with bash from the checkout's root: zsh does not split the knobs.
set -uo pipefail
Y=$(cd "$(dirname "$0")" && pwd); film=$1; shift
declare -A build=([a]=ya [b]=ya [c]=yc [d]=ya [e]=ya [g]=yg [r]=yg [gr]=yg [g128r]=yg [bl]=yg)
declare -A knobs=([a]="" [b]="TERTIUS_X_DIFFUSE_CAP=128" [c]="TERTIUS_X_DIFFUSE_CAP=128"
                  [d]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_OFF=1" [e]="TERTIUS_X_DIFFUSE_CAP=64 TERTIUS_X_REGION_OFF=1"
                  [g]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128" [r]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_RANK=1"
                  [gr]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=36 TERTIUS_X_REGION_RANK=1"
                  [g128r]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_RANK=1" [bl]="TERTIUS_X_DIFFUSE_CAP=128")
for col in "$@"; do
  prefix=rb; [ "$film" = switch ] && prefix=sw; [ "$film" = switch-film ] && prefix=swf
  [ "$film" = rect-bay-settled ] && prefix=rbs
  echo "$(date +%T) $film $col: ${build[$col]} ${knobs[$col]}, load $(sysctl -n vm.loadavg | awk '{print $2}')"
  # shellcheck disable=SC2086
  b=${build[$col]}; folder=$prefix-$col; turn=""
  [ -n "${TURN:-}" ] && { b=yh; folder=${prefix}t-$col; turn="TERTIUS_X_YAW_DEGREES=$TURN"; }
  log=""; [ "$b" != ya ] && [ "$b" != yc ] && log="TERTIUS_X_REGION_LOG=/private/tmp/t1-yi11/films/$folder"
  extra=""; [ "$film" = switch ] && [ -n "${KEEP:-}" ] && extra="KEEP=$KEEP"
  nix develop --command python3 -I "$Y/yifilm.py" "$b" "$folder" "$film" ${knobs[$col]} $turn $log $extra 2>&1 | grep -v SQLite | tail -1 | cut -c1-400
done
