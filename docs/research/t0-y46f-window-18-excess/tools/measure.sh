#!/usr/bin/env bash
# measure.sh: prints every table of the README from the run folders in /private/tmp/t0-y46f/runs. Its output is tables.txt.
M=/private/tmp/t0-y46f/m.sh
run() { echo "\$ $*"; "$M" "$@" 2>&1 | grep -v warning; echo; }
echo "# Lamp hall, no jitter, 4.08 stops: light against A"
run light.py n-A n-A n-B n-C n-C2 n-C3 n-C4 n-D n-E n-fix n-F n-Acap n-C-noclamp n-D-t20 n-D-t40 n-D-t80 n-Cw n-Dw n-cap4 n-cap4-nc n-cap8-nc n-cap16-nc
echo "# Lamp hall, no jitter: history lengths"
run lengths.py n-A n-A-len n-B n-C-len2 n-D n-E n-fix n-F n-Acap n-C-noclamp n-D-t20 n-D-t40 n-D-t80 n-Cw n-Dw n-cap4 n-cap4-nc
echo "# Lamp hall, no jitter: per signal (2 bounce, 3 reflections), against A of the same signal"
run light.py n-A-bounce n-A-bounce n-C-bounce n-cap4-nc-bounce
run light.py n-A-refl n-A-refl n-C-refl n-cap4-nc-refl
echo "# Lamp hall, no jitter: whole image without the cut to smooth pixels"
run whole.py n-A n-A n-B n-C n-D n-fix n-Cw n-cap4 n-cap4-nc n-cap8-nc n-cap16-nc
run whole.py n-A-bounce n-A-bounce n-C-bounce n-cap4-nc-bounce
run whole.py n-A-refl n-A-refl n-C-refl n-cap4-nc-refl
echo "# Lamp hall, no jitter: the diffuse filter off, against A with the filter off"
run light.py n-A-nof n-A-nof n-cap4-nc-nof
run light.py n-A n-A-nof n-cap4-nc-nof
run whole.py n-A-nof n-A-nof n-cap4-nc-nof
run whole.py n-A n-A-nof
echo "# Lamp hall, no jitter: long stills of 180 captures, long mean of draws 5,313 to 5,940"
run light.py n-A-long n-A n-A-long n-cap4-nc n-cap4-nc-long n-C n-C-long
run light.py n-A n-A-long n-cap4-nc-long n-C-long
run lengths.py n-A n-A-long n-cap4-nc-long n-C-long
echo "# Lamp hall, today's jitter, 4.08 stops: light against t-A"
run light.py t-A t-A t-B t-C t-D t-Cw t-cap4-nc
run lengths.py t-A t-A t-B t-C t-D t-Cw t-cap4-nc
echo "# Cabin exit-cabin, no jitter, 1.0 stops: light against c-A"
run light.py c-A c-A c-B c-C c-D c-Cw c-cap4-nc
run lengths.py c-A c-A c-B c-C c-D c-Cw c-cap4-nc
run whole.py c-A:exit-cabin c-A c-B c-C c-D c-Cw c-cap4-nc
echo "# Cabin exposure choice, A at 1, 2 and 3 stops"
run whole.py c-A-x1:exit-cabin c-A-x1 c-A-x2 c-A-x3
run codes.py exit-cabin c-A-x1 c-A-x2 c-A-x3
echo "# Lamp hall, no jitter: the sweep G and E with the corrected window"
run light.py n-A n-Cw n-Dw n-Dw-t20 n-Dw-t40 n-Dw-t80 n-Ew n-B
run lengths.py n-A n-Cw n-Dw n-Dw-t20 n-Dw-t40 n-Dw-t80 n-Ew
echo "# Lamp hall, no jitter, in scene light (scene.py): against s-A, and the filter-off pair against s-A-nof"
run scenelight.py s-A s-A s-C s-fix s-Cw s-cap4-nc s-A-nof s-cap4-nc-nof
run scenelight.py s-A-nof s-A-nof s-cap4-nc-nof
