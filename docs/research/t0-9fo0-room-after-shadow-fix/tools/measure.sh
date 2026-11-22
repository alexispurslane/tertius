#!/usr/bin/env bash
# Tool code of t0-9fo0. measure.sh: every measure of this record from the runs in /private/tmp/t0-9fo0/runs, in the
# order of README.md's tables. Its output is tables.txt.
T=$(dirname "$0"); m() { bash "$T/m.sh" "$@" 2>&1 | grep -vE "^warning|^\s*$"; }
R=results/switch/cabin.json
echo "# Take 1: the still rect-lamps (stills.py, daub.py against A's long reference, light.py against A)"
m stills.py rect-lamps s-M s-X0 s-A s-AM e-M e-X0 e-A e-AM
for v in s-M s-X0 s-A s-AM e-M e-X0 e-A e-AM; do python3 -c "
import json; r = json.load(open('/private/tmp/t0-9fo0/runs/$v/results/still/rect-lamps.json'))
print('$v specks %.5f blotch %.5f level %+.5f' % (r['specks'], r['blotch'], r['level']))"; done
REF=s-A m daub.py s-M:rect-lamps s-X0:rect-lamps s-A:rect-lamps
REF=e-A m daub.py e-M:rect-lamps e-X0:rect-lamps e-A:rect-lamps
m light.py s-A s-M s-X0 s-A
m light.py e-A e-M e-X0 e-A e-AM
echo "# Take 1, check 2: the side walls (walls.py)"
m walls.py s-M s-X0 s-A
echo "# Take 2: the switch cabin (switch.py of t0-xlcu: each side against its own controls, then X0 against M's)"
m switch.py s/w-M/$R s/w-M/$R
m switch.py s/w-X0/$R s/w-X0/$R
m switch.py s/w-M/$R s/w-X0/$R
echo "# Take 2 per region (regions.py), M then X0"
m regions.py s/w-M/$R s/w-M/$R
m regions.py s/w-X0/$R s/w-X0/$R
echo "# Take 3: the stand part (survey results, region r11 = index 5)"
for v in st-M st-X0; do python3 -c "
import json; r = json.load(open('/private/tmp/t0-9fo0/runs/$v/results/stand/cabin-stand.json'))
print('$v settleDraws', r['settleDraws'], 'after tick', r['settleDrawsAfterTick'], 'first window r11', round(r['excess'][0][5], 4), 'failed', r['failed'])"; done
echo "# Take 3: the stand film (standfilm.py: the survey's motion part)"
for v in sf-M sf-X0; do python3 -c "
import json; r = json.load(open('/private/tmp/t0-9fo0/runs/$v/results/motion/cabin-stand-film.json'))
print('$v stops', r['stops'], 'flicker', r['flicker'], 'grain', r['grainFlicker'], 'samples', r['samples'], 'failed', r['failed'])"; done
echo "# Take 3: the stand film's single-bounce cycle (cycle.py)"
for v in st-M st-X0; do m cycle.py stand s/$v/results/stand/cabin-stand.json 100,330 0 5; done
echo "# Take 5: the climb's step from tick 300 to 306 on M (climbstep.py)"
m climbstep.py s/c-M/results/climb/cabin-climb.json
echo "# Take 5: the jitter phases of the lamps-on control, M with and without jitter (phases.py), and X0"
m phases.py s/w-M/$R s/j-M/series/switch/cabin-on.json
m phases.py s/w-M/$R s/j-M/series/switch/cabin-on.json 172,459
m phases.py s/w-X0/$R
echo "# Take 5: the jitter cycle after the trend's division (cycle.py), M then X0"
for v in w-M w-X0; do m cycle.py switch:on s/$v/$R 36,215 0 1 2 3 4 5 6 7 8 9; done
echo "# Take 5: the census of young light-age records in the still (survey results)"
for v in n-X0 nj-X0 na-X0 naj-X0 nm-X0 ns-X0; do f=/private/tmp/t0-9fo0/runs/$v/results/census-still/rect-lamps.json; [ -e $f ] || continue; python3 -c "
import json; r = json.load(open('$f'))
for s in r['signals']:
    print('$v', s['light'], 'draws', s['draws'], 'levels', s['levels'], 'light pixel-draws', s['causes']['light']['pixelDraws'], 'share', s['causes']['light']['pixelShare'])"; done
