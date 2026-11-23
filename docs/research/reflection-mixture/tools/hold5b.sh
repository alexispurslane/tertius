#!/bin/bash
# ter-fe63 stage 1, hold 5b: short census keeps (145-byte records with the cause record, draws 600 to 620) of the
# reflection kind's samples under the exact count at 0.25 and at 0.125, for the slope of the variance in the share:
# the nose's box; the bay's sunlit patch, right wall and floor; rect-lamps' floor.
F=/private/tmp/t1-wl7c/fe63; C=/private/tmp/t1-wl7c/tools/rayend/census.py; P=/private/tmp/t1-wl7c/probe-head
export CENSUS_DRAWS=600-620 TERTIUS_PROBE_RAMP=0.1,0.38 TERTIUS_PROBE_EXACT_REFLECTION=1
for s in 0.25 0.125; do
  export TERTIUS_PROBE_REFLECTION_SHARE=$s
  CENSUS_EXPOSURE=-1.7507 python3 -I $C $P sunlit-nose 0.6667 $F/w-nose-$s "750,0,1170,330" < /dev/null
  CENSUS_EXPOSURE=-4.089 python3 -I $C $P bay-sunbeam 0.6667 $F/w-bay-$s "100,40,420,360;1300,100,1800,500;600,650,1200,950" < /dev/null
  CENSUS_EXPOSURE=3.83 python3 -I $C $P rect-lamps-f62 0.6667 $F/w-lamps-$s "600,620,1320,1000" < /dev/null
done
date +%H:%M
