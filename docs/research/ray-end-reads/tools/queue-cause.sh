#!/usr/bin/env bash
# Tool code of ter-pdw8: the cause takes on the lamp hall (rect-lamps-f62) with the second probe step (apply-rayend2.py).
# Exposure 4.08 stops as the speck films, every capture kept, keep rectangles: 0 the ceiling around the near lamps, 1 the
# floor at the foot of the far wall (the flecks). Names c<width>-<variant>:
#   v0 (0), v1 = stand-in (c) (1), v2 (2), v3 = (c) + V2 (3), v2c = V2 over the children that hold light (10),
#   v3c = (c) + v2c (11), v1old50 = the old stand-in (4) settled over 50 captures, counted draws 1240 to 1640 (test (b)).
# Each take's results go to the evidence folder, and then its raw sums go.
# Usage: bash queue-cause.sh <takes dir> [name:variant:scale[:captures:draws] ...]
set -u
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a-evidence/rayend
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a
T=$1; shift
jobs=${*:-c1072-v0:0:0.558334 c1072-v1:1:0.558334 c1072-v2:2:0.558334 c1072-v2c:10:0.558334 c1072-v3:3:0.558334 c1072-v3c:11:0.558334 c1072-v1old50:4:0.558334:50:1240-1640 c1040-v1:1:0.541668 c1040-v2c:10:0.541668 c1040-v3c:11:0.541668}
cd /private/tmp/ter-pdw8/build || exit 1
for job in $jobs; do
  IFS=: read -r name variant scale captures draws <<< "$job"
  [ -f "$T/$name/census-result.json" ] && continue
  [ "$(/bin/df -g / | awk 'NR==2{print $4}')" -lt 20 ] && { echo "disk under 20 GiB: stop"; exit 2; }
  TERTIUS_PROBE_RAY_END=$variant CENSUS_EXPOSURE=4.08 CENSUS_KEEP_ALL=1 CENSUS_CAPTURES=${captures:-20} \
    CENSUS_DRAWS=${draws:-240-640} \
    nix develop $W --command python3 -I $R/census.py /private/tmp/ter-pdw8/build rect-lamps-f62 "$scale" "$T/$name" \
    '643,200,1043,400;885,577,1040,594' 2>&1 | tail -1
  nix develop $W --command python3 -I $R/lamp.py "$T/$name" > "$T/$name.lamp.txt" 2>&1
  nix develop $W --command python3 -I $R/analyze.py "$T/$name" $R/regions.json rect-lamps-f62 > "$T/$name.analyze.txt" 2>&1
  nix develop $W --command python3 -I $R/sill.py "$T/$name" 1 > "$T/$name.sill.json" 2>&1
  nix develop $W --command python3 -I $R/levels.py "$T/$name" $R/regions.json rect-lamps-f62 > "$T/$name.levels.json" 2>&1
  mkdir -p "$R/results/$name" && cp "$T/$name"/*.json "$T/$name"/series.txt "$R/results/$name/" &&
    cp "$T/$name".*.txt "$T/$name".*.json "$R/results/" &&
    rm -f -- "$T/$name"/census.sums "$T/$name"/census.slow "$T/$name"/census.keep* "$T/$name"/census.hits* \
      "$T/$name"/census.levels
  echo "$name done $(date +%H:%M)"
done
echo queue done
