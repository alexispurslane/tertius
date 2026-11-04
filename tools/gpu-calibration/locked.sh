#!/usr/bin/env bash
# Tool code (tools/README.md, "GPU calibration"): runs one command under the GPU lock, /tmp/tertius-gpu-measure.lock,
# which every timed run on the development Mac takes. It waits while another run holds the lock, records uptime at the
# start and the end of the run, and removes the lock when the run ends, also when it fails. It never removes a lock
# that it did not make.
#   bash tools/gpu-calibration/locked.sh <log> <command> [arguments]
# The log gets the two uptime lines and the command's output. The exit code is the command's.
set -uo pipefail

log=${1:?usage: locked.sh <log> <command> [arguments]}
shift
lock=/tmp/tertius-gpu-measure.lock

held=0
trap '[ "$held" = 1 ] && rmdir "$lock"' EXIT
# A signal ends the script through exit, so that the trap above also runs for a killed or timed-out run.
trap 'exit 130' INT
trap 'exit 143' TERM
waited=0
until mkdir "$lock" 2> /dev/null; do
    sleep 5
    waited=$((waited + 5))
done
held=1
{
    echo "uptime-start: $(uptime)"
    echo "waited: ${waited}s"
} > "$log"
"$@" >> "$log" 2>&1
code=$?
echo "uptime-end: $(uptime)" >> "$log"
rmdir "$lock"
held=0
exit "$code"
