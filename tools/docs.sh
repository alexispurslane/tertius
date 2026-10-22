#!/usr/bin/env bash
# Builds the docs site with DocFX: the API reference from the doc comments, and the Markdown docs. See tools/README.md.
set -euo pipefail

usage() {
    cat >&2 <<'USAGE'
Usage: tools/docs.sh [-s] [-p <port>] [-n]
  -s         Serve the site at http://localhost:<port> after the build. Stop it with Ctrl-C.
  -p <port>  The port for -s. The default is 8080.
  -n         Skip the Release build of the solution and use the assemblies that are there.
USAGE
    exit 2
}

serve=false
port=8080
build=true
while getopts "sp:nh" opt; do
    case "$opt" in
        s) serve=true ;;
        p) port=$OPTARG ;;
        n) build=false ;;
        *) usage ;;
    esac
done

cd "$(git rev-parse --show-toplevel)"

if ! command -v dotnet > /dev/null; then
    echo "tools/docs.sh: dotnet is not on the PATH. Run the script in the dev shell: nix develop --command bash tools/docs.sh" >&2
    exit 2
fi

# DocFX reads the compiled assemblies and the XML documentation files beside them, not the source.
if $build; then
    dotnet build Tertius.slnx -c Release -v quiet -nologo
fi
dotnet tool restore > /dev/null

# A stale API folder keeps the pages of types that no longer exist.
rm -rf obj/docfx artifacts/docs
dotnet docfx docfx.json

echo "Site: artifacts/docs/index.html" >&2
if $serve; then
    dotnet docfx serve artifacts/docs --port "$port"
fi
