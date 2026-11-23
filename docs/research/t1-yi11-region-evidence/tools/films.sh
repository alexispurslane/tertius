#!/usr/bin/env bash
# Tool code of t1-yi11. films.sh <film> <column>...: yifilm.py's film (rect-bay, switch or switch-film) for each column, with the
# column's build and knobs. The columns: a = ya, no knob; b = ya, cap 128; c = yc, cap 128; d = ya, cap 128 and region
# evidence off; e = ya, cap 64 and region evidence off. Run it with bash from the checkout's root: zsh does not split the knobs.
set -uo pipefail
Y=$(cd "$(dirname "$0")" && pwd); film=$1; shift
declare -A build=([a]=ya [b]=ya [c]=yc [d]=ya [e]=ya)
declare -A knobs=([a]="" [b]="TERTIUS_X_DIFFUSE_CAP=128" [c]="TERTIUS_X_DIFFUSE_CAP=128"
                  [d]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_OFF=1" [e]="TERTIUS_X_DIFFUSE_CAP=64 TERTIUS_X_REGION_OFF=1")
for col in "$@"; do
  prefix=rb; [ "$film" = switch ] && prefix=sw; [ "$film" = switch-film ] && prefix=swf
  echo "$(date +%T) $film $col: ${build[$col]} ${knobs[$col]}, load $(sysctl -n vm.loadavg | awk '{print $2}')"
  # shellcheck disable=SC2086
  nix develop --command python3 -I "$Y/yifilm.py" "${build[$col]}" "$prefix-$col" "$film" ${knobs[$col]} 2>&1 | grep -v SQLite | tail -1 | cut -c1-400
done
