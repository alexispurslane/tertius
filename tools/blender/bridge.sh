#!/usr/bin/env bash
# Starts or stops the bridge: a headless Blender that runs the MCP add-on and listens on localhost:$BLENDER_MCP_PORT.
# The MCP server of .mcp.json sends each tool call to it. Run tools/blender/setup.sh once first.
# Usage: tools/blender/bridge.sh start|stop|status
#   start   starts the bridge in the background and waits until its socket accepts connections
#   stop    stops it with SIGTERM, waits for it to exit, and deletes its temporary folder
#   status  prints "running <pid> <checkout>" or "stopped"
# One bridge serves the machine, because every MCP server connects to one port. The bridge belongs to the checkout or
# worktree that started it. A start or a stop from another checkout stops with exit code 1 and names the owner.
# Blender ignores Ctrl+C and SIGINT, and after SIGTERM it leaves its temporary folder behind. So always stop the
# bridge with this script when the work is done. A running bridge holds memory and slows GPU work on the machine.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
if [[ -z "${TERTIUS_BLENDER:-}" ]]; then
    exec nix develop "$root#assets" --command bash "$0" "$@"
fi

# The bridge's own folder: its process id, its log and its temporary folder. One bridge serves one port on the
# machine, so the folder is per user and per port. TMPDIR does not work here: nix develop sets a new one each time.
state="${XDG_STATE_HOME:-$HOME/.local/state}/tertius/blender-bridge-$BLENDER_MCP_PORT"
pid_file="$state/pid"
root_file="$state/checkout"

# The process id of the running bridge. After a reboot or a crash, the pid file can name a process that is not the
# bridge, so the command line of the process must be the one that start ran.
running_pid() {
    [[ -f "$pid_file" ]] || return 1
    local pid command
    pid=$(cat "$pid_file")
    command=$(ps -p "$pid" -o command= 2> /dev/null) || return 1
    [[ "$command" == *"--command blender_mcp"*"--port $BLENDER_MCP_PORT"* ]] || return 1
    echo "$pid"
}

owner() {
    cat "$root_file" 2> /dev/null || echo "an unknown checkout"
}

# Stops with exit code 1 when the running bridge belongs to another checkout.
require_owner() {
    if [[ "$(owner)" != "$root" ]]; then
        echo "bridge: the running bridge, pid $1, belongs to $(owner). This checkout is $root. Wait until its" \
            "work ends, or ask its owner to stop it." >&2
        exit 1
    fi
}

port_open() {
    python3 -c "import socket, sys; socket.create_connection(('$BLENDER_MCP_HOST', int(sys.argv[1])), 1).close()" \
        "$BLENDER_MCP_PORT" 2> /dev/null
}

start() {
    if pid=$(running_pid); then
        require_owner "$pid"
        echo "bridge: already running, pid $pid, port $BLENDER_MCP_PORT."
        return 0
    fi
    if port_open; then
        echo "bridge: another process listens on port $BLENDER_MCP_PORT." >&2
        return 1
    fi
    rm -rf "$state"
    mkdir -p "$state/tmp"
    # Blender takes its temporary folder from TMPDIR, so stop can delete it.
    TMPDIR="$state/tmp" nohup "$TERTIUS_BLENDER" --background --online-mode --command blender_mcp \
        --host "$BLENDER_MCP_HOST" --port "$BLENDER_MCP_PORT" > "$state/bridge.log" 2>&1 &
    echo $! > "$pid_file"
    echo "$root" > "$root_file"
    for _ in $(seq 1 120); do
        if port_open; then
            echo "bridge: running, pid $(cat "$pid_file"), port $BLENDER_MCP_PORT, log $state/bridge.log."
            return 0
        fi
        if ! running_pid > /dev/null; then
            echo "bridge: Blender exited. The last lines of $state/bridge.log:" >&2
            tail -20 "$state/bridge.log" >&2
            return 1
        fi
        sleep 0.5
    done
    echo "bridge: the socket did not open in 60 s. Stopping Blender." >&2
    stop
    return 1
}

stop() {
    if pid=$(running_pid); then
        require_owner "$pid"
        kill -TERM "$pid"
        for _ in $(seq 1 60); do
            kill -0 "$pid" 2> /dev/null || break
            sleep 0.5
        done
        if kill -0 "$pid" 2> /dev/null; then
            kill -KILL "$pid"
        fi
        echo "bridge: stopped pid $pid."
    else
        echo "bridge: not running."
    fi
    rm -rf "$state"
}

case "${1:-}" in
    start) start ;;
    stop) stop ;;
    status)
        if pid=$(running_pid); then echo "running $pid $(owner)"; else echo "stopped"; fi
        ;;
    *)
        echo "usage: tools/blender/bridge.sh start|stop|status" >&2
        exit 2
        ;;
esac
