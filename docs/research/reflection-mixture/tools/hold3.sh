#!/bin/bash
# ter-fe63 stage 1, hold 3: the replay's logs again (share 0, draws 240 to 280, reflection fields), and the bay's
# clamp-off pair (main and the exact count at 0.25, writer bit 8).
F=/private/tmp/t1-wl7c/fe63; C=/private/tmp/t1-wl7c/tools/rayend/census.py; P=/private/tmp/t1-wl7c/probe-head
( export CENSUS_CAPTURES=10 CENSUS_DRAWS=240-280
  CENSUS_EXPOSURE=-1.7507 python3 -I $C $P sunlit-nose 0.6667 $F/keeps-sunlit-nose "750,0,1170,330" < /dev/null
  CENSUS_EXPOSURE=-4.089 python3 -I $C $P bay-sunbeam 0.6667 $F/keeps-bay-sunbeam "100,40,420,360;1300,100,1800,500" < /dev/null
  python3 -I $C $P rect-lamps-f62 0.6667 $F/keeps-rect-lamps-f62 "600,620,1320,1000" < /dev/null
  python3 -I $C $P mirror-plate 0.6667 $F/keeps-mirror-plate "800,350,1150,700" < /dev/null )
# The clamp-off takes in the specular signal alone, where the mixture's means moved most (right wall +9 %, thin box +18 %).
( export CENSUS_KEEP_ALL=1 CENSUS_DRAWS=0-1 CENSUS_EXPOSURE=-4.089 TERTIUS_PROBE_WRITERS_OFF=8 TERTIUS_PROBE_SIGNAL=3
  python3 -I $C $P bay-sunbeam 0.6667 $F/bay-noclamp-main < /dev/null
  TERTIUS_PROBE_REFLECTION_SHARE=0.25 TERTIUS_PROBE_RAMP=0.1,0.38 TERTIUS_PROBE_EXACT_REFLECTION=1 python3 -I $C $P bay-sunbeam 0.6667 $F/bay-noclamp-exact < /dev/null
  TERTIUS_PROBE_REFLECTION_SHARE=0.25 TERTIUS_PROBE_RAMP=0.1,0.38 TERTIUS_PROBE_THIN_BOX=1 python3 -I $C $P bay-sunbeam 0.6667 $F/bay-noclamp-thin < /dev/null )
# The fast history and the clamp's moves on the bay's sunlit patch: the specular signal's accumulation trace (layers 3
# and 6) over draws 600 to 615, for main, the coin and the exact count at 0.25, and the exact count at 0.125.
( export CENSUS_DRAWS=600-640 CENSUS_EXPOSURE=-4.089 TERTIUS_PROBE_TRACE_DUMP=600-616 TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace
  python3 -I $C $P bay-sunbeam 0.6667 $F/trace-main < /dev/null
  TERTIUS_PROBE_REFLECTION_SHARE=0.25 TERTIUS_PROBE_RAMP=0.1,0.38 python3 -I $C $P bay-sunbeam 0.6667 $F/trace-coin < /dev/null
  TERTIUS_PROBE_REFLECTION_SHARE=0.25 TERTIUS_PROBE_RAMP=0.1,0.38 TERTIUS_PROBE_EXACT_REFLECTION=1 python3 -I $C $P bay-sunbeam 0.6667 $F/trace-exact < /dev/null
  TERTIUS_PROBE_REFLECTION_SHARE=0.125 TERTIUS_PROBE_RAMP=0.1,0.38 TERTIUS_PROBE_EXACT_REFLECTION=1 python3 -I $C $P bay-sunbeam 0.6667 $F/trace-exact125 < /dev/null )
date +%H:%M
