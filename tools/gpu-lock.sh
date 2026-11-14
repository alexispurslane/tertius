#!/usr/bin/env bash
# Tool code (tools/README.md, "The GPU lock"): runs one command under the GPU lock, /tmp/tertius-gpu-measure.lock, which
# every timed run on the development Mac takes. It is the one helper of the lock for every timing tool. Take the lock
# only through it.
#   bash tools/gpu-lock.sh [--shared] <log> <command> [arguments]
# A run without --shared holds the GPU alone, and every timed run takes it so. A run with --shared holds it beside up to
# TERTIUS_GPU_SHARED_CAP - 1 other shared runs (3 runs by default) and never beside a run that holds it alone. Only
# untimed work, such as stills and films for image metrics, takes the shared mode. The log gets the uptime at the start,
# the seconds that the run waited for the lock, the command's output and the uptime at the end. The command runs with
# TERTIUS_GPU_LOCK_HELD set to exclusive or shared. The exit code is the command's. TERTIUS_GPU_LOCK and TERTIUS_GPU_QUEUE give other paths for the lock and the queue, for the tests
# (tools/test_gpu_lock.py).
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
# The record and the liveness rule are those of every machine lock (tools/README.md, "The record of a machine lock").
# tools/stryker/run.py holds the same rule in Python, and tools/test_gpu_lock.py tests that each lock judges the other's
# record as the other does. A waiter clears a stale lock: one whose record names a process that is gone. A lock without
# a record, or with a record that lacks its pid, start or token, is being taken right now, or the helper before ter-r88x
# took it. A waiter never clears it.
#
# The waiters take turns in the order in which they came. Each waiter writes a ticket, a file named by its start time
# and its process id, into /tmp/tertius-gpu-measure.queue. The ticket holds the waiter's record, so that the liveness
# rule judges a waiter as it judges a holder. A waiter tries mkdir, or clears a stale lock, only while its ticket is the
# oldest ticket of a live waiter. Without the order, a loop of runs that tried every 0.1 s won nearly every release
# against a waiter that tried every 5 s, for more than 20 minutes on 2026-11-03. The order also makes the clear safe:
# only the oldest waiter clears, and only the oldest waiter takes a lock that the clear freed.
#
# The shared mode keeps the same lock directory. Shared holders put no holder file into it, but the folder shared, with
# one record file per holder, named by the holder's ticket. The first shared run makes the lock and the folder, and a
# later one adds its record while the folder holds fewer records than the cap. The release removes the run's own record
# and then the folder with rmdir, which fails while the folder holds a record. Only the run whose rmdir removed the
# folder removes the lock, also with rmdir. So of several runs that end at once, exactly one removes the lock. A shared
# run also waits for its ticket to be the oldest, whatever the mode of the older tickets. So a stream of shared runs
# cannot pass a waiting exclusive run, and the shared runs at the head of the queue start one after the other, 0.1 s
# apart. Every helper since 93e46dd7f fails its mkdir while shared runs hold the lock, so it waits for them.
set -uo pipefail
shopt -s nullglob

shared=0
if [ "${1-}" = --shared ]; then
    shared=1
    shift
fi
log=${1:?usage: gpu-lock.sh [--shared] <log> <command> [arguments]}
shift
lock=${TERTIUS_GPU_LOCK:-/tmp/tertius-gpu-measure.lock}
queue=${TERTIUS_GPU_QUEUE:-/tmp/tertius-gpu-measure.queue}
cap=${TERTIUS_GPU_SHARED_CAP-3}
if [ "$shared" = 1 ] && [[ ! $cap =~ ^[0-9]+$ || $cap -lt 1 ]]; then
    echo "gpu-lock: TERTIUS_GPU_SHARED_CAP must be a whole number of at least 1, not '$cap'" >&2
    exit 2
fi
ticket=${EPOCHREALTIME/./}-$$
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

