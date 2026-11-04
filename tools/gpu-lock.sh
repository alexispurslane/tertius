#!/usr/bin/env bash
# Tool code (tools/README.md, "The GPU lock"): runs one command under the GPU lock, /tmp/tertius-gpu-measure.lock, which
# every timed run on the development Mac takes. It is the one helper of the lock for every timing tool. Take the lock
# only through it.
#   bash tools/gpu-lock.sh <log> <command> [arguments]
# The log gets the uptime at the start, the seconds that the run waited for the lock, the command's output and the
# uptime at the end. The exit code is the command's. TERTIUS_GPU_LOCK and TERTIUS_GPU_QUEUE give other paths for the
# lock and the queue, for the tests (tools/test_gpu_lock.py).
#
# The lock is a directory, and mkdir takes it. Right after its mkdir, the holder writes its record into the file holder
# in the lock: its process id, that process's start time as ps gives it, a random token and a label (the log and the
# command). A plain rmdir of the lock then fails, because the lock is not empty.
#
# The release removes the lock only when the record's token is the run's own, also when the command fails or a signal
# ends it. When the lock vanished, or another run's record is in it, the release removes nothing and writes one line
# into the log, so that the run's timing can be discarded. A removal renames the lock to a name of its own first, so
# that no other run sees a lock that lost its record.
#
# A waiter clears a stale lock: one whose record names a process that no longer runs, or a process id that a later
# process reuses with another start time. A lock without a record is being taken right now, or the helper before
# ter-r88x took it. A waiter never clears it.
#
# The waiters take turns in the order in which they came. Each waiter writes a ticket, a file named by its start time
# and its process id, into /tmp/tertius-gpu-measure.queue, and tries mkdir, or clears a stale lock, only while its
# ticket is the oldest ticket of a live process. Without the order, a loop of runs that tried every 0.1 s won nearly
# every release against a waiter that tried every 5 s, for more than 20 minutes on 2026-11-03. The order also makes the
# clear safe: only the oldest waiter clears, and only the oldest waiter takes a lock that the clear freed.
set -uo pipefail

log=${1:?usage: gpu-lock.sh <log> <command> [arguments]}
shift
lock=${TERTIUS_GPU_LOCK:-/tmp/tertius-gpu-measure.lock}
queue=${TERTIUS_GPU_QUEUE:-/tmp/tertius-gpu-measure.queue}
mkdir -p "$queue"
ticket=${EPOCHREALTIME/./}-$$
touch "$queue/$ticket"
token=$ticket-$SRANDOM$SRANDOM
label="$log: $*"
label=${label//$'\n'/ }

held=0
trap '[ "$held" = 1 ] && release; rm -f "$queue/$ticket"' EXIT
# Until the record is written, a signal only marks the run, so that no signal leaves a lock without a record behind.
signal=
trap 'signal=130' INT
trap 'signal=143' TERM

# The start time of the process $1, with its spaces squeezed, or nothing for a process that does not run. The word
# splitting of the unquoted command squeezes the spaces.
start_of() {
    echo $(LC_ALL=C ps -o lstart= -p "$1" 2> /dev/null)
}

# The record in the lock directory $1, or nothing.
record_of() {
    cat "$1/holder" 2> /dev/null
}

# The field $2 of the record $1.
field() {
    sed -n "s/^$2=//p" <<< "$1"
}

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

# Takes the lock and writes the record into it, or fails while another run holds it. If the record cannot be written,
# it removes the lock that it just made and ends the run.
take() {
    mkdir "$lock" 2> /dev/null || return 1
    if ! printf 'pid=%s\nstart=%s\ntoken=%s\nlabel=%s\n' "$$" "$(start_of $$)" "$token" "$label" > "$lock/holder.new" ||
        ! mv "$lock/holder.new" "$lock/holder"; then
        rm -f "$lock/holder.new"
        rmdir "$lock"
        echo "gpu-lock: cannot write the holder's record into $lock" >&2
        exit 1
    fi
    held=1
}

# Clears the lock if its record names a process that no longer runs with the recorded start time. The renamed lock
# holds the record that the check read, unless a run took the lock by hand since the read. Then it goes back.
clear_if_stale() {
    local record gone=$lock.stale.$ticket
    record=$(record_of "$lock")
    [ -n "$record" ] || return
    [ "$(start_of "$(field "$record" pid)")" = "$(field "$record" start)" ] && return
    mv "$lock" "$gone" 2> /dev/null || return
    if [ "$(record_of "$gone")" = "$record" ]; then
        rm -rf "$gone"
    else
        mv "$gone" "$lock"
    fi
}

# Removes the lock if its record holds this run's token. Otherwise it writes into the log what holds the path now.
release() {
    local record gone=$lock.released.$ticket
    record=$(record_of "$lock")
    if [ "$(field "$record" token)" = "$token" ]; then
        mv "$lock" "$gone" && rm -rf "$gone"
        return
    fi
    local now="it vanished"
    if [ -n "$record" ]; then
        now="it holds the record of $(field "$record" label)"
    elif [ -d "$lock" ]; then
        now="it holds no record"
    fi
    echo "gpu-lock: the lock was not this run's at its release ($now), so it stays. Discard this run's timing." >> "$log"
}

started=$EPOCHSECONDS
while :; do
    [ -n "$signal" ] && exit "$signal"
    if [ "$(oldest_ticket)" = "$ticket" ]; then
        take && break
        clear_if_stale
    fi
    sleep 0.1
done
rm -f "$queue/$ticket"
# A signal from now on ends the script through exit, so that the trap above also releases the lock of a killed or
# timed-out run.
trap 'exit 130' INT
trap 'exit 143' TERM
[ -n "$signal" ] && exit "$signal"
{
    echo "uptime-start: $(uptime)"
    echo "waited: $((EPOCHSECONDS - started))s"
} > "$log"
"$@" >> "$log" 2>&1
code=$?
echo "uptime-end: $(uptime)" >> "$log"
release
held=0
exit "$code"
