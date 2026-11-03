#!/usr/bin/env bash
# Tool code (tools/README.md, "FSR 3.1 golden values"): compares the pinned FSR 3.1 source,
# src/Tertius.Rendering/ThirdParty/Fsr3, with AMD's tag v1.1.4, which the dev shell gives as TERTIUS_FFX_SDK
# (native/fidelityfx-sdk.nix). It passes when the only differences are those of pin.diff: the two declarations and the
# line of AMD's 3.1.6 fix that ThirdParty/Fsr3/README.md records. Run it in the dev shell:
#   nix develop --command bash tools/fsr3-golden/check-pin.sh
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
pin="$(cd "$here/../../src/Tertius.Rendering/ThirdParty/Fsr3" && pwd)"
sdk="${TERTIUS_FFX_SDK:?Run this in the dev shell, which sets TERTIUS_FFX_SDK.}"

differences() {
    cd "$pin"
    find . -type f ! -name README.md | LC_ALL=C sort | while read -r file; do
        diff -u --label "amd/${file#./}" --label "pin/${file#./}" "$sdk/$file" "$file" 2>&1 || true
    done
}

diff -u "$here/pin.diff" <(differences)
echo "The pin equals AMD's v1.1.4 apart from the declarations and the fix of pin.diff."
