#!/usr/bin/env bash
# measure.sh: prints the image tables of t0-xdck from the run folders in /private/tmp/t0-xdck/runs, through m.sh. Its
# output is tables-images.txt. The frames are deleted after the record, so a section prints only while its
# runs exist. The timing tables come from timing-table.py (tables-timing.txt).
M=$(dirname "$0")/m.sh; D=$(dirname "$0")
run() { echo "\$ $*"; "$M" "$@" 2>&1 | grep -v -i "warning\|sqlite"; echo; }
F=results/motion/rect-bay.json
echo "## Film rect-bay, today's jitter: the survey's middle and last frame"; echo
run films.py s/t-M-rb/$F s/t-X-rb/$F s/t-D4-rb/$F s/t-D8-rb/$F s/t-D4s-rb/$F s/t-C4-rb/$F s/t-D4c-rb/$F s/t-D8c-rb/$F
run filmspecks.py t-M-rb t-X-rb t-D4-rb t-D8-rb t-D4s-rb t-C4-rb t-D4c-rb t-D8c-rb
echo "\$ filmflicker.py ..."; python3 -I $D/filmflicker.py t-M-rb t-X-rb t-D4-rb t-D8-rb t-D4s-rb t-C4-rb t-D4c-rb t-D8c-rb; echo
echo "## Own rays per film frame (M has no count: main's build has no probe)"; echo
echo "\$ ownshare.py ..."; python3 -I $D/ownshare.py t-X-rb t-Xt-rb t-D4-rb t-D8-rb t-D4s-rb t-C4-rb t-D4c-rb t-D8c-rb; echo
echo "## Lag film bay-sun: median lag in draws / median lag error"; echo
run lags.py s/t-M-lag s/t-X-lag s/t-D4-lag s/t-D8-lag s/t-D4s-lag s/t-C4-lag
echo "## Still bay-sunbeam at the automatic exposure: specks, blotch, daub against t-A-s"; echo
run stills.py bay-sunbeam t-A-s t-M-s t-X-s t-D4-s t-D8-s t-D4s-s
echo "\$ REF=t-A-s daub.py ..."; REF=t-A-s "$M" daub.py t-A-s:bay-sunbeam t-M-s:bay-sunbeam t-X-s:bay-sunbeam t-D4-s:bay-sunbeam t-D8-s:bay-sunbeam t-D4s-s:bay-sunbeam 2>&1 | grep -v -i "warning\|sqlite"; echo
