#!/usr/bin/env bash
# Starts JetBrains Rider inside the dev shell, so that Rider, its builds, its test runs and its debug sessions use the
# dev shell's .NET SDK and see its variables (TERTIUS_JOLTC_DIR, TERTIUS_VULKAN_DIR, VK_ICD_FILENAMES, VK_LAYER_PATH).
# It also keeps .nix/dotnet-sdk, a stable link to that SDK and a garbage-collector root for it, which Rider's
# ".NET CLI executable path" setting names (see tools/README.md).
#
# Tool code: it never ships.
#
# Usage, from anywhere: bash tools/rider.sh
# It starts Rider EAP by default, because the stable Rider 2026.2 does not understand C# 15 unions and marks every
# pattern match on a union as an error. Set RIDER to start another Rider, for example RIDER=/Applications/Rider.app.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
rider="${RIDER:-/Applications/Rider EAP.app}/Contents/MacOS/rider"

if [[ ! -x "$rider" ]]; then
  echo "Rider is not at $rider. Set RIDER to the Rider app bundle." >&2
  exit 1
fi

# A running Rider opens the folder in its own process, with its own environment, and this script's variables are lost.
if pgrep -f "$rider" > /dev/null; then
  echo "Rider is running. Quit it first (Rider > Quit Rider), then run this script again." >&2
  exit 1
fi

cd "$root"
mkdir -p .nix
nix build .#dotnet-sdk --out-link .nix/dotnet-sdk
echo "SDK: $root/.nix/dotnet-sdk/share/dotnet/dotnet"

# Rider keeps running after the terminal closes. Its output goes to .nix/rider.log.
nohup nix develop --command "$rider" "$root" > .nix/rider.log 2>&1 &
disown
echo "Rider is starting. Its output goes to .nix/rider.log."
