#!/usr/bin/env bash
# Tool code of ter-pdw8: the lamp hall at frame 62 of scripts/rect-lamps.json, at 1072 x 603 (bursts) and 1040 x 585
# (clean), through the variants V0 to V3 and a second V0 run whose counted draws are 247 to 646 (v0b), at the speck
# films' fixed exposure of 4.08 stops, every capture's PNG kept, the ceiling around the near lamps as the keep
# rectangle. lamp.py and analyze.py run after each take. Usage: bash queue-lamp.sh <takes dir>
set -u
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a-evidence/rayend
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a
T=$1
cd /private/tmp/ter-pdw8/build || exit 1
take() { # take <scale> <name> <variant> [draws]
  [ -f "$T/$2/lamp-result.json" ] && return
  [ "$(/bin/df -g / | awk 'NR==2{print $4}')" -lt 20 ] && { echo "disk under 20 GiB: stop"; exit 2; }
  TERTIUS_PROBE_RAY_END=$3 CENSUS_DRAWS=${4:-240-640} CENSUS_EXPOSURE=4.08 CENSUS_KEEP_ALL=1 \
    nix develop $W --command python3 -I $R/census.py /private/tmp/ter-pdw8/build rect-lamps-f62 "$1" "$T/$2" \
    '643,200,1043,400' 2>&1 | tail -1
  nix develop $W --command python3 -I $R/lamp.py "$T/$2" > "$T/$2.lamp.txt" 2>&1
  nix develop $W --command python3 -I $R/analyze.py "$T/$2" $R/regions.json rect-lamps-f62 > "$T/$2.analyze.txt" 2>&1
  echo "$2 done $(date +%H:%M)"
}
for v in 0b 1 2 3; do
  case $v in 0b) take 0.558334 lamp1072-v0b 0 247-647 ;; *) take 0.558334 lamp1072-v$v $v ;; esac
done
for v in 0 1 2 3 0b; do
  case $v in 0b) take 0.541668 lamp1040-v0b 0 247-647 ;; *) take 0.541668 lamp1040-v$v $v ;; esac
done
echo queue done
