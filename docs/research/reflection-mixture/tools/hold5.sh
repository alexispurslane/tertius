#!/bin/bash
# ter-fe63 stage 1, hold 5: the exact count at a share of 0.125: the bay's full picture (still and reference) and the
# nose's box with its census (draws 240 to 640, 65-byte records).
F=/private/tmp/t1-wl7c/fe63; C=/private/tmp/t1-wl7c/tools/rayend/census.py; P=/private/tmp/t1-wl7c/probe-head
export TERTIUS_PROBE_REFLECTION_SHARE=0.125 TERTIUS_PROBE_RAMP=0.1,0.38 TERTIUS_PROBE_EXACT_REFLECTION=1 CENSUS_KEEP_ALL=1
( export CENSUS_DRAWS=0-1 CENSUS_EXPOSURE=-4.089 TERTIUS_PROBE_CENSUS_KEEP_REFLECTION=1
  CENSUS_DRAWS=600-640 python3 -I $C $P bay-sunbeam 0.6667 $F/bay-exact125 "100,40,420,360;1300,100,1800,500" < /dev/null
  CENSUS_CAPTURES=100 python3 -I $C $P bay-sunbeam 0.6667 $F/bay-exact125-ref < /dev/null )
( export CENSUS_EXPOSURE=-1.7507 TERTIUS_PROBE_CENSUS_KEEP_REFLECTION=0
  python3 -I $C $P sunlit-nose 0.6667 $F/nose-exact125 "750,0,1170,330" < /dev/null )
( export CENSUS_DRAWS=0-1 CENSUS_EXPOSURE=3.83
  python3 -I $C $P rect-lamps-f62 0.6667 $F/lamps-exact125 < /dev/null
  CENSUS_CAPTURES=100 python3 -I $C $P rect-lamps-f62 0.6667 $F/lamps-exact125-ref < /dev/null )
date +%H:%M