# Reads the record in the file $1 into record_pid, record_start, record_token and record_label, with builtins only,
# because a waiter reads it every 0.1 s. These globals hold the record that it read last, and own_record and own_start
# hold this run's own. Fails unless the record has its pid in ASCII digits, its start and its token.
# Each line is split at its first =, and a line without = is no line of the record. read drops the NUL bytes and a last
# line without its newline.
read_record() {
    local line
    record_pid= record_start= record_token= record_label=
    while IFS= read -r line; do
        [[ $line == *=* ]] || continue
        case ${line%%=*} in
            pid) record_pid=${line#*=} ;;
            start) record_start=${line#*=} ;;
            token) record_token=${line#*=} ;;
            label) record_label=${line#*=} ;;
        esac
    done 2> /dev/null < "$1"
    [[ -n $record_pid && $record_pid != *[!0123456789]* && -n $record_start && -n $record_token ]]
}

# Succeeds when the process $1, which started at $2, is gone: kill -0 finds no such process, or ps gives a start time
# that is not empty and differs from $2. A permission error of kill -0 or an empty answer of ps counts as alive. With an
# empty $2, kill -0 alone judges. ps forks, so a waiter runs it at most once a second for each process, and in the other
# seconds a process that kill -0 finds counts as alive. The second is per process, so that the check of one live shared
# holder cannot keep ps from another one.
declare -A checked=()
gone() {
    local now
    if ! kill -0 "$1" 2> /dev/null; then
        # Only the message tells no such process from a permission error. Its fork runs only when kill -0 fails.
        [[ $(LC_ALL=C; kill -0 "$1" 2>&1) == *"No such process"* ]] && return 0
    fi
    [ -n "$2" ] && [ "${checked[$1]-}" != "$EPOCHSECONDS" ] || return 1
    checked[$1]=$EPOCHSECONDS
    now=$(start_of "$1")
    [ -n "$now" ] && [ "$now" != "$2" ]
}

# Sets oldest to the oldest ticket of a live waiter. It removes the tickets of waiters that are gone, which ended without
# their trap. The ticket of a helper before ter-s4fg is empty, so kill -0 alone judges it.
oldest_ticket() {
    local entry
    oldest=
    for entry in $(ls "$queue" | sort); do
        if [ "$entry" = "$ticket" ]; then
            oldest=$entry
            return
        fi
        read_record "$queue/$entry"
        if ! gone "${entry##*-}" "$record_start"; then
            oldest=$entry
            return
        fi
        rm -f "$queue/$entry"
    done
}

# Takes the lock and writes the record into it, or fails while another run holds it. If no complete record can be
# written, it removes the lock that it just made and ends the run.
take() {
    mkdir "$lock" 2> /dev/null || return 1
    if [ -z "$own_start" ] || ! printf %s "$own_record" > "$lock/holder.new" \
            || ! mv "$lock/holder.new" "$lock/holder"; then
        rm -f "$lock/holder.new"
        rmdir "$lock"
        cannot_write
    fi
    held=1
}

cannot_write() {
    echo "gpu-lock: cannot write the holder's record into $lock" >&2
    exit 1
}

