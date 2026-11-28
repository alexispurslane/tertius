# Diagnostic tool code. helmet-pair.sh <worktree> <out folder> [script.json]
# Renders the 1:41 cockpit film (tools/cockpit.json, 120 frames) at the fixed exposure 6.0043726 twice from the demo that
# <worktree> built in Release: with the normal helmet into <out>/normal and with the black helmet into <out>/black.
# Frame 60 of each also writes the volume dump and the scene-linear HDR target (volume60.bin, volume60.bin.hdr).
# Then it prints the helmet's light per region (helmet_map.py) and in scene-linear values (hdr_panel.py), and writes
# <out>/helmet-mean-normal.png, helmet-mean-black.png and helmet-helmet-x16.png.
# The worktree needs tools/diagnostics.patch applied (the black-helmet recolor, the dumps and the composite debug modes,
# all off unless their environment variables are set), then: nix develop --command dotnet build src/Tertius.Demo -c Release
# Each film takes the shared GPU lock and about 2 minutes.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../../.." && pwd)
wt=$(cd "$1" && pwd); out=$2; script=${3:-$here/cockpit.json}
mkdir -p "$out"; out=$(cd "$out" && pwd)
export TERTIUS_CONTENT_ROOT=${TERTIUS_CONTENT_ROOT:-/Users/alexispurslane/Development/tertius/content}
# EXPOSURE gives other stops; EXPOSURE=auto lets the exposure settle. ONLY=normal or ONLY=black renders one film.
exposure="--exposure ${EXPOSURE:-6.0043726}"; [ "${EXPOSURE:-}" = auto ] && exposure=""
film() {
  local name=$1; shift
  mkdir -p "$out/$name"
  env TERTIUS_DEBUG_VOLUME_FRAME=60 TERTIUS_DEBUG_VOLUME_DUMP="$out/$name/volume60.bin" "$@" \
    nix develop "$repo" --command bash "$repo/tools/gpu-lock.sh" --shared "$out/$name/lock.log" \
    "$wt/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo" --script "$script" --headless --out "$out/$name" \
    $exposure > "$out/$name/render.log" 2>&1
}
[ "${ONLY:-normal}" = black ] || film normal
[ "${ONLY:-black}" = normal ] || film black TERTIUS_DEBUG_RECOLOR_BYTES=1637884 TERTIUS_DEBUG_RECOLOR_MODE=black
[ -n "${ONLY:-}" ] && exit 0
nix develop "$repo" --command bash -c "python3 '$here/helmet_map.py' '$out/normal' '$out/black' '$out/helmet' && \
  python3 '$here/hdr_panel.py' '$out/normal/volume60.bin.hdr' '$out/black/volume60.bin.hdr' && \
  python3 -I '$here/helmet_buckets.py' '$out/normal/volume60.bin' '$out/black/volume60.bin' | tail -3"
