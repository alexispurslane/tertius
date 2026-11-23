#!/bin/bash
# ter-fe63 stage 1, part 4's analysis: the lever (region evidence off, diffuse cap 128) alone and with the exact count at
# 0.25 and 0.125, beside main. The grain / blob table of capture 19 against each build's own reference in units of main's
# full mean, the 20-capture means against main's recorded means, and 1:1 crops of the bay's four regions.
set -e
G=/private/tmp/t1-wl7c/guide; F=/private/tmp/t1-wl7c/fe63; E=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/fe63
MAINSTILL=$G/stills/bay-0.25; MAINREF=$G/daub/reference-mean.npy
for b in lever lever-exact lever-exact125; do mkdir -p $F/daub-$b; python3 -I $G/reference.py $F/$b-ref $F/daub-$b > /dev/null; done
python3 -I $G/parts.py $G/daub/boxes.json $MAINREF "main=$MAINSTILL/c-s19.png:$MAINREF" \
  "lever=$F/lever/c-s19.png:$F/daub-lever/reference-mean.npy" "lever-exact=$F/lever-exact/c-s19.png:$F/daub-lever-exact/reference-mean.npy" \
  "lever-exact125=$F/lever-exact125/c-s19.png:$F/daub-lever-exact125/reference-mean.npy" \
  "exact125=$F/bay-exact125/c-s19.png:$F/daub-bay-exact125/reference-mean.npy" > $F/parts-lever.json
python3 -I $G/means.py $G/daub/boxes.json $F/lever $F/lever-exact $F/lever-exact125 > $F/means-lever.json
mkdir -p $E/lever4
BAYCOLS_ALL=1 python3 -I $F/baycols.py $E/lever4/bay "main|-4.089 stops, capture 19=$MAINSTILL" \
  "lever: region evidence off|cap 128, no mixture=$F/lever" "lever + exact count 0.25|with today's arrival=$F/lever-exact" \
  "lever + exact count 0.125|with today's arrival=$F/lever-exact125"
echo done
