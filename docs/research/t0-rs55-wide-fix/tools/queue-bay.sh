#!/usr/bin/env bash
# Tool code of t0-rs55. queue-bay.sh: the bay's start of the wide fix's takes, through t0-gumm's fairrun.sh (a frozen film of
# bay-sunbeam's view at 1920 x 1080 and the constant render scale 2/3, the fixed exposure of -4.0 stops) and fairmeasure.py.
#   1. check-real: xrz (builds.sh: the real form on the scratch form's base) with today's shape (1, 8, 0, 0) and the own-ray
#      log, against t0-gumm's bay-wide-today (xwz with TERTIUS_X_WIDE_FIX=1): every row of frames.series (the region means of
#      every draw, to the last digit) and the PNGs that both kept (draws 1, 2, 4, 7 and 8).
#   2. today and wide: m0z (main 86ae0fbc8) and m1z (the wide fix), each at the world's tick 420, 421 and 422
#      (FAIR_TICKS_ADD 0, 1, 2: the spread between runs).
#   3. the same with TERTIUS_PROBE_SIGNAL=3: the reflections alone.
# Every run is measured (fairmeasure.py) and keeps the PNGs of draws 0, 1, 2, 4 and 8. A run starts at 20 GiB of free disk or
# more (the line orchestrator's limit); the queue stops under 20.
T=$(cd "$(dirname "$0")" && pwd); G=$(cd "$T/../../t0-gumm-catch-up-spread/tools" && pwd); R=$(cd "$T/../../../.." && pwd)
F=/private/tmp/t0-gbgj/gumm-fair
freegb() { df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}'; }
same() {  # same <run> <reference run>: every frames.series row, then the PNGs that both kept
  local a=$F/$1 b=$F/$2
  if cmp -s <(sort $a/frames.series) <(sort $b/frames.series); then echo "$1 against $2: frames.series same in all $(wc -l < $a/frames.series) rows"
  else echo "$1 against $2: frames.series DIFFERENT in $(diff <(sort $a/frames.series) <(sort $b/frames.series) | grep -c '^<') rows"; fi
  for p in $b/f-*.png; do [ -e "$p" ] && [ -e $a/$(basename $p) ] && { cmp -s $p $a/$(basename $p) && echo "  $(basename $p) same" || echo "  $(basename $p) DIFFERENT"; }; done
}
measure() {  # measure <run> <label>: fairmeasure.py, then keep only the PNGs of draws 0, 1, 2, 4 and 8
  (cd "$R" && nix develop --command python3 "$G/fairmeasure.py" "$2|$F/$1|bay|start" 2>&1 | grep -v -i 'warning\|sqlite\|flake')
  keepn="f-000000.png f-000001.png f-000002.png f-000004.png f-000007.png f-000008.png"
  (cd $F/$1 && for png in *.png; do [[ " $keepn " == *" $png "* ]] || /bin/rm -f "$png"; done)
}
run() {  # run <name> <build> [VAR=value...]
  local name=$1 build=$2; shift 2
  [ "$(freegb)" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
  bash "$G/fairrun.sh" $build bay start $name "$@"
  [ "$(cat $F/$name/size-*.txt 2>/dev/null | wc -l)" -ge 200 ] || { echo "$(date +%T) $name: incomplete"; exit 1; }
}
if [ "${SKIP_CHECK:-}" != 1 ]; then
  run rs-check-real xrz "TERTIUS_X_CATCHUP=1 8 0 0" "TERTIUS_X_OWNRAY_LOG=$F/rs-check-real"
  same rs-check-real bay-wide-today
  measure rs-check-real check-real
fi
for signal in all reflections; do
  for t in 0 1 2; do
    for side in today:m0z wide:m1z; do
      IFS=: read code build <<< "$side"
      name=rs-bay-$signal-$code+$t
      extra=(); [ $signal = reflections ] && extra=(TERTIUS_PROBE_SIGNAL=3)
      FAIR_TICKS_ADD=$t run $name $build ${extra[@]+"${extra[@]}"}
      measure $name "$signal $code +$t"
    done
  done
done
echo "$(date +%T) queue-bay.sh done"
