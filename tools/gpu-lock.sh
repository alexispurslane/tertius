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
# in the lock: its process id, that process's start time as ps gives it in UTC, a random token and a label (the log and
# the command). A plain rmdir of the lock then fails, because the lock is not empty. A run that cannot write a complete
# record removes its new lock and ends.
#
# The release removes the lock only when the record's token is the run's own, also when the command fails or a signal
# ends it. When the lock vanished, or another run's record is in it, the release removes nothing and writes one line
# into the log, so that the run's timing can be discarded. A removal renames the lock to a name of its own first, so
# that no other run sees a lock that lost its record. INT, TERM and HUP only mark the run during the wait, the take and
# the release, so that no signal stops the take or the release halfway.
#
# A waiter clears a stale lock: one whose record names a process that no longer runs, or a process id that a later
# process reuses with another start time. When ps gives nothing for a live process, the waiter does not know, and it
# waits. A lock without a record, or with a record that lacks its pid, start or token, is being taken right now, or the
# helper before ter-r88x took it. A waiter never clears it.
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

# A signal only marks the run, so that it cannot stop the take or the release halfway.
signal=
defer_signals() {
    trap 'signal=130' INT
    trap 'signal=143' TERM
    trap 'signal=129' HUP
}
# A signal ends the script through exit, so that the EXIT trap also releases the lock of a killed or timed-out run.
end_on_signals() {
    trap 'exit 130' INT
    trap 'exit 143' TERM
    trap 'exit 129' HUP
}

held=0
trap '[ "$held" = 1 ] && release; rm -f "$queue/$ticket"' EXIT
defer_signals

# The start time of the process $1 in UTC, with its spaces squeezed, or nothing for a process that does not run or when
# ps fails. TZ and LC_ALL make every run write and compare the same text. The word splitting of the unquoted command
# squeezes the spaces.
start_of() {
    echo $(TZ=UTC0 LC_ALL=C ps -o lstart= -p "$1" 2> /dev/null)
}

# Reads the record in the lock directory $1 into record_pid, record_start, record_token and record_label, with builtins
# only, because a waiter reads it every 0.1 s. Fails unless the record has its pid, start and token.
read_record() {
    local key value
    record_pid= record_start= record_token= record_label=
    while IFS='=' read -r key value; do
        case $key in
            pid) record_pid=$value ;;
            start) record_start=$value ;;
            token) record_token=$value ;;
            label) record_label=$value ;;
        esac
    done 2> /dev/null < "$1/holder"
    [ -n "$record_pid" ] && [ -n "$record_start" ] && [ -n "$record_token" ]
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

# Takes the lock and writes the record into it, or fails while another run holds it. If no complete record can be
# written, it removes the lock that it just made and ends the run.
take() {
    mkdir "$lock" 2> /dev/null || return 1
    local start
    start=$(start_of $$)
    if [ -z "$start" ] ||
        ! printf 'pid=%s\nstart=%s\ntoken=%s\nlabel=%s\n' "$$" "$start" "$token" "$label" > "$lock/holder.new" ||
        ! mv "$lock/holder.new" "$lock/holder"; then
        rm -f "$lock/holder.new"
        rmdir "$lock"
        echo "gpu-lock: cannot write the holder's record into $lock" >&2
        exit 1
    fi
    held=1
}

# Clears the lock if its record names a process that no longer runs, or a live process that ps gives another start
# time. The check of the start time forks ps, so it runs at most once a second. The renamed lock holds the token that
# the check read, unless a run took the lock since the read. Then it goes back.
checked=
clear_if_stale() {
    read_record "$lock" || return
    if kill -0 "$record_pid" 2> /dev/null; then
        [ "$checked" = "$EPOCHSECONDS" ] && return
        checked=$EPOCHSECONDS
        local now
        now=$(start_of "$record_pid")
        [ -n "$now" ] && [ "$now" != "$record_start" ] || return
    fi
    local stale=$record_token gone=$lock.stale.$ticket
    mv "$lock" "$gone" 2> /dev/null || return
    if read_record "$gone" && [ "$record_token" = "$stale" ]; then
        rm -rf "$gone"
    else
        mv "$gone" "$lock"
    fi
}

# Removes the lock if its record holds this run's token. Otherwise it writes into the log what holds the path now.
# The run holds no lock afterwards, and no signal stops the release halfway.
release() {
    defer_signals
    held=0
    local gone=$lock.released.$ticket
    read_record "$lock"
    if [ "$record_token" = "$token" ]; then
        mv "$lock" "$gone" && rm -rf "$gone"
        return
    fi
    local now="it vanished"
    if [ -n "$record_token" ]; then
        now="it holds the record of $record_label"
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
end_on_signals
[ -n "$signal" ] && exit "$signal"
{
    echo "uptime-start: $(uptime)"
    echo "waited: $((EPOCHSECONDS - started))s"
} > "$log"
"$@" >> "$log" 2>&1
code=$?
echo "uptime-end: $(uptime)" >> "$log"
release
exit "$code"
