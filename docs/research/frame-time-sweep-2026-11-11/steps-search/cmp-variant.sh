#!/usr/bin/env bash
# cmp-variant.sh <prefix> <control suffix> <variant suffix>: the pixel comparison (capdiff2.py) and the project's FLIP
# of every still of a variant against the control, into refl/cmp/<prefix><variant>.{capdiff,flip}.txt. Dev shell.
S=/private/tmp/tertius-sweep-20261111
mkdir -p $S/refl/cmp/heat-$1$3
python3 -I $S/tools/capdiff2.py $S/refl/out3 $1 $2 $3 > $S/refl/cmp/$1$3.capdiff.txt 2>&1
python3 -I $S/tools/fliplist.py $S/refl/out3 $1 $2 $3 > $S/refl/cmp/$1$3.list.tsv
$S/tools/flipcmp/bin-out/flipcmp $S/refl/cmp/$1$3.list.tsv $S/refl/cmp/heat-$1$3 > $S/refl/cmp/$1$3.flip.txt 2>&1
echo "$1$3: $(grep -c . $S/refl/cmp/$1$3.list.tsv) pairs"