# Takes the lock in the shared mode: it makes the lock and its folder shared, or joins the shared holders while they are
# fewer than the cap. It fails while an exclusive run holds the lock, while the cap is reached, and while the lock has
# neither a holder file nor the folder: a run takes it at this moment, or a helper of 93e46dd7f holds it. If the last
# shared holder removes the folder before this run's record is in it, the write fails, and the run tries again. If no
# complete record can be written into a folder that stays, the run removes what it made and ends.
take_shared() {
    local into=$lock/shared made=0
    if mkdir "$lock" 2> /dev/null; then
        made=1
        mkdir "$into" || { rmdir "$lock"; cannot_write; }
    else
        local holders=("$into"/*)
        [ -d "$into" ] && [ "${#holders[@]}" -lt "$cap" ] || return 1
    fi
    if [ -z "$own_start" ] || ! printf %s "$own_record" > "$into/$ticket.new" 2> /dev/null \
            || ! mv "$into/$ticket.new" "$into/$ticket"; then
        rm -f "$into/$ticket.new"
        if [ "$made" = 1 ]; then
            rmdir "$into" && rmdir "$lock"
        elif [ ! -d "$into" ]; then
            return 1
        fi
        cannot_write
    fi
    held=1
}

# Removes the folder shared and then the lock, if the folder holds no record. Only the run whose rmdir removed the
# folder removes the lock. Nobody writes into a lock without a holder file or the folder, so that lock is empty.
remove_empty_shared() {
    rmdir "$lock/shared" 2> /dev/null && rmdir "$lock"
}

# Clears the lock if its record names a process that is gone. The renamed lock holds the token that the check read,
# unless a run took the lock since the read. Then it goes back. In a shared lock, it removes the record of each shared
# holder that is gone, and the lock when no record stays. A record file holds one holder, so its removal needs no
# rename.
clear_if_stale() {
    if [ -d "$lock/shared" ]; then
        local entry
        for entry in "$lock/shared"/*; do
            read_record "$entry" && gone "$record_pid" "$record_start" && rm -f "$entry"
        done
        remove_empty_shared
        return
    fi
    read_record "$lock/holder" && gone "$record_pid" "$record_start" || return
    local stale=$record_token cleared=$lock.stale.$ticket
    mv "$lock" "$cleared" 2> /dev/null || return
    if read_record "$cleared/holder" && [ "$record_token" = "$stale" ]; then
        rm -rf "$cleared"
    else
        mv "$cleared" "$lock"
    fi
}

# Removes the lock if its record holds this run's token. A shared run removes its own record, and the lock if no other
# record stays. Otherwise it writes into the log what holds the path now. The run holds no lock afterwards, and no
# signal stops the release halfway.
release() {
    defer_signals
    held=0
    local released=$lock.released.$ticket own=$lock/holder
    [ "$shared" = 1 ] && own=$lock/shared/$ticket
    read_record "$own"
    if [ "$record_token" = "$token" ]; then
        if [ "$shared" = 1 ]; then
            rm -f "$own"
            remove_empty_shared
        else
            mv "$lock" "$released" && rm -rf "$released"
        fi
        return
    fi
    local now="it vanished"
    read_record "$lock/holder"
    if [ -n "$record_token" ]; then
        now="it holds the record of $record_label"
    elif [ -d "$lock/shared" ]; then
        now="it holds no record of this run"
    elif [ -d "$lock" ]; then
        now="it holds no record"
    fi
    echo "gpu-lock: the lock was not this run's at its release ($now), so it stays. Discard this run's timing." >> "$log"
}

# The record of this run, which its ticket holds while it waits and the lock holds while it runs.
own_start=$(start_of $$)
printf -v own_record 'pid=%s\nstart=%s\ntoken=%s\nlabel=%s\n' "$$" "$own_start" "$token" "$label"
mkdir -p "$queue"
printf %s "$own_record" > "$queue/$ticket"
started=$EPOCHSECONDS
while :; do
    [ -n "$signal" ] && exit "$signal"
    oldest_ticket
    if [ "$oldest" = "$ticket" ]; then
        if [ "$shared" = 1 ]; then
            take_shared && break
        else
            take && break
        fi
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
    [ "$shared" = 1 ] && echo "mode: shared, with up to $cap shared runs at once"
} > "$log"
# The command learns the mode that it holds, so that a GPU test run inside it does not wait for this run's own lock
# (ticket ter-zoof, tests/Tertius.Testing/GpuTestLock.cs).
mode=exclusive
[ "$shared" = 1 ] && mode=shared
TERTIUS_GPU_LOCK_HELD=$mode "$@" >> "$log" 2>&1
code=$?
echo "uptime-end: $(uptime)" >> "$log"
release
exit "$code"
