#!/usr/bin/env bash
# Tool code of the region take (t0-qvm1). queue.sh: the take's runs on the builds xfd (Design X 05a27262d with region
# evidence) and xfn (without it, scratch-no-region-evidence.patch), from t0-gumm's builds.sh, under the shared GPU lock:
#   1. the walk's pictures (t0-3vwx's macdyn.sh, the walk's last 60 draws and the 200 after the stop), both codes on
#      the card's walk log of Design X 05a27262d from t0-3vwx's final take (sizes-final/walk-x-16.6667), so that the
#      numbers stand beside that film's; then the acceptance record's sidevideo.py: the video pics/walk-rule-on-off.mp4
#      and the center region's light after the stop; then the PNGs are deleted;
#   2. the survey's walk take of both codes on the same log (t0-gbgj's take.sh): the settle after the stop;
#   3. the survey's switch take of both codes at the constant scale 2/3: the settle after off and after on.
# The outputs go to /private/tmp/t0-gbgj (macdyn5/pics/w-QD, w-QN; runs/wk-QD, wk-QN, sw-QD, sw-QN) and the numbers to
# ../queue.txt. A run starts at 22 GiB free or more.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/.." && pwd); R=$(cd "$N/../../.." && pwd)
V=$R/docs/research/t0-3vwx-dynamic-scale-films; G=$R/docs/research/t0-gbgj-combined-check/tools
A=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/tools
W=/private/tmp/t0-gbgj; O=$W/macdyn5/pics; LOG=$V/sizes-final/walk-x-16.6667/size-none.txt
free() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
need() { [ "$(free)" -ge 22 ] || { echo "$(date +%T) only $(free) GiB free: stop before $1"; exit 1; }; }
wk=$( (for k in $(seq 60 119); do printf 'g-%06d,' $k; done; for k in $(seq 0 199); do printf 'h-%06d,' $k; done) )
out=$N/queue.txt; echo "# queue.sh, $(date '+%Y-%m-%d %H:%M')" >> $out
for b in xfd xfn; do q=Q${b:2:1}; name=w-${q^^}
  [ -s $N/pics/walk-rule-on-off.mp4 ] && break  # the walk's video exists: step 1 is done
  need $name; KEEP=$wk bash $V/tools/macdyn.sh pictures walk $b $LOG $name TERTIUS_X_HISTORY_LOG=$O/$name | tee -a $out
done
[ -s $N/pics/walk-rule-on-off.mp4 ] || (cd "$R" && nix develop --command python3 -I $A/sidevideo.py $N/pics/walk-rule-on-off.mp4 walk \
  "with region evidence|$O/w-QD|$O/w-QD/size-none.txt" "without region evidence|$O/w-QN|$O/w-QN/size-none.txt") 2>&1 \
  | grep -v "warning\|sqlite" | tee -a $out
[ -s $N/pics/walk-rule-on-off.mp4 ] && rm -f $O/w-QD/*.png $O/w-QN/*.png
for b in xfd xfn; do q=Q${b:2:1}; q=${q^^}
  need wk-$q; bash $G/take.sh wk-$q walk cabin-walk $b TERTIUS_X_FRAME_TIME_TARGET=16.6667 TERTIUS_X_SIZE_REPLAY=$LOG \
    TERTIUS_X_SIZE_LOG=$W/runs/wk-$q/sizes | tee -a $out
  python3 -I -c "import json; r = json.load(open('$W/runs/wk-$q/results/walk/cabin-walk.json')); print('wk-$q slowest region', r['settleDraws'], 'whole', r['settleDrawsWhole'], 'per region', r['regionDraws'])" | tee -a $out
done
for ((d = 0; d < 500; d++)); do echo "$d 0 0 0.6667"; done > $W/replay-0.6667.txt
for b in xfd xfn; do q=Q${b:2:1}; q=${q^^}
  need sw-$q; bash $G/take.sh sw-$q switch cabin $b TERTIUS_X_FRAME_TIME_TARGET=16.6667 \
    TERTIUS_X_SIZE_REPLAY=$W/replay-0.6667.txt TERTIUS_X_SIZE_LOG=$W/runs/sw-$q/sizes | tee -a $out
  (cd "$R" && bash $G/m.sh regions.py s/sw-$q/results/switch/cabin.json s/sw-$q/results/switch/cabin.json 2>&1 | grep -vE "warning|sqlite") | tee -a $out
done
echo "$(date +%T) queue.sh done" | tee -a $out
