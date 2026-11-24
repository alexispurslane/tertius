#!/bin/bash
# ter-fe63 stage 1, part 1's analysis: each build's independent reference (captures 30 to 99 of its 100-capture take),
# the grain / blob table of capture 19 against its own reference in units of main's full mean, the region means of the 20
# captures against main, and the 1:1 crops of the right wall and the sunlit patch. Usage: analyze1.sh <mode: full|spec>
set -e
G=/private/tmp/t1-wl7c/guide; F=/private/tmp/t1-wl7c/fe63; E=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/fe63
mode=$1; s=""; [ "$mode" = spec ] && s="-spec"
MAINSTILL=$G/stills/bay-0.25; MAINREF=$G/daub/reference-mean.npy
[ "$mode" = spec ] && { MAINSTILL=$G/stills/bay-specular-0.25; MAINREF=$G/daub-specular/reference-mean.npy; }
for b in coin exact thin; do mkdir -p $F/daub-bay$s-$b; python3 -I $G/reference.py $F/bay$s-$b-ref $F/daub-bay$s-$b > /dev/null; done
python3 -I $G/parts.py $G/daub/boxes.json $G/daub/reference-mean.npy "main=$MAINSTILL/c-s19.png:$MAINREF" \
  "coin=$F/bay$s-coin/c-s19.png:$F/daub-bay$s-coin/reference-mean.npy" "exact=$F/bay$s-exact/c-s19.png:$F/daub-bay$s-exact/reference-mean.npy" \
  "thin=$F/bay$s-thin/c-s19.png:$F/daub-bay$s-thin/reference-mean.npy" > $F/parts-bay-$mode.json
python3 -I $G/means.py $G/daub/boxes.json $MAINSTILL $F/bay$s-coin $F/bay$s-exact $F/bay$s-thin > $F/means-bay-$mode.json
python3 -I $F/baycols.py $E/bay-$mode "main|-4.089 stops, capture 19=$MAINSTILL" \
  "mixture, coin, 0.25|with today's arrival=$F/bay$s-coin" "mixture, exact count, 0.25|with today's arrival=$F/bay$s-exact" \
  "mixture, coin, thin box (scratch)|1.1 % dark bias on the patch=$F/bay$s-thin"
echo done
