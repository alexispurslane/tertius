#!/usr/bin/env bash
# The build tool of scripted models (docs/plans/asset-pipeline.md, "The build tool"). Builds one model folder in
# headless Blender with no MCP server, and prints build.json.
# Usage: tools/blender/build.sh <model folder> --out <folder> [--views] [--device auto|cpu]
# Exit codes: 0 built, 2 bad input, 3 the build failed, 5 Blender is missing, 6 too little free space for the build,
# 130 a signal (SIGINT or SIGTERM) stopped the build.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)

usage() {
    echo "usage: tools/blender/build.sh <model folder> --out <folder> [--views] [--device auto|cpu]" >&2
    exit 2
}

model="" out="" views=() device=auto
while [[ $# -gt 0 ]]; do
    case "$1" in
        --out) [[ $# -ge 2 ]] || usage; out=$2; shift 2 ;;
        --views) views=(--views); shift ;;
        --device) [[ $# -ge 2 && ( $2 == auto || $2 == cpu ) ]] || usage; device=$2; shift 2 ;;
        -*) usage ;;
        *) [[ -z "$model" ]] || usage; model=$1; shift ;;
    esac
done
[[ -n "$model" && -n "$out" ]] || usage
[[ -f "$model/model.json" ]] || { echo "build: $model holds no model.json." >&2; exit 2; }

# Outside the assets shell, run again inside it. Check first that the shell starts, so a failure of Nix is exit
# code 5 and not the exit code of a build.
if [[ -z "${TERTIUS_BLENDER:-}" ]]; then
    if ! nix develop "$root#assets" --command true > /dev/null 2>&1; then
        echo "build: Blender is missing: the assets shell (nix develop .#assets) does not start." >&2
        exit 5
    fi
    exec nix develop "$root#assets" --command bash "$0" "$model" --out "$out" "${views[@]}" --device "$device"
fi

model=$(cd "$model" && pwd)
mkdir -p "$(dirname "$out")"
out="$(cd "$(dirname "$out")" && pwd)/$(basename "$out")"

# Stop before the build can fill the disk. The space that a build needs grows with the size and the number of the
# baked maps (needed_mb). A model.json that the estimate cannot read gets the estimate of no bake, and Blender then
# reports the bad input.
need_mb=$(python3 "$here/needed_mb.py" "$model/model.json")
free_mb=$(df -Pk "$(dirname "$out")" | awk 'NR == 2 { print int($4 / 1024) }')
if [[ "$free_mb" -lt "$need_mb" ]]; then
    echo "build: only $free_mb MB free on the volume of $out. The build needs $need_mb MB." >&2
    exit 6
fi

# A folder with build.json holds an earlier build, which this build replaces. Any other folder with files is kept.
if [[ -d "$out" && -n "$(ls -A "$out")" ]]; then
    if [[ ! -f "$out/build.json" ]]; then
        echo "build: $out holds files and no build.json. Give an empty or new folder." >&2
        exit 2
    fi
    rm -rf "$out"
fi
mkdir -p "$out"

# Blender takes its temporary folder from TMPDIR. A folder of this build alone can be deleted at the end.
tmp=$(mktemp -d "${TMPDIR:-/tmp}/tertius-build.XXXXXX")
blender_pid=""
cleanup() {
    # Blender ignores SIGINT, and after SIGTERM it leaves its temporary folder behind.
    if [[ -n "$blender_pid" ]] && kill -0 "$blender_pid" 2> /dev/null; then
        kill -TERM "$blender_pid" 2> /dev/null || true
        wait "$blender_pid" 2> /dev/null || true
    fi
    rm -rf "$tmp"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# Runs Blender in the background, so a signal reaches the trap at once, and waits for it. Blender's user resource
# folder is a folder of this build too: an add-on that a model script enables, such as MPFB, writes its logs and
# settings there, and never into the user's own Blender folder.
run_blender() {
    TMPDIR="$tmp" BLENDER_USER_RESOURCES="$tmp/user" "$TERTIUS_BLENDER" --background --factory-startup \
        --disable-autoexec --python-exit-code 3 "$@" >> "$out/blender.log" 2>&1 &
    blender_pid=$!
    wait "$blender_pid" || true
    blender_pid=""
}

status_of() {
    python3 -c 'import json, sys; print(json.load(open(sys.argv[1]))["status"])' "$out/build.json"
}

# The first run ends a good build with the status "failed" at the step "export", until the export run replaces it.
export_pending() {
    [[ -f "$out/build.json" ]] && python3 -c '
import json, sys
report = json.load(open(sys.argv[1]))
step = (report.get("error") or {}).get("step")
sys.exit(0 if report["status"] == "failed" and step == "export" else 1)' "$out/build.json"
}

# The first run builds, bakes, saves model.blend and renders the views. The second run opens model.blend with one
# thread and exports model.glb. With more threads, Blender computes the tangents in an order that changes between
# runs, and the .glb files of two builds differ in their last bits.
run_blender --python "$here/build.py" -- "$model" "$out" "${views[@]}" --device "$device"
if export_pending; then
    run_blender --threads 1 "$out/model.blend" --python "$here/build.py" -- --export "$out"
fi

if [[ ! -f "$out/build.json" ]]; then
    echo "build: Blender stopped before it wrote build.json. The last lines of $out/blender.log:" >&2
    tail -20 "$out/blender.log" >&2
    exit 3
fi
cat "$out/build.json"
case "$(status_of)" in
    ok) exit 0 ;;
    badInput) exit 2 ;;
    *) exit 3 ;;
esac
