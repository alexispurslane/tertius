#!/usr/bin/env bash
# Tool code of t0-gbgj. measure.sh: every measure of the record from the runs in /private/tmp/t0-gbgj/runs, through m.sh,
# in the order of README.md's tables, and the pictures. Its output is tables.txt.
# measure.sh [all | 2]: "2" starts at take 2b. The sections before it read the film frames that film.py kept, and those
# frames were deleted after their videos and numbers were committed. tables.txt holds those sections' output from the run
# of 12:22 on 2026-11-22 and the output of "measure.sh 2" after them. The size ramps, the window test, the wobble and the
# edge ramp also read frames that were deleted; this script prints their saved outputs (the record's ramp-*.txt,
# window.txt, wobble.txt and edgeramp.txt) and redraws only the pictures whose inputs remain.
T=$(cd "$(dirname "$0")" && pwd); P=$(cd "$T/../pics" && pwd); . "$T/vars.sh"
# The acceptance lead's crop of the slit (t0-5tyj, in its worktree; not committed when this record was written).
EARLIER=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/pics/film-bay-sunbeam-slit.png
m() { echo "\$ $*"; bash "$T/m.sh" "$@" 2>&1 | grep -vE "^warning|^\s*$|sqlite"; echo; }
R=results/switch/cabin.json; F=results/motion/rect-bay.json; D=$(cd "$T/.." && pwd)
if [ "${1:-all}" = all ]; then
echo "## The survey's numbers"; m summary.py
echo "## Take 1: the stills, daub against A2's long reference of the same exposure"
REF=s-A2 m daub.py s-M2:rect-lamps s-X2:rect-lamps s-A2:rect-lamps
REF=e-A2 m daub.py e-M2:rect-lamps e-X2:rect-lamps e-A2:rect-lamps
for c in exit-cabin docking-cabin cabin-light-cabin deck-cuts bay-sunbeam; do REF=s-A2 m daub.py s-M2:$c s-X2:$c s-A2:$c; done
echo "## Take 1: the light of rect-lamps' ceiling and walls, as a share of A2's long reference"
m light.py s-A2 s-M2 s-X2 s-A2
m light.py e-A2 e-M2 e-X2 e-A2
echo "## Take 2: the switch cabin, M2's controls for both sides, then each side against its own"
m switch.py s/w-M2/$R s/w-M2/$R s/w-X2/$R
m switch.py s/w-X2/$R s/w-X2/$R
for v in w-M2 w-X2; do echo "== $v against its own controls"; m regions.py s/$v/$R s/$v/$R; done
echo "== w-X2 against M2's controls"; m regions.py s/w-X2/$R s/w-M2/$R
echo "## Take 3: the film rect-bay (level and blotch at the middle and last frame, specks) and the lag film bay-sun"
m films.py s/f-M2/$F s/f-X2/$F
m filmspecks.py f-M2 f-X2
m lags.py s/l-M2 s/l-X2
echo "## Take 3b: the film bay-sunbeam (added by the line orchestrator): level, blotch and specks at the middle and last frame"
m films.py s/fs-M2/results/motion/bay-sunbeam.json s/fs-X2/results/motion/bay-sunbeam.json
m filmspecks.py --case bay-sunbeam fs-M2 fs-X2
echo "## Take 3b: the slit's teeth (bug tc-6v3d): four frames in a row on M2 and X2, then frame 90 beside the acceptance record's"
m teeth.py $P/bay-sunbeam-slit-teeth.png 89 4 M2=fs-M2 X2=fs-X2
m layerteeth.py $P/bay-sunbeam-slit-layer.png "$EARLIER" 90 'old main c4c1f1f2f=tile:0' 'M2=fs-M2' 'Design X alone=tile:1' 'X2=fs-X2'
echo "## What the layer changed: M2 against old main and X2 against Design X alone (the acceptance lead's still takes)"
mkdir -p $W/w/s && ln -sfn /private/tmp/t0-5tyj/runs/s1-M $W/w/s/old-M && ln -sfn /private/tmp/t0-5tyj/runs/s1-X $W/w/s/old-X
for c in cabin-light-cabin exit-cabin docking-cabin deck-cuts; do
  m layerdiff.py $P/layerdiff-$c.png $c 0.4 'main: old main to M2=old-M:s-M2' 'Design X: X alone to X2=old-X:s-X2'
