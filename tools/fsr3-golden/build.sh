#!/usr/bin/env bash
# Tool code (tools/README.md, "FSR 3.1 golden values"): builds golden.cpp with AMD's host code of FSR 3.1.4 and writes
# the golden values that Fsr3HostTests holds. Run it in the dev shell, which gives AMD's sources at the pin as
# TERTIUS_FFX_SDK (native/fidelityfx-sdk.nix):
#   nix develop --command bash tools/fsr3-golden/build.sh
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
sdk="${TERTIUS_FFX_SDK:?Run this in the dev shell, which sets TERTIUS_FFX_SDK.}"
out="$root/tests/Tertius.Rendering.Tests/Fsr3Golden.json"
build="$(mktemp -d "${TMPDIR:-/tmp}/fsr3-golden.XXXXXX")"
trap 'rm -rf "$build"' EXIT

# AMD's sources stay unchanged: compat.h supplies the Windows names that they use.
c++ -std=c++17 -O1 -w -fshort-wchar \
    -include "$here/compat.h" \
    -I "$sdk/sdk/include" \
    -I "$sdk/sdk/src/shared" \
    -I "$sdk/sdk/src/components/fsr3upscaler" \
    "$here/golden.cpp" \
    "$sdk/sdk/src/components/fsr3upscaler/ffx_fsr3upscaler.cpp" \
    "$sdk/sdk/src/shared/ffx_object_management.cpp" \
    "$sdk/sdk/src/shared/ffx_assert.cpp" \
    "$sdk/sdk/src/shared/ffx_message.cpp" \
    -o "$build/golden"

"$build/golden" > "$build/golden.json"
python3 -I -m json.tool --indent 1 "$build/golden.json" > "$out"
echo "Wrote $out"
