#!/usr/bin/env bash
# Tool code of ter-yglw: the Mac's timed runs, rounds of the four variants in a rotated order (size:cull), each through
# time.sh on the deck script. A run starts only when the 1-minute load is below <max load> (default 8 on this 10-core
# Mac), so that no run times beside another agent's test run; it waits in steps of 60 s otherwise.
# Usage (in the dev shell, from the checkout root): bash queue-mac.sh <out root> <script.json> <rounds> [max load]
set -uo pipefail
root=$1; script=$2; rounds=$3; max=${4:-8}
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# VARIANTS (env) replaces the four variants, for example "32:1 128:0"; other TERTIUS_PROBE_ settings pass through.
read -r -a variants <<< "${VARIANTS:-128:0 128:1 32:0 32:1}"
nv=${#variants[@]}
for ((r = 0; r < rounds; r++)); do
  for ((k = 0; k < nv; k++)); do
    v=${variants[$(((k + r) % nv))]}; size=${v%%:*}; cull=${v#*:}
    name="r$r-w$size-c$cull"
    [[ -f "$root/$name/gpu-timings.json" ]] && continue
    while :; do
      load=$(uptime | sed -E 's/.*load averages?: ([0-9.]+).*/\1/')
      awk -v l="$load" -v m="$max" 'BEGIN{exit !(l < m)}' && break
      echo "$(date +%H:%M) load $load, waiting"; sleep 60
    done
    free=$(/bin/df -g / | awk 'NR==2{print $4}'); [[ "$free" -lt 20 ]] && { echo "disk $free GiB: stop"; exit 2; }
    echo "$(date +%H:%M) $name load $load"
    bash "$here/time.sh" "$root/$name" "$script" "$size" "$cull" --size 1280x720 --in-flight 2 | tail -2
    # The load that counts is the one when the run took the lock (run.log's uptime-start), after its wait. A run that
    # took it at <max load> or more, or that failed, moves aside as x-<name>-<n> and runs again, at most 4 times.
    started=$(sed -nE 's/^uptime-start:.*load averages?: ([0-9.]+).*/\1/p' "$root/$name/run.log")
    if [[ ! -f "$root/$name/frames.log" ]] || awk -v l="${started:-99}" -v m="$max" 'BEGIN{exit !(l >= m)}'; then
      n=0; while [[ -e "$root/x-$name-$n" ]]; do n=$((n + 1)); done
      mv "$root/$name" "$root/x-$name-$n"; echo "  set aside (load ${started:-none} at the lock)"
      ((n < 3)) && { k=$((k - 1)); continue; }
    fi
  done
done
echo queue done
