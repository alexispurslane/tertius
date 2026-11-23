#!/usr/bin/env bash
# Tool code of t0-gumm. queue-mac2.sh: the Mac's event runs (macrun.sh) at the constant scale 2/3, after card job 2 chose
# the peak share 1/32: for each code, a picture run and a timing run (TIMING=1) of the switch, the start and the cut.
# The codes, in order: today (xg with the shape 1 8 0, byte-identical to today's code by check 1), A the cap (xb), B32
# the shape (0.03125, 32, 0) with the restart at 4 draws (xg), C32 the same shape with the cap (xc), then B16 and C16 with
# the shape (0.0625, 32, 0). C' (the cap, and spread rays only after a start or a cut) needs no run: after a light change
# it runs A's code path (no history is young), and after a start or a cut C's. A run whose size log is complete is kept,
# so the queue resumes. Each run goes to /private/tmp/t0-gbgj/gumm-mac/<event>-<code> (pictures) or t-<event>-<code>
# (timing). It stops under 20 GiB of free disk.
T=$(cd "$(dirname "$0")" && pwd); O=/private/tmp/t0-gbgj/gumm-mac
declare -A DRAWS=([switch]=493 [start]=200 [cut]=334)
done_run() { [ "$(cat $O/$1/size-*.txt 2>/dev/null | wc -l)" -ge ${DRAWS[$2]} ]; }
run() {  # run <code> <build> [VAR=value...]
  local code=$1 build=$2; shift 2
  for event in switch start cut; do
    [ "$(df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}')" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
    done_run $event-$code $event || bash "$T/macrun.sh" $build $event $event-$code "$@"
    done_run t-$event-$code $event || TIMING=1 bash "$T/macrun.sh" $build $event t-$event-$code "$@"
  done
}
run today xg 'TERTIUS_X_CATCHUP=1 8 0'
run A xb
run B32 xg 'TERTIUS_X_CATCHUP=0.03125 32 0'
run C32 xc 'TERTIUS_X_CATCHUP=0.03125 32 0'
run B16 xg 'TERTIUS_X_CATCHUP=0.0625 32 0'
run C16 xc 'TERTIUS_X_CATCHUP=0.0625 32 0'
