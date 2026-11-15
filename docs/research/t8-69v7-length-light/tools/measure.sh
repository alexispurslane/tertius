#!/usr/bin/env bash
# measure.sh: prints every table of the README from the run folders in /private/tmp/t8-69v7/runs. Its output is tables.txt.
M=/private/tmp/t8-69v7/m.sh
run() { echo "\$ $*"; "$M" "$@"; echo; }
R=/private/tmp/t8-69v7/runs
echo "# Control runs: the second build at its defaults against the first (equal long means)"
for p in "a a2" "c4 c4-2" "a a-plain"; do set -- $p; cmp -s $R/$1/scene/rect-lamps-long.npy $R/$2/scene/rect-lamps-long.npy && echo "$2 equals $1" || echo "$2 differs from $1"; done; echo
echo "# Light in scene light, each against the same variant with no cap (32 draws)"
run scenelight.py a a c4 c8 c16
run scenelight.py a-nof a-nof c4-nof c8-nof c16-nof
run scenelight.py a-sb a-sb c4-sb
run scenelight.py a-nof-sb a-nof-sb c4-nof-sb
run scenelight.py a-hn a-hn c4-hn c8-hn c16-hn
run scenelight.py a-nof-hn a-nof-hn c4-nof-hn
run scenelight.py a-nat a-nat c4-nat
run scenelight.py a-nof-nat a-nof-nat c4-nof-nat c8-nof-nat c16-nof-nat
run scenelight.py a-nof-nat-hn a-nof-nat-hn c4-nof-nat-hn
run scenelight.py a-nof-b a-nof-b c4-nof-b
run scenelight.py a-nof-r a-nof-r c4-nof-r
echo "# What the nearest-half store and FSR change in the reference itself"
run scenelight.py a a-hn a-nat
run scenelight.py a-nof a-nof-hn
run scenelight.py a-nof-nat a-nof-nat-hn
echo "# Stage means of the diffuse signal, each against the same variant with no cap"
run stages.py a a c4 c8 c16
run stages.py a-sb a-sb c4-sb
run stages.py a-nof a-nof c4-nof
run stages.py a-hn a-hn c4-hn c8-hn c16-hn
echo "# Each variant's slow history and shown light against its own samples"
run own.py a c4 c8 c16 a-sb c4-sb a-nof c4-nof a-nof-sb c4-nof-sb a-hn c4-hn c8-hn c16-hn a-nof-hn c4-nof-hn a-nat c4-nat a-nof-nat c4-nof-nat a-nof-nat-hn c4-nof-nat-hn
