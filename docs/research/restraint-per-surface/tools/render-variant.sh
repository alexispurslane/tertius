#!/usr/bin/env bash
# Research tool (issue #42): renders the pictures of one prototype restraint on the checked-out engine.
#   nix develop --command bash docs/research/restraint-per-surface/tools/render-variant.sh <variant> <gain> <set>...
# <variant> is RESTRAINT_VARIANT of volume.glsl (0 today, 1 Design 1, 2 Design 2, 3 hybrid), <gain> is COLOR_GAIN
# with a decimal point (1.0, 4.0). Each set is one of:
#   probe     the mixed probe and the red-only probe at both poses, with the volume and HDR readback
#   lamps     the orange-lamp and the warm-white scenes at both poses
#   orange    the orange-lamp scene at both poses
#   crossing  the crossing film of the mixed probe (the camera path of #22's crossing.mp4), EV 0
#   deck      the full-art hangar deck (tools/deck-still.json) at 1280 x 720 and the automatic exposure: three captures
#             a, b and c of a frozen world, with exposure.json
#   glow      set 1b: three deck views beside the floor's glow (glow-back, glow-up, glow-along), like deck
# Writes captures/issue42/v<variant>-g<gain>/<name>/. Needs no change under src/, and restores it on exit.
# Each finished case leaves the file .done in its folder, and a later run skips it, so a take can resume after a restart.
# Before each render it waits while any other Tertius.Demo render or Tertius.Rendering.Tests run exists on the machine
# (one GPU job at a time).
set -euo pipefail
variant=$1
gain=$2
shift 2
tools="$(cd "$(dirname "$0")" && pwd)"
scope="$PWD/captures/issue42"
out="$scope/v$variant-g$gain"
content=${TERTIUS_CONTENT_ROOT:-/Users/alexispurslane/Development/tertius/content}
glsl=src/Tertius.Rendering/Shaders/volume.glsl
if [[ -n $(git status --short --untracked-files=no -- src) ]]; then
    echo "render-variant.sh needs a checkout with no change under src/."
    exit 1
fi
restore() {
    git restore --worktree -- src/Tertius.Demo/DemoScenes.cs src/Tertius.Rendering/VulkanRenderer.cs "$glsl"
    git restore --worktree -- src/Tertius.Demo/DemoScenes.StationExit.cs
    rm -f src/Tertius.Demo/DemoScenes.Issue22Probe.cs src/Tertius.Demo/DemoScenes.Issue42Probe.cs
}
trap restore EXIT
python3 -I "$tools/prepare-render.py"
python3 -I - "$glsl" "$variant" "$gain" <<'EOF'
import sys
from pathlib import Path
path, variant, gain = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
text = path.read_text()
for old, new in (("#define RESTRAINT_VARIANT 0", f"#define RESTRAINT_VARIANT {variant}"),
                 ("#define COLOR_GAIN 1.0", f"#define COLOR_GAIN {gain}")):
    assert text.count(old) == 1, old
    text = text.replace(old, new)
path.write_text(text)
EOF
dotnet build src/Tertius.Demo -c Release -v quiet
mkdir -p "$out"

gpu_free() {
    while pgrep -f 'Tertius.Demo.*--headless|Tertius.Rendering.Tests' > /dev/null; do
        echo "$(date +%T) waiting: another GPU job runs"
        sleep 30
    done
}
render() {
    local dir=$1
    local script=$2
    shift 2
    [[ -f "$dir/.done" ]] && return 0
    rm -rf "$dir"
    mkdir -p "$dir"
    gpu_free
    env TERTIUS_LIGHT_VOLUME=1 TERTIUS_ISSUE22_VOLUME_DUMP="$dir/volume.bin" TERTIUS_ISSUE22_PIXEL_DUMP=1 \
        bash tools/gpu-lock.sh --shared "$dir/render.log" \
        dotnet run --project src/Tertius.Demo -c Release --no-build -- \
        --script "$script" --headless --out "$dir" "$@"
    touch "$dir/.done"
}
probe=(--exposure 0 --environment-map off --upscaling off --screen-trace off)
for set in "$@"; do
    case "$set" in
    probe)
        for view in outside across red-outside red-across; do
            render "$out/$view" "$PWD/captures/issue22/$view.json" "${probe[@]}"
        done
        ;;
    lamps | orange)
        names=(orange)
        [[ "$set" == lamps ]] && names+=(warm)
        for name in "${names[@]}"; do
            for pose in outside across; do
                render "$out/$name-$pose" "$scope/$name-$pose.json" "${probe[@]}"
            done
        done
        ;;
    crossing)
        [[ -f "$out/crossing/.done" ]] && continue
        rm -rf "$out/crossing" "$out/crossing.mkv"
        mkdir -p "$out/crossing"
        gpu_free
        env TERTIUS_LIGHT_VOLUME=1 bash tools/gpu-lock.sh --shared "$out/crossing/render.log" \
            dotnet run --project src/Tertius.Demo -c Release --no-build -- \
            --script "$PWD/captures/issue22/crossing.json" --headless --out "$out/crossing" \
            --video "$out/crossing.mkv" --fps 60 "${probe[@]}"
        touch "$out/crossing/.done"
        ;;
    deck)
        TERTIUS_CONTENT_ROOT="$content" render "$out/deck" "$tools/deck-still.json" --size 1280x720
        ;;
    glow)
        # Set 1b: the three deck views beside the floor's glow, at the automatic exposure, or at the fixed exposure
        # GLOW_EXPOSURE (stops) into glow-<view>-fixed. GLOW_VIEWS can name fewer views.
        for view in ${GLOW_VIEWS:-glow-back glow-up glow-along}; do
            if [[ -n "${GLOW_EXPOSURE:-}" ]]; then
                TERTIUS_CONTENT_ROOT="$content" render "$out/$view-fixed" "$scope/$view.json" --size 1280x720 \
                    --exposure "$GLOW_EXPOSURE"
            else
                TERTIUS_CONTENT_ROOT="$content" render "$out/$view" "$scope/$view.json" --size 1280x720
            fi
        done
        ;;
    *)
        echo "unknown set $set"
        exit 1
        ;;
    esac
done
echo "rendered $out: $*"
