#!/usr/bin/env bash
# Tool code of t0-3vwx. final.sh <switch|walk>: the final take of one film under the dynamic render scale, in one command,
# on the Design X commit XF and the main commit MF (default ac5f23915), the same method as takes A and B:
#   1. the probe builds xf and mf (builds.sh), each skipped when its probe-bin exists;
#   2. the card's size logs (cloud.sh sizes 16.6667 with X=$XF, M=$MF, C=$W/cloud-final): one job logs both films on both
#      codes, so the second film reuses it. The logs are copied into ../sizes-final/;
#   3. the Mac's picture runs with the card's scale played back (queue-a.sh: s-XF and s-MF, or w-XF and w-MF), each
#      checked against its card log;
#   4. the survey's own take of the film on both codes (t0-gbgj's take.sh, the card's sizes played back): the settle;
#   5. the measures into ../final-<film>.txt (measure5.py, wobble.py, wobsplit.py, levjump.py, boilnoise.py and
#      dynscale.py for the switch; walklogs.py for the walk; the survey's settle per region for both);
#   6. the video ../pics/<film>-final.mp4 (the acceptance record's sidevideo.py, by its path), main left and Design X right.
# Each step stops the script on a failure. The disk rules of the line orchestrator hold: a build or a picture run starts
# at 22 GiB free or more (builds.sh and queue-a.sh check it). The cloud job costs about $0.05 (take A's f434f616).
# Example: XF=<commit> MF=ac5f23915 bash final.sh switch, then XF=<commit> bash final.sh walk.
set -euo pipefail
film=$1
: "${XF:?XF must name the Design X commit}"; MF=${MF:-ac5f23915}
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/.." && pwd); G=$(cd "$N/../t0-gbgj-combined-check/tools" && pwd)
R=$(git -C "$T" rev-parse --show-toplevel)
A=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/tools
W=/private/tmp/t0-gbgj; O=$W/macdyn5/pics; C=$W/cloud-final; S=$N/sizes-final; D=$W/wobdump-final
XF=$(git -C "$R" rev-parse "$XF"); MF=$(git -C "$R" rev-parse "$MF")
echo "$(date +%T) final $film: Design X ${XF:0:9}, main ${MF:0:9}"
# 1. The builds. A build of another commit under the same name is removed first.
for b in xf mf; do
  want=$([ $b = xf ] && echo $XF || echo $MF)
  [ "$(cat $W/bins/$b/commit 2>/dev/null)" = $want ] && continue
  rm -rf $W/bins/$b
  XF=$XF MF=$MF bash "$T/builds.sh" $b
  echo $want > $W/bins/$b/commit
done
# 2. The card's size logs.
if [ ! -d $S ]; then
  X=$XF M=$MF C=$C bash "$T/cloud.sh" sizes 16.6667
  mkdir -p $S && cp -r $C/sizes/out/sizes/. $S/
fi
case $film in
  switch) runs="s-MF s-XF"; log=size-193-393; part=switch; case=cabin; json=switch/cabin; sv=sw ;;
  walk) runs="w-MF w-XF"; log=size-none; part=walk; case=cabin-walk; json=walk/cabin-walk; sv=wk ;;
  *) echo "final.sh <switch|walk>"; exit 1 ;;
esac
# FROM=4 starts at the survey's takes and keeps the picture runs of an earlier call (queue-a.sh can make a missing one);
# FROM=5 starts at the measures and keeps the survey's takes too.
if [ "${FROM:-1}" -le 3 ]; then
# 3. The picture runs (a run's folder is replaced when it exists).
for r in $runs; do rm -rf $O/$r; done
q=$(SIZES=$S bash "$T/queue-a.sh" $runs); echo "$q" | tee -a $W/logs/final.log
! grep -q "DIFFER\|stop before" <<< "$q" || { echo "a picture run failed its check: stop"; exit 1; }
fi
if [ "${FROM:-1}" -le 4 ]; then
# 4. The survey's takes.
for side in m x; do
  b=${side}f; run=$sv-$([ $side = m ] && echo M || echo X)F; rm -rf $W/runs/$run
  bash "$G/take.sh" $run $part $case $b TERTIUS_X_FRAME_TIME_TARGET=16.6667 \
    TERTIUS_X_SIZE_REPLAY=$S/$film-$side-16.6667/$log.txt TERTIUS_X_SIZE_LOG=$W/runs/$run/sizes
done
fi
# 5. The measures.
out=$N/final-$film.txt; mkdir -p $D $W/check-final
py() { echo "\$ $*"; nix develop "$R" --command python3 -I "$@" 2>&1 | grep -vE "^warning|sqlite|^\s*$"; echo; }
{
  echo "# final.sh $film: Design X $XF, main $MF, $(date '+%Y-%m-%d %H:%M')"
  for r in $runs; do echo "$r: $(sort -u -k2,3 $O/$r/$log.txt | wc -l | tr -d ' ') render sizes, scale $(awk 'NR > 173 {print $4}' $O/$r/$log.txt | sort -g | sed -n '1p;$p' | tr '\n' ' ')in the film's window"; done
  if [ $film = switch ]; then
    py "$T/measure5.py" switch "MF|$O/s-MF" "XF|$O/s-XF"
    for r in s-MF:MF s-XF:XF; do WOBBLE_DUMP=$D py "$G/wobble.py" $W/check-final/edges-${r##*:}.png "${r##*:}|$O/${r%%:*}|$O/${r%%:*}/$log.txt"; done
    py "$G/wobsplit.py" $S/switch-x-16.6667/$log.txt "XF|$D/XF.txt|flat"
    py "$G/wobsplit.py" $S/switch-m-16.6667/$log.txt "MF|$D/MF.txt|flat"
    for r in s-MF:MF s-XF:XF; do py "$G/levjump.py" "${r##*:}|$O/${r%%:*}|$O/${r%%:*}/$log.txt"; py "$A/boilnoise.py" "${r##*:}|$O/${r%%:*}|$O/${r%%:*}/$log.txt"; done
    py "$A/dynscale.py" $O/s-XF
  else
    py "$T/walklogs.py" "MF|$O/w-MF" "XF|$O/w-XF"
  fi
  for side in M X; do run=$sv-${side}F
    echo "== the survey's $run"
    if [ $film = switch ]; then bash "$G/m.sh" regions.py s/$run/results/$json.json s/$run/results/$json.json 2>&1 | grep -vE "warning|sqlite"
    else python3 -I -c "import json; r = json.load(open('$W/runs/$run/results/$json.json')); print('slowest region', r['settleDraws'], 'whole', r['settleDrawsWhole'], 'per region', r['regionDraws'], 'failed', r['failed'])"; fi
  done
} > $out
# 6. The video.
nix develop "$R" --command python3 -I "$A/sidevideo.py" $N/pics/$film-final.mp4 $film \
  "main ${MF:0:9}|$O/${runs%% *}|$O/${runs%% *}/$log.txt" "Design X ${XF:0:9}|$O/${runs##* }|$O/${runs##* }/$log.txt" >> $out 2>&1
echo "$(date +%T) final $film done: $out, $N/pics/$film-final.mp4"
