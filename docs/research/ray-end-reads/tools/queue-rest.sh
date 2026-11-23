#!/usr/bin/env bash
# Tool code of ter-pdw8: the census's stills through V0 to V3 and the second V0 run (counted draws 247 to 646), at
# render scales 2/3 and 0.9584, with the renderer's automatic exposure as in the census, every capture's PNG kept.
# bay-sunbeam first (the decision rule names it), then cabin-light-cabin, exit-cabin, sunlit-nose and rect-lamps.
# The keep rectangles are the census's: bay-sunbeam's sunlit patch and ter-fe63's box in sunlit-nose.
# Usage: bash queue-rest.sh <takes dir> [cases...]
set -u
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a-evidence/rayend
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a
T=$1; shift
cases=${*:-bay-sunbeam cabin-light-cabin exit-cabin sunlit-nose rect-lamps}
cd /private/tmp/ter-pdw8/build || exit 1
keep_of() { case $1 in bay-sunbeam) echo '100,40,420,360' ;; sunlit-nose) echo '750,0,1170,330' ;; *) echo '' ;; esac; }
take() { # take <case> <scale> <name> <variant> [draws]
  [ -f "$T/$3/census-result.json" ] && return
  [ "$(/bin/df -g / | awk 'NR==2{print $4}')" -lt 20 ] && { echo "disk under 20 GiB: stop"; exit 2; }
  TERTIUS_PROBE_RAY_END=$4 CENSUS_DRAWS=${5:-240-640} CENSUS_KEEP_ALL=1 \
    nix develop $W --command python3 -I $R/census.py /private/tmp/ter-pdw8/build "$1" "$2" "$T/$3" "$(keep_of $1)" \
    2>&1 | tail -1
  [ -n "$(keep_of $1)" ] && nix develop $W --command python3 -I $R/lamp.py "$T/$3" > "$T/$3.lamp.txt" 2>&1
  nix develop $W --command python3 -I $R/analyze.py "$T/$3" $R/regions.json "$1" > "$T/$3.analyze.txt" 2>&1
  # The results go to the evidence folder; then the raw sums go (the PNGs stay for pics.py).
  mkdir -p "$R/results/$3" && cp "$T/$3"/*.json "$T/$3"/series.txt "$R/results/$3/" && cp "$T/$3".*.txt "$R/results/" &&
    rm -f -- "$T/$3"/census.sums "$T/$3"/census.slow "$T/$3"/census.keep*
  echo "$3 done $(date +%H:%M)"
}
for c in $cases; do
  for s in 067 096; do
    scale=$([ $s = 067 ] && echo 0.6667 || echo 0.9584)
    for v in 0 0b 1 2 3; do
      case $v in 0b) take $c $scale $c-$s-v0b 0 247-647 ;; *) take $c $scale $c-$s-v$v $v ;; esac
    done
  done
  echo "$c all done"
done
echo queue done
