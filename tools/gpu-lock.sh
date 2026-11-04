#!/usr/bin/env bash
# Tool code (tools/README.md, "The GPU lock"): runs one command under the GPU lock, /tmp/tertius-gpu-measure.lock, which
# every timed run on the development Mac takes. It is the one helper of the lock for every timing tool.
#   bash tools/gpu-lock.sh <log> <command> [arguments]
# The log gets the uptime at the start, the seconds that the run waited for the lock, the command's output and the
# uptime at the end. The exit code is the command's.
#
# The lock is the directory: mkdir takes it, and rmdir releases it. A run that takes it by hand with mkdir follows the
# same rule. The helper removes the lock when the command ends, also when it fails or a signal ends it, and it never
# removes a lock that it did not make.
#
# The waiters take turns in the order in which they came. Each waiter writes a ticket, a file named by its start time
# and its process id, into /tmp/tertius-gpu-measure.queue, and tries mkdir only while its ticket is the oldest ticket of
# a live process. Without the order, a loop of runs that tried every 0.1 s won nearly every release against a waiter
# that tried every 5 s, for more than 20 minutes on 2026-11-03. A run that takes the lock by hand has no ticket, and it
# competes with the oldest waiter only.
set -uo pipefail

log=${1:?usage: gpu-lock.sh <log> <command> [arguments]}
shift
lock=/tmp/tertius-gpu-measure.lock
queue=/tmp/tertius-gpu-measure.queue
mkdir -p "$queue"
ticket=${EPOCHREALTIME/./}-$$
touch "$queue/$ticket"

held=0
trap '[ "$held" = 1 ] && rmdir "$lock"; rm -f "$queue/$ticket"' EXIT
# A signal ends the script through exit, so that the trap above also runs for a killed or timed-out run.
trap 'exit 130' INT
trap 'exit 143' TERM

# The oldest ticket of a live process. It removes the tickets of processes that ended without their trap.
oldest_ticket() {
    local entry
    for entry in $(ls "$queue" | sort); do
        if kill -0 "${entry##*-}" 2> /dev/null; then
            echo "$entry"
            return
        fi
        rm -f "$queue/$entry"
    done
}

started=$EPOCHSECONDS
until [ "$(oldest_ticket)" = "$ticket" ] && mkdir "$lock" 2> /dev/null; do
    sleep 0.1
done
held=1
rm -f "$queue/$ticket"
{
    echo "uptime-start: $(uptime)"
    echo "waited: $((EPOCHSECONDS - started))s"
} > "$log"
"$@" >> "$log" 2>&1
code=$?
echo "uptime-end: $(uptime)" >> "$log"
rmdir "$lock"
held=0
exit "$code"
