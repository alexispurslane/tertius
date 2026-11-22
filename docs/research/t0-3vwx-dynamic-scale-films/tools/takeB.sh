#!/usr/bin/env bash
# Tool code of t0-3vwx. takeB.sh: every measure of take B, from the runs of queue-b.sh, in the order of README.md's
# take B section. Its output is takeB.txt. The picture runs (s-X3, l-X5, s-M3, and s-M5 and s-X5 of take A) are in
# /private/tmp/t0-gbgj/macdyn5/pics; their frames are deleted after their numbers are committed, so a later run of this
# script prints only the sections whose inputs remain. The survey's runs (sw-*, wk-*) are in /private/tmp/t0-gbgj/runs.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/.." && pwd); G=$(cd "$N/../t0-gbgj-combined-check/tools" && pwd)
R=$(git -C "$T" rev-parse --show-toplevel)
# The acceptance record's tools, run by their paths (726bf338c, in the acceptance lead's worktree).
A=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/tools
O=/private/tmp/t0-gbgj/macdyn5/pics; D=/private/tmp/t0-gbgj/wobdump5; C=/private/tmp/t0-gbgj/check5; mkdir -p $D $C
X=$N/sizes/switch-x-16.6667/size-193-393.txt; M=$N/sizes/switch-m-16.6667/size-193-393.txt
py() { echo "\$ $*"; nix develop "$R" --command python3 -I "$@" 2>&1 | grep -vE "^warning|sqlite|^\s*$"; echo; }
have() { [ -e "$O/$1/f-000140.png" ]; }
echo "## The picture runs: jambs, grain and history per span, split by changed and one-size draws (measure5.py)"
for r in s-M5:M5 s-X5:X5 s-X3:X3 s-M3:M3 l-X5:L5; do have ${r%%:*} && py "$T/measure5.py" switch "${r##*:}|$O/${r%%:*}"; done
echo "## The two window edges (wobble.py of the combined check), and their split (wobsplit.py)"
for r in s-M5:M5:size-193-393 s-X5:X5:size-193-393 s-X3:X3:size-193-393 s-M3:M3:size-193-393 l-X5:L5:size-none; do
  IFS=: read -r run label log <<< "$r"
  have $run && WOBBLE_DUMP=$D py "$G/wobble.py" "$C/edges-$label.png" "$label|$O/$run|$O/$run/$log.txt"
done
py "$G/wobsplit.py" "$X" "X3|$D/X3.txt|aspect" "X5|$D/X5.txt|flat" "L5|$D/L5.txt|flat"
py "$G/wobsplit.py" "$M" "M3|$D/M3.txt|aspect" "M5|$D/M5.txt|flat"
echo "## The level's kink at size changes (levjump.py) and the boil without the fade (boilnoise.py)"
for r in s-M5:M5:size-193-393 s-X5:X5:size-193-393 s-X3:X3:size-193-393 s-M3:M3:size-193-393 l-X5:L5:size-none; do
  IFS=: read -r run label log <<< "$r"
  have $run && py "$G/levjump.py" "$label|$O/$run|$O/$run/$log.txt" && py "$A/boilnoise.py" "$label|$O/$run|$O/$run/$log.txt"
done
echo "## The region test per span (dynscale.py)"
for r in s-X5 s-X3; do py "$A/dynscale.py" "$O/$r"; done
echo "## The survey's switch cabin under the played-back scale: draws to settle per region, each run against its own controls"
for r in sw-M5 sw-X5 sw-X5r sw-M3 sw-X3; do
  [ -e /private/tmp/t0-gbgj/runs/$r/results/switch/cabin.json ] || continue
  echo "== $r"; bash "$G/m.sh" regions.py s/$r/results/switch/cabin.json s/$r/results/switch/cabin.json 2>&1 | grep -vE "warning|sqlite"
  for f in /private/tmp/t0-gbgj/runs/$r/sizes/*.txt; do
    log=$M; case $r in *X*) log=$X;; esac
    echo "  $(basename $f): $(wc -l < $f) draws, lines that differ from the card's log: $(diff <(awk '{print $1,$2,$3}' $f) <(awk '{print $1,$2,$3}' $log) | grep -c '^[<>]')"
  done
done
echo
echo "## The survey's walk cabin-walk under the played-back scale"
for r in wk-M5 wk-X5; do
  f=/private/tmp/t0-gbgj/runs/$r/results/walk/cabin-walk.json; [ -e $f ] || continue
  echo "== $r: $(python3 -I -c "import json; r = json.load(open('$f')); print('slowest region', r['settleDraws'], 'draws, whole image', r['settleDrawsWhole'], 'draws, per region', r['regionDraws'], 'failed', r['failed'])")"
done
echo
echo "## The walk's history lengths and region test (walklogs.py)"
py "$T/walklogs.py" "M5|$O/w-M5" "X5|$O/w-X5"
