#!/usr/bin/env bash
# Installs the Blender MCP add-on into Blender's user extensions and enables it. Run it once per machine.
# The add-on file comes from a fixed-output Nix fetch (native/blender-mcp.nix), so its bytes are pinned by hash.
# If version 1.0.3 is already installed and enabled, the script changes nothing.
# Usage: tools/blender/setup.sh
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
if [[ -z "${TERTIUS_BLENDER:-}" ]]; then
    exec nix develop "$root#assets" --command bash "$0" "$@"
fi

want=1.0.3

# Prints "<installed version or none> <enabled: True or False>" from the user preferences of Blender.
installed() {
    "$TERTIUS_BLENDER" --background --python-exit-code 1 --python-expr '
import os, re, bpy
manifest = bpy.utils.user_resource("EXTENSIONS", path="user_default/mcp/blender_manifest.toml")
version = "none"
if os.path.exists(manifest):
    with open(manifest, encoding="utf-8") as fh:
        found = re.search(r"^version\s*=\s*\"([^\"]+)\"", fh.read(), re.M)
    version = found.group(1) if found else "unknown"
print("TERTIUS-MCP", version, "bl_ext.user_default.mcp" in bpy.context.preferences.addons)
' 2>/dev/null | sed -n 's/^TERTIUS-MCP //p'
}

state=$(installed)
if [[ "$state" == "$want True" ]]; then
    echo "setup: the MCP add-on $want is installed and enabled."
    exit 0
fi

echo "setup: found '$state'. Installing $TERTIUS_BLENDER_MCP_ADDON."
# The Blender of nixpkgs has no Python module requests, so Blender's extension add-on prints a traceback while it
# starts. The install works all the same, and the check below proves it.
"$TERTIUS_BLENDER" --command extension install-file --repo user_default --enable "$TERTIUS_BLENDER_MCP_ADDON" || true

state=$(installed)
if [[ "$state" != "$want True" ]]; then
    echo "setup: the install did not take: found '$state'." >&2
    exit 1
fi
echo "setup: the MCP add-on $want is installed and enabled."
