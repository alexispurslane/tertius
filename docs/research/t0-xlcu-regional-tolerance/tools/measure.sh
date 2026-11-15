#!/usr/bin/env bash
# measure.sh: prints every table of step 2 and the control from the run folders in /private/tmp/t0-xlcu/runs, through
# m.sh. Its output is tables.txt. Step 1's count is in tables-step1.txt (count.py). Its raw captures are deleted after the record.
M=$(dirname "$0")/m.sh
run() { echo "\$ $*"; "$M" "$@" 2>&1 | grep -v -i "warning\|sqlite"; echo; }
S=results/switch/cabin.json; F=results/motion/rect-bay.json
echo "# Control: lamp hall still at 4.08 stops, T0 through the old path (t-T0-e, n-T0-e), through the new path (t-T0path-e)"
run light.py t-A-e t-A-e t-T0-e t-T0path-e
echo "# Lamp hall still at 4.08 stops, today's jitter: light against t-A-e, and the history lengths"
run light.py t-A-e t-A-e t-fix-e t-T0-e t-T1-e t-T2-e
run lengths.py t-A-e t-fix-e t-T0-e t-T1-e t-T2-e
echo "# The same without jitter"
run light.py n-A-e n-A-e n-fix-e n-T0-e n-T1-e n-T2-e
run lengths.py n-A-e n-fix-e n-T0-e n-T1-e n-T2-e
echo "# The same under the long Halton sequence"
run light.py l-A-e l-A-e l-fix-e l-T0-e l-T1-e
run lengths.py l-A-e l-fix-e l-T0-e l-T1-e
echo "# Lamp hall still at the automatic exposure: specks and blotch, and the daub against the same jitter's reference"
run stills.py rect-lamps t-A-s t-fix-s t-T0-s t-T1-s t-T2-s n-A-s n-fix-s n-T0-s n-T1-s n-T2-s
for j in t n; do echo "\$ REF=$j-A-s daub.py ..."; REF=$j-A-s "$M" daub.py $j-A-s:rect-lamps $j-fix-s:rect-lamps $j-T0-s:rect-lamps $j-T1-s:rect-lamps $j-T2-s:rect-lamps 2>&1 | grep -v -i "warning\|sqlite"; echo; done
echo "# Cabin switch, today's jitter"
run switch.py s/t-A-sw/$S s/t-A-sw/$S s/t-fix-sw/$S s/t-T0-sw/$S s/t-T1-sw/$S s/t-T2-sw/$S s/t-cap4-sw/$S s/t-T0cap4-sw/$S
echo "# Cabin switch, no jitter"
run switch.py s/n-A-sw/$S s/n-A-sw/$S s/n-fix-sw/$S s/n-T0-sw/$S s/n-T1-sw/$S s/n-T2-sw/$S
echo "# Cabin switch, long Halton sequence"
run switch.py s/l-A-sw/$S s/l-A-sw/$S s/l-fix-sw/$S s/l-T0-sw/$S s/l-T1-sw/$S
echo "# Film rect-bay, today's jitter"
run films.py s/t-fix-rb/$F s/t-T0-rb/$F s/t-T1-rb/$F s/t-T2-rb/$F s/t-cap4-rb/$F
echo "# Lag film bay-sun, today's jitter"
run lags.py s/t-fix-lag s/t-T0-lag s/t-T1-lag s/t-T2-lag s/t-cap4-lag
