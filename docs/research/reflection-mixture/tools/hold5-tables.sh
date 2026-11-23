#!/bin/bash
# ter-fe63 stage 1, hold 5's tables from the cause-record keeps: w^2 by draw kind, the causes of the guided draws, and the
# replay at six shares, at the take shares 0.25 and 0.125 (exact count).
F=/private/tmp/t1-wl7c/fe63; cd $F
for s in 0.25 0.125; do
  python3 -I wslope.py w-nose-$s nose > t5-wslope-nose-$s.json
  python3 -I wslope.py w-bay-$s patch right floor > t5-wslope-bay-$s.json
  python3 -I wslope.py w-lamps-$s floor > t5-wslope-lamps-$s.json
  python3 -I causes.py w-nose-$s nose > t5-causes-nose-$s.json
  python3 -I causes.py w-bay-$s patch right floor > t5-causes-bay-$s.json
  python3 -I causes.py w-lamps-$s floor > t5-causes-lamps-$s.json
  python3 -I wreplay.py w-nose-$s $s nose=280x220 > t5-replay-nose-$s.json
  python3 -I wreplay.py w-bay-$s $s patch=214x214 right=334x267 floor=400x200 > t5-replay-bay-$s.json
  python3 -I wreplay.py w-lamps-$s $s floor=480x253 > t5-replay-lamps-$s.json
done
python3 -I causes.py bay-exact125 patch right > t5-causes-bay-exact125-still.json
echo done
