#!/bin/bash
# ter-fe63 stage 1, hold 5's analysis: the exact count at 0.125 beside main, the coin and the exact count at 0.25. The bay's
# grain / blob table of capture 19 against each build's own reference in units of main's full mean, and 1:1 crops; the
# nose's specks and samples; rect-lamps' floor against each build's own reference, and its crop.
set -e
G=/private/tmp/t1-wl7c/guide; F=/private/tmp/t1-wl7c/fe63; E=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/fe63
MAINSTILL=$G/stills/bay-0.25; MAINREF=$G/daub/reference-mean.npy
for t in lamps; do mkdir -p $F/daub-$t-exact125; python3 -I $G/reference.py $F/$t-exact125-ref $F/daub-$t-exact125 > /dev/null; done
[ -f $F/parts-bay-125.json ] && true || python3 -I $G/parts.py $G/daub/boxes.json $G/daub/reference-mean.npy "main=$MAINSTILL/c-s19.png:$MAINREF" \
  "coin=$F/bay-coin/c-s19.png:$F/daub-bay-coin/reference-mean.npy" "exact=$F/bay-exact/c-s19.png:$F/daub-bay-exact/reference-mean.npy" \
  "exact125=$F/bay-exact125/c-s19.png:$F/daub-bay-exact125/reference-mean.npy" "thin=$F/bay-thin/c-s19.png:$F/daub-bay-thin/reference-mean.npy" > $F/parts-bay-125.json
# Main's captures 0 to 18 were deleted with the census sums; its 20-capture means are in means-bay-full.json.
python3 -I $G/means.py $G/daub/boxes.json $F/bay-exact125 > $F/means-bay-125.json
[ -f $E/bay-125-right-wall.png ] || python3 -I $F/baycols.py $E/bay-125 "main|-4.089 stops, capture 19=$MAINSTILL" "mixture, coin, 0.25|with today's arrival=$F/bay-coin" \
  "mixture, exact count, 0.25|with today's arrival=$F/bay-exact" "mixture, exact count, 0.125|with today's arrival=$F/bay-exact125"
[ -f $E/nose-box-1x-125.png ] || NOSE_BOUND=3.8886351585388184 python3 -I $F/nose.py 750,0,1170,330 $E/nose-box-1x-125.png main=$F/nose-main coin-0.25=$F/nose-mix exact-0.25=$F/nose-exact exact-0.125=$F/nose-exact125 > $F/nose-125.json
python3 -I $G/parts.py $F/lamps-boxes.json $F/daub-lamps-main/reference-mean.npy \
  "main=$F/lamps-main/c-s19.png:$F/daub-lamps-main/reference-mean.npy" "coin=$F/lamps-coin/c-s19.png:$F/daub-lamps-coin/reference-mean.npy" \
  "exact=$F/lamps-exact/c-s19.png:$F/daub-lamps-exact/reference-mean.npy" "exact125=$F/lamps-exact125/c-s19.png:$F/daub-lamps-exact125/reference-mean.npy" > $F/parts-lamps-125.json
python3 -I $G/means.py $F/lamps-boxes.json $F/lamps-exact125 > $F/means-lamps-125.json
python3 -I $F/sheet.py $F/lamps-boxes.json $E/lamps-floor-125.png "main|3.83 stops, capture 19=$F/lamps-main" "mixture, coin, 0.25=$F/lamps-coin" "mixture, exact count, 0.25=$F/lamps-exact" "mixture, exact count, 0.125=$F/lamps-exact125"
echo done
