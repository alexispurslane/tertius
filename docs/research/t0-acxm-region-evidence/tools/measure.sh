#!/usr/bin/env bash
# Tool code of t0-acxm. measure.sh: prints the image tables of the bay takes (queue-bay.sh) from $W/runs, through m.sh.
# Its output is the record's tables.txt. The frames are deleted after the record, so a section prints only while its
# runs exist. The timing tables come from table.py.
. "$(dirname "$0")/vars.sh"
M=$(dirname "$0")/m.sh
run() { echo "\$ $*"; "$M" "$@" 2>&1 | grep -v -i "warning\|sqlite"; echo; }
F=results/motion/rect-bay.json
echo "## Film rect-bay: the survey's middle and last frame"; echo
run films.py s/t-M-rb/$F s/t-B-rb/$F s/t-B1-rb/$F s/t-B6-rb/$F
run filmspecks.py t-M-rb t-B-rb t-B1-rb t-B6-rb
echo "\$ flicker (results/motion/rect-bay.json)"
for v in t-M-rb t-B-rb t-B1-rb t-B6-rb; do
  python3 -I -c "import json,sys; r=json.load(open(sys.argv[1])); print(f'{sys.argv[2]:12} flicker {r[\"flicker\"]:.5f} grain {r[\"grainFlicker\"]:.5f}')" "$W/runs/$v/$F" "$v"
done; echo
echo "## Lag film bay-sun: median lag in draws / median lag error"; echo
run lags.py s/t-M-lag s/t-B-lag s/t-B1-lag
for c in bay-sunbeam exit-cabin; do
  echo "## Still $c: specks, blotch, daub against t-A-s"; echo
  run stills.py $c t-A-s t-M-s t-B-s t-B1-s
  echo "\$ REF=t-A-s daub.py ..."; REF=t-A-s "$M" daub.py t-A-s:$c t-M-s:$c t-B-s:$c t-B1-s:$c 2>&1 | grep -v -i "warning\|sqlite"; echo
done
