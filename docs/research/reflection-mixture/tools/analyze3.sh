#!/bin/bash
# ter-fe63 stage 1, part 3's analysis: rect-lamps' floor (f62) against each build's own 100-capture reference, in units
# of main's mean; the plate against each build's own mean of its 20 captures (no reference take); region means against
# main; 1:1 crops.
set -e
G=/private/tmp/t1-wl7c/guide; F=/private/tmp/t1-wl7c/fe63; E=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/fe63
for b in main coin exact; do mkdir -p $F/daub-lamps-$b; python3 -I $G/reference.py $F/lamps-$b-ref $F/daub-lamps-$b > /dev/null; done
python3 -I $G/parts.py $F/lamps-boxes.json $F/daub-lamps-main/reference-mean.npy \
  "main=$F/lamps-main/c-s19.png:$F/daub-lamps-main/reference-mean.npy" "coin=$F/lamps-coin/c-s19.png:$F/daub-lamps-coin/reference-mean.npy" \
  "exact=$F/lamps-exact/c-s19.png:$F/daub-lamps-exact/reference-mean.npy" > $F/parts-lamps.json
python3 -I $G/means.py $F/lamps-boxes.json $F/lamps-main $F/lamps-coin $F/lamps-exact > $F/means-lamps.json
python3 -I $G/means.py $F/plate-boxes.json $F/plate-main $F/plate-coin $F/plate-exact > $F/means-plate.json
python3 -I $F/sheet.py $F/lamps-boxes.json $E/lamps-floor-main-coin-exact.png "main|3.83 stops, capture 19=$F/lamps-main" "mixture, coin, 0.25=$F/lamps-coin" "mixture, exact count, 0.25=$F/lamps-exact"
python3 -I $F/sheet.py $F/plate-boxes.json $E/plate-main-coin-exact.png "main|0.73 stops, capture 19=$F/plate-main" "mixture, coin, 0.25|(plate at 0.05: share 0)=$F/plate-coin" "mixture, exact count, 0.25=$F/plate-exact"
echo done
