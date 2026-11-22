#!/usr/bin/env bash
# Tool code of t0-9fo0, part B. measure-b.sh: every measure of part B from the runs in /private/tmp/t0-9fo0b/runs, in the
# order of README.md's part B tables, and the part B pictures. Its output is the part B half of tables.txt.
T=$(cd "$(dirname "$0")" && pwd); P=$(cd "$T/../pics" && pwd)
export BASE=/private/tmp/t0-9fo0b; B=$BASE/runs; L=$BASE/regionlog
m() { bash "$T/m.sh" "$@" 2>&1 | grep -vE "^warning|^\s*$"; }
R=results/switch/cabin.json
still() { python3 -c "
import json; r = json.load(open('$B/$1/results/still/rect-lamps.json'))
print('$1 specks %.5f blotch %.5f level %+.5f' % (r['specks'], r['blotch'], r['level']))"; }
same() { cmp -s "$1" "$2" && echo "same: $1 $2" || echo "DIFFERENT: $1 $2"; }
echo "# Take 1: the still rect-lamps (stills.py, daub.py against A's long reference, light.py against A)"
m stills.py rect-lamps s-M s-X0 s-X s-A e-M e-X0 e-X e-A
for v in s-M s-X0 s-X s-A e-M e-X0 e-X e-A; do still $v; done
REF=s-A m daub.py s-M:rect-lamps s-X0:rect-lamps s-X:rect-lamps s-A:rect-lamps
REF=e-A m daub.py e-M:rect-lamps e-X0:rect-lamps e-X:rect-lamps e-A:rect-lamps
m light.py s-A s-M s-X0 s-X s-A
m light.py e-A e-M e-X0 e-X e-A
echo "# Take 1: the region log changes no image (s-Xr and s-Xr2 against s-X)"
for r in s-Xr s-Xr2 s-X2; do for f in rect-lamps.png rect-lamps-reference.png; do same $B/s-X/images/still/$f $B/$r/images/still/$f; done; done
echo "# Take 1: pictures (sheet.py, blotchmap.py)"
m sheet.py $P/b-rect-lamps-whole.png rect-lamps full 0.333 'M=s-M' 'X0=s-X0' 'X=s-X' | tail -1
m sheet.py $P/b-rect-lamps-whole-408.png rect-lamps full 0.333 'M, 4.08 stops=e-M' 'X0, 4.08 stops=e-X0' 'X, 4.08 stops=e-X' | tail -1
m sheet.py $P/b-rect-lamps-ceiling.png rect-lamps 640,0,1280,420 1 'M=s-M' 'X0=s-X0' 'X=s-X' | tail -1
m sheet.py $P/b-rect-lamps-ceiling-408.png rect-lamps 640,0,1280,420 1 'M, 4.08 stops=e-M' 'X0, 4.08 stops=e-X0' 'X, 4.08 stops=e-X' | tail -1
m blotchmap.py $P/b-rect-lamps-blotch-map.png M=s-M X0=s-X0 X=s-X
echo "# Take 2: the switch cabin (switch.py of t0-xlcu: M's controls for all sides, then each side against its own)"
m switch.py s/w-M/$R s/w-M/$R s/w-X0/$R s/w-X/$R | head -4
m switch.py s/w-X0/$R s/w-X0/$R | sed -n 2p
m switch.py s/w-X/$R s/w-X/$R | sed -n 2p
python3 -c "import json; print('w-X failed', json.load(open('$B/w-X/$R'))['failed'])"
echo "# Take 2 per region (regions.py), each side against its own controls"
for v in w-M w-X0 w-X; do echo "== $v"; m regions.py s/$v/$R s/$v/$R; done
m levelchart.py $P/b-switch-r11.png s/w-M/$R M=s/w-M/$R X0=s/w-X0/$R X=s/w-X/$R | tail -1
echo "# Take 2: the grid films repeat the switch takes (the series of g-* against w-*)"
for s in M X0; do same $B/w-$s/series/switch/cabin-switch.json $B/g-$s/series/grid/cabin-switch.json; done
same $B/w-X/series/switch/cabin-switch.json $B/g-Xr/series/grid/cabin-switch.json
same $B/w-X/series/switch/cabin-switch.json $B/g-Xr2/series/grid/cabin-switch.json
echo "# Take 2: the anchors' grid (gridsheet.py, gridedge.py)"
m gridsheet.py $P/b-grid-whole.png $P/b-grid-crops.png g-M g-X0 g-Xr
m gridedge.py g-M g-X0 g-Xr
m gridstep.py g-M g-X0 g-Xr
echo "# The region's test: the share of anchor-draws with a moved answer (regionfires.py, the counts of the first log)"
m regionfires.py $L/s-Xr
m regionfires.py $L/g-Xr
echo "# The region's test per span (regionspans.py, the per-anchor log): the still, then the switch"
m regionspans.py $L/s-Xr2/region-none s-X $P/b-fires-still.png 37,132 133,400 401,923
m regionspans.py $L/g-Xr2/region-193-393 s-X $P/b-fires-switch.png 36,132 133,192 193,292 293,392 393,492
echo "# The region test replayed on real, shuffled and Gaussian light (regionnoise.py): the still, then the switch"
m regionnoise.py $L/s-Xr2/region-none 37,132 133,400 401,923
m regionnoise.py $L/g-Xr2/region-193-393 36,132 133,192 293,392
echo "# Take 3: the stand part (survey results, region r11 = index 5), and part A's M and X0"
for v in st-X; do python3 -c "
import json; r = json.load(open('$B/$v/results/stand/cabin-stand.json'))
print('$v settleDraws', r['settleDraws'], 'after tick', r['settleDrawsAfterTick'], 'first window r11', round(r['excess'][0][5], 4), 'failed', r['failed'])"; done
echo "# Take 3: the stand film (standfilm.py: the survey's motion part)"
for v in sf-M sf-X; do python3 -c "
import json; r = json.load(open('$B/$v/results/motion/cabin-stand-film.json'))
print('$v stops', r['stops'], 'flicker', r['flicker'], 'grain', r['grainFlicker'], 'samples', r['samples'], 'failed', r['failed'])"; done
m filmsheet.py $P/b-stand-film-frames.png cabin-stand-film 0.5 'M film=sf-M' 'X film=sf-X' | tail -1
