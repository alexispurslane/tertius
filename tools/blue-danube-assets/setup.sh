#!/usr/bin/env bash
# Sets up the Blue Danube art: fetches the pinned files and converts each model with Blender in the assets dev shell
# (tools/README.md, "The Blue Danube art"). The arguments go to setup.py.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
cd "$repo"
exec nix develop "$repo#assets" --command python3 -I "$here/setup.py" "$@"