done
echo "## The pictures"
m sheet.py $P/rect-lamps-whole.png rect-lamps full 0.5 'M2=s-M2' 'X2=s-X2'
m sheet.py $P/rect-lamps-whole-408.png rect-lamps full 0.5 'M2, 4.08 stops=e-M2' 'X2, 4.08 stops=e-X2'
m sheet.py $P/rect-lamps-ceiling.png rect-lamps 640,0,1280,420 1 'reference A2=s-A2:reference' 'M2=s-M2' 'X2=s-X2'
for c in exit-cabin docking-cabin cabin-light-cabin deck-cuts bay-sunbeam; do
  m sheet.py $P/$c.png $c full 0.5 "M2=s-M2" "X2=s-X2"
done
m levelchart.py $P/switch-r11.png s/w-M2/$R M2=s/w-M2/$R X2=s/w-X2/$R
m filmsheet.py $P/rect-bay-frames.png rect-bay 0.4 "still M2=f-M2:still" "M2=f-M2" "still X2=f-X2:still" "X2=f-X2"
m filmcrop.py $P/rect-bay-end-floor.png end 700,600,1100,1000 "still M2=f-M2:still" "M2=f-M2" "X2=f-X2"
for l in f-bay-M:old-f-M f-bay-X:old-f-X f3-M:old-fs-M f3-X:old-fs-X; do ln -sfn /private/tmp/t0-5tyj/runs/${l%%:*} $W/w/s/${l##*:}; done
m filmspecks.py old-f-M f-M2 old-f-X f-X2
m filmspecks.py --case bay-sunbeam old-fs-M fs-M2 old-fs-X fs-X2
m filmsheet.py $P/layer-rect-bay-frames.png rect-bay 0.4 "old main c4c1f1f2f=old-f-M" "M2=f-M2" "Design X alone=old-f-X" "X2=f-X2"
m sheet.py $P/layer-exit-cabin-jamb.png exit-cabin 450,350,750,1000 1 'old main c4c1f1f2f=old-M' 'M2=s-M2' 'Design X alone=old-X' 'X2=s-X2'
echo "## The videos (video.sh), M2 left and X2 right at M2's fixed exposure"
for v in "rect-bay f-M2 f-X2" "bay-sunbeam fs-M2 fs-X2"; do set -- $v
  stops=$(python3 -I -c "import json; print('%.2f' % json.load(open('$W/runs/$2/results/motion/$1.json'))['stops'])")
  [ -e $P/$1-M2-X2.mp4 ] || bash "$T/video.sh" $P/$1-M2-X2.mp4 1920 "M2  main 284025851,  $stops stops" $2 "X2  merged head a7e373197,  $stops stops" $3
done
echo "## Motion in the two films: light per region, specks, grain and two fixed edges per frame, and five frames of each"
EDGES='window:860,180,910,340;corner:990,100,1050,450'
m filmmotion.py $P/rect-bay-motion.png 'film rect-bay, M2 and X2 at -0.26 stops' "$EDGES" M2=f-M2 X2=f-X2
m filmmotion.py $P/bay-sunbeam-motion.png 'film bay-sunbeam, M2 and X2 at -4.01 stops' "$EDGES" M2=fs-M2 X2=fs-X2
m filmstrip.py $P/rect-bay-strip.png f-M2 f-X2
m filmstrip.py $P/bay-sunbeam-strip.png fs-M2 fs-X2
fi
echo "## Take 2b: the jambs after the switch at constant render scales 0.956 and 2/3 (the glow-stays case)"
m jambsheet.py $P/jamb-sheet-glow-stays.png 2 'M2 0.956=sc-M2-956' 'X2 0.956=sc-X2-956' 'M2 2/3=sc-M2-23' 'X2 2/3=sc-X2-23'
echo "## Take 4: the deck's GPU frame span, per round"
echo "\$ timetable.py $W/timing"; python3 -I "$T/timetable.py" $W/timing; echo
echo "## The stills sample at a constant render scale of 0.956 beside 2/3 (M2 = m2g, X2 = x2g, with scratch-render-scale.patch)"
for c in rect-lamps exit-cabin deck-cuts bay-sunbeam; do m2=s9-M2; [ $c = deck-cuts ] && m2=s9d-M2
  m sheet.py $P/s956-$c.png $c full 0.4 "M2 0.956=$m2" "X2 0.956=s9-X2" "M2 2/3=s-M2" "X2 2/3=s-X2"; done
m sheet.py $P/s956-rect-lamps-408.png rect-lamps full 0.4 "M2 0.956=e9-M2" "X2 0.956=e9-X2" "M2 2/3=e-M2" "X2 2/3=e-X2"
m stillcrops.py $P/s956-rect-lamps-opening.png rect-lamps 1640,360,1920,600 2 'M2 0.956=s9-M2' 'X2 0.956=s9-X2' 'M2 2/3=s-M2' 'X2 2/3=s-X2'
m stillcrops.py $P/s956-deck-cuts-glass.png deck-cuts 1150,40,1600,360 1 'M2 0.956=s9d-M2' 'X2 0.956=s9-X2' 'M2 2/3=s-M2' 'X2 2/3=s-X2'
m stillcrops.py $P/s956-bay-sunbeam-slit.png bay-sunbeam 150,0,420,360 2 'M2 0.956=s9-M2' 'X2 0.956=s9-X2' 'M2 2/3=s-M2' 'X2 2/3=s-X2'
echo "## Take 2b and 2c: glow stays against glow off at both constant scales"
m glowsheet.py $P/glow-switch-23.png 23
m glowsheet.py $P/glow-switch-956.png 956
echo "## Take 2c: the fair switch at the survey's scale, M2's controls for both sides, then each side against its own"
m switch.py s/wg-M2/$R s/wg-M2/$R s/wg-X2/$R
for v in wg-M2 wg-X2; do echo "== $v against its own controls"; m regions.py s/$v/$R s/$v/$R; done
echo "== wg-X2 against wg-M2's controls"; m regions.py s/wg-X2/$R s/wg-M2/$R
m levelchart.py $P/switch-r11-glow-off.png s/wg-M2/$R M2=s/wg-M2/$R X2=s/wg-X2/$R
echo "## Take 2d: the jambs through the dynamic scale's replay path on M2 at 0.956, beside the fixed path"
m jambsheet.py $P/jamb-sheet-replay.png 2 'M2 fixed 0.956=sc-M2-956' 'M2 replay 0.956=scr-M2-956' 'M2 fixed 2/3=sc-M2-23'
m scalepair.py $P/jamb-m2-replay-0956-vs-fixed.png scr-M2-956 sc-M2-956 150 355
m scalepair.py $P/jamb-m2-fixed-0956-vs-23.png sc-M2-956 sc-M2-23 150 355
echo "## What stays at a render scale near 1 (bug t58-i3sn), and on M3 and X3"
m edgefaults.py $P/opening-0956-vs-23.png 150 220 355 -- 'M2 0.956=sc-M2-956' 'X2 0.956=sc-X2-956' 'M2 2/3=sc-M2-23' 'X2 2/3=sc-X2-23'
m edgefaults.py $P/opening-0956-m2-vs-m3.png 150 220 355 -- 'M2 0.956=sc-M2-956' 'M3 0.956=scr-M3-956' 'M2 2/3=sc-M2-23'
m edgefaults.py $P/opening-0956-x2-vs-x3.png 150 220 355 -- 'X2 0.956=sc-X2-956' 'X3 0.956=scr-X3-956' 'X2 2/3=sc-X2-23'
echo "## The size ramps (the acceptance lead's sizeramp.sh of b53c037f1; frames deleted, the saved tables' last lines)"
for c in M3 X3 W; do echo "== ramp-$c.txt"; tail -n 4 "$D/ramp-$c.txt"; echo; done
echo "## The window test (wintest.sh; frames deleted: the saved results, and the sheet from the opening crops that remain)"
cat "$D/window.txt"; echo
m winsheet.py $P/window-1800x1008-M3-W.png "M3 1800x1008=$W/runs/win-M3-1800x1008/opening.png" "W 1800x1008=$W/runs/win-W-1800x1008/opening.png" "M3 1920x1080=$W/runs/win-M3-1920x1080/opening.png" "W 1920x1080=$W/runs/win-W-1920x1080/opening.png"
echo "## The wobble before and after the fix (frames deleted; the saved output)"
cat "$D/wobble.txt"
echo "## The edge ramp on W: the window edges per render size with the light held still (frames deleted; the saved output)"
[ -e "$D/edgeramp.txt" ] && cat "$D/edgeramp.txt"
