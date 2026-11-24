#!/bin/bash
# ter-fe63 stage 1: still takes of one case at 2/3 with the census off (20 captures, or CENSUS_CAPTURES), each line of
# the plan file "<out name> <case> <exposure or auto> [VAR=value...]". Usage: part.sh <plan file>
F=/private/tmp/t1-wl7c/fe63
while read -r name case exposure rest; do
  [ -z "$name" ] && continue
  (
    export CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-1
    [ "$exposure" != auto ] && export CENSUS_EXPOSURE=$exposure
    for kv in $rest; do export "$kv"; done
    python3 -I /private/tmp/t1-wl7c/tools/rayend/census.py /private/tmp/t1-wl7c/probe-head "$case" 0.6667 "$F/$name" < /dev/null
  )
done < "$1"
date +%H:%M
