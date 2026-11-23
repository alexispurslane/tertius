#!/usr/bin/env bash
# Tool code of ter-pdw8: the surface share takes (apply-rayend5.py), one take's raw data on disk at a time.
# A job is name:case:variant:scale:exposure:keeps:hits:dump, with
#   variant  0 V0, 80 S1 (with the guide reading the same value), 144 S2 (likewise)
#   exposure fixed stops, or "auto"
#   keeps    the keep rectangles in output px, "x0,y0,x1,y1;..." or "-"
#   hits     1 records the kept samples' hit pixels (needed for the wall foot's hit rows), else 0
#   dump     draws to dump ("630,631") or "-"
# The group's V0 must come first: its name ends in -v0, and each later take of the group is measured against it.
# After each take: lamp.py (with a keep), analyze.py, sill.py (with hits), premise.py (with a dump at 630 and 631),
# pics.py against the group's V0 (the variant as "v1" beside V0), the results into the evidence folder, and then the
# take's raw sums, kept samples, hits, dumps and capture frames are deleted; the group's V0 keeps captures c-s08 to c-s19
# for the later takes' pictures.
# Usage: bash queue-share.sh <takes dir> <crops json per case: case=path,...> <job> ...
set -u
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a-evidence/rayend
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a
T=$1; shift
CROPS=$1; shift
crops_of() { echo "$CROPS" | tr ',' '\n' | awk -F= -v c="$1" '$1 == c {print $2}'; }
cd /private/tmp/ter-pdw8/build || exit 1
for job in "$@"; do
  IFS=: read -r name case variant scale exposure keeps hits dump <<< "$job"
  group=${name%-*}
  if [ ! -f "$T/$name/census-result.json" ]; then
    free=$(/bin/df -g / | awk 'NR==2{print $4}')
    [ "$free" -lt 20 ] && { echo "disk $free GiB, under 20: stop"; exit 2; }
    [ "$free" -lt 22 ] && { echo "disk $free GiB, under 22: no take starts"; exit 3; }
    env TERTIUS_PROBE_RAY_END="$variant" CENSUS_KEEP_ALL=1 \
      $([ "$exposure" != auto ] && echo "CENSUS_EXPOSURE=$exposure") \
      $([ "$hits" = 1 ] && echo TERTIUS_PROBE_CENSUS_HITS=1) \
      $([ "$dump" != - ] && echo "TERTIUS_PROBE_CENSUS_DUMP_AT=$dump") \
      nix develop $W --command python3 -I $R/census.py /private/tmp/ter-pdw8/build "$case" "$scale" "$T/$name" \
      "$([ "$keeps" != - ] && echo "$keeps")" 2>&1 | tail -1
    echo "$name weighs $(du -sh "$T/$name" | cut -f1) before the cleanup"
  fi
  [ "$keeps" != - ] && nix develop $W --command python3 -I $R/lamp.py "$T/$name" > "$T/$name.lamp.txt" 2>&1
  nix develop $W --command python3 -I $R/analyze.py "$T/$name" $R/regions.json "$case" > "$T/$name.analyze.txt" 2>&1
  [ "$hits" = 1 ] && nix develop $W --command python3 -I $R/sill.py "$T/$name" 1 > "$T/$name.sill.json" 2>&1
  if [ "$dump" = 630,631 ]; then
    w=$(python3 -c "import json;print(json.load(open('$T/$name/census.json'))['Viewport'][0])")
    win=$([ "$w" = 1072 ] && echo 480,300,600,330 || echo 465,290,585,320)
    nix develop $W --command python3 -I $R/premise.py "$T/$name" 631 630 "$win" > "$T/$name.premise.json" 2>&1
  fi
  if [ "$name" != "$group-v0" ]; then
    ln -sfn "$group-v0" "$T/$name-pv-v0"; ln -sfn "$name" "$T/$name-pv-v1"
    nix develop $W --command python3 -I $R/pics.py "$T" "$name-pv" "$case" $R/regions.json "$(crops_of "$case")" $R/pics \
      > "$T/$name.pictures.txt" 2>&1
    rm -f -- "${T:?}/${name:?}-pv-v0" "${T:?}/${name:?}-pv-v1"
  fi
  mkdir -p "$R/results/$name" && cp "$T/$name"/*.json "$T/$name"/series.txt "$R/results/$name/" &&
    cp "$T/$name".*.txt "$R/results/" && { cp "$T/$name".*.json "$R/results/" 2>/dev/null; true; } &&
    rm -f -- "${T:?}/${name:?}"/census.sums "${T:?}/${name:?}"/census.slow "${T:?}/${name:?}"/census.keep* \
      "${T:?}/${name:?}"/census.hits* "${T:?}/${name:?}"/census.levels "${T:?}/${name:?}"/census.d*
  if [ "$name" = "$group-v0" ]; then
    rm -f -- "${T:?}/${name:?}"/c-s0[0-7].png "${T:?}/${name:?}"/map-*.png
  else
    rm -f -- "${T:?}/${name:?}"/c-s*.png "${T:?}/${name:?}"/map-*.png
  fi
  echo "$name done $(date +%H:%M), $(du -sh "$T/$name" | cut -f1) kept, disk $(/bin/df -g / | awk 'NR==2{print $4}') GiB free"
done
echo queue done
