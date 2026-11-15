#!/usr/bin/env bash
# measure.sh: prints every table of step 2, the controls and the strips check from the run folders in
# /private/tmp/t0-ion0/runs, through m.sh. Its output is tables.txt. Step 1's count is in tables-step1.txt
# (measure-1.sh). The raw captures are deleted after the record, so a section prints only while its runs exist.
M=$(dirname "$0")/m.sh
run() { echo "\$ $*"; "$M" "$@" 2>&1 | grep -v -i "warning\|sqlite"; echo; }
S=results/switch/cabin.json; F=results/motion/rect-bay.json
section() { echo "## $*"; echo; }
section "Controls"
echo "# c-T0-e: every new switch at its default, old store, must repeat the tolerance record's T0 (96.4 / 101.4)"
run light.py c-A-e c-A-e c-T0-e
echo "# The nearest-half store in scene light, no jitter, clamps off: c-sA-hn against c-sA (t8-69v7: 102.7 / 105.8),"
echo "# c-sc4-hn against c-sA-hn (t8-69v7: 100.0 / 100.1)"
run scenelight.py c-sA c-sA c-sA-hn
run scenelight.py c-sA-hn c-sA-hn c-sc4-hn
section "Lamp hall still at 4.08 stops, today's jitter: light against t-A-e, and the bounce history's lengths"
run light.py t-A-e t-A-e t-fix-e t-cap4-e t-Q0-e t-Q1-e t-Q3-e t-Q2-e
run lengths.py t-A-e t-fix-e t-cap4-e t-Q0-e t-Q1-e t-Q3-e t-Q2-e
section "Lamp hall still in scene light, no jitter, 4.08 stops: against the reference with the store (c-sA-hn)"
run scenelight.py c-sA-hn c-sA-hn sc-fix sc-cap4 sc-Q0 sc-Q1 sc-Q3 sc-Q2
section "Lamp hall still at the automatic exposure, today's jitter: specks, blotch, daub against t-A-s"
run stills.py rect-lamps t-A-s t-fix-s t-cap4-s t-Q0-s t-Q1-s t-Q3-s t-Q2-s
echo "\$ REF=t-A-s daub.py ..."; REF=t-A-s "$M" daub.py t-A-s:rect-lamps t-fix-s:rect-lamps t-cap4-s:rect-lamps t-Q0-s:rect-lamps t-Q1-s:rect-lamps t-Q3-s:rect-lamps t-Q2-s:rect-lamps 2>&1 | grep -v -i "warning\|sqlite"; echo
section "Cabin switch, today's jitter"
run switch.py s/t-A-sw/$S s/t-A-sw/$S s/t-fix-sw/$S s/t-cap4-sw/$S s/t-Q0-sw/$S s/t-Q1-sw/$S s/t-Q3-sw/$S s/t-Q2-sw/$S
run switchearly.py s/t-A-sw/$S s/t-A-sw/$S s/t-fix-sw/$S s/t-cap4-sw/$S s/t-Q0-sw/$S s/t-Q1-sw/$S s/t-Q3-sw/$S s/t-Q2-sw/$S
section "Film rect-bay, today's jitter: the survey's middle and last frame, and frames 10, 20 and 30 (filmearly.py)"
run films.py s/t-fix-rb/$F s/t-cap4-rb/$F s/t-Q0-rb/$F s/t-Q1-rb/$F s/t-Q3-rb/$F s/t-Q2-rb/$F
for v in fix cap4 Q0 Q1 Q3 Q2; do f=/private/tmp/t0-ion0/runs/t-$v-fe/early.log; [ -e $f ] && echo "t-$v-fe $(tail -1 $f)"; done; echo
section "Lag films, today's jitter: median lag in draws / median lag error"
run lags.py s/t-fix-lag s/t-cap4-lag s/t-Q0-lag s/t-Q1-lag s/t-Q3-lag s/t-Q2-lag
for j in n l; do
  section "Jitter $j (n none, l the long Halton sequence)"
  run light.py $j-A-e $j-A-e $j-fix-e $j-Q1-e $j-Q3-e $j-Q2-e
  run lengths.py $j-A-e $j-fix-e $j-Q1-e $j-Q3-e $j-Q2-e
  run stills.py rect-lamps $j-A-s $j-fix-s $j-Q1-s $j-Q3-s $j-Q2-s
  echo "\$ REF=$j-A-s daub.py ..."; REF=$j-A-s "$M" daub.py $j-A-s:rect-lamps $j-fix-s:rect-lamps $j-Q1-s:rect-lamps $j-Q3-s:rect-lamps $j-Q2-s:rect-lamps 2>&1 | grep -v -i "warning\|sqlite"; echo
  run switch.py s/$j-A-sw/$S s/$j-A-sw/$S s/$j-fix-sw/$S s/$j-Q1-sw/$S s/$j-Q3-sw/$S s/$j-Q2-sw/$S
  run switchearly.py s/$j-A-sw/$S s/$j-A-sw/$S s/$j-fix-sw/$S s/$j-Q1-sw/$S s/$j-Q3-sw/$S s/$j-Q2-sw/$S
done
section "The strips check"
echo "# Question 1: the lamps-on control of the cabin switch per jitter phase (strips.py), the reference and the cap of 4 draws"
run strips.py s/t-A-sw/$S s/n-A-sw/$S
run strips.py s/t-cap4-sw/$S s/n-cap4-sw/$S
echo "# Question 2: the lamp hall still with and without the sun (TERTIUS_NO_SUN=1), the reference and region, window 4"
for j in t n; do for sun in sun nosun; do
  run light.py s-$j-A-$sun-e s-$j-A-$sun-e s-$j-W4-$sun-e
  run scenelight.py ss-$j-A-$sun ss-$j-A-$sun ss-$j-W4-$sun
done; done
echo "# The cabin switch without the sun, today's jitter: the cap of 4 draws against the reference without the sun"
run switch.py s/s-t-A-nosun-sw/$S s/s-t-A-nosun-sw/$S s/s-t-cap4-nosun-sw/$S
run strips.py s/s-t-cap4-nosun-sw/$S s/s-t-cap4-nosun-sw/$S
