#!/usr/bin/env bash
# Tool code of t0-gumm. queue-mac1.sh: the Mac's first queue of event runs (macrun.sh) at the constant scale 2/3: for
# each code, a picture run and a timing run (TIMING=1) of the switch, the start and the cut. The codes: today (xg with
# the shape 1 8 0, byte-identical to today's code by check 1), A the cap (xb), B the shape (0.0625, 32, 0) with the
# restart at 4 draws (xg), C the same shape with the cap (xc). Each run goes to /private/tmp/t0-gbgj/gumm-mac/<event>-<code>
# (pictures) or t-<event>-<code> (timing). It stops under 20 GiB of free disk.
T=$(cd "$(dirname "$0")" && pwd)
run() {  # run <code> <build> [VAR=value...]
  local code=$1 build=$2; shift 2
  for event in switch start cut; do
    [ "$(df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}')" -lt 20 ] && { echo "under 20 GiB, stop"; exit 1; }
    bash "$T/macrun.sh" $build $event $event-$code "$@"
    TIMING=1 bash "$T/macrun.sh" $build $event t-$event-$code "$@"
  done
}
run today xg 'TERTIUS_X_CATCHUP=1 8 0'
run A xb
run B16 xg 'TERTIUS_X_CATCHUP=0.0625 32 0'
run C16 xc 'TERTIUS_X_CATCHUP=0.0625 32 0'
