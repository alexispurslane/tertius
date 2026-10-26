# blender-mcp: the official Blender Lab MCP server (https://projects.blender.org/lab/blender_mcp), and its Blender
# add-on. Agents model through it (docs/plans/asset-pipeline.md, "Scripted models").
# The server is pure Python. It needs the Python package mcp below version 2, and the pinned nixpkgs has mcp 1.x.
# The assertion below stops the build if a nixpkgs update brings mcp 2, instead of a server that fails at run time.
{
  lib,
  python3Packages,
  fetchgit,
  fetchurl,
}:

let
  version = "1.0.3";

  # The official release file of the add-on. native/blender-extensions.nix unpacks it into the system extension
  # repository of the assets shell, and tools/blender/bridge.sh enables it with --addons.
  addon = fetchurl {
    url = "https://projects.blender.org/lab/blender_mcp/releases/download/v${version}/mcp-${version}.zip";
    hash = "sha256-p6nagWGSUC5aCiAqOWRE4ma0fY/E90rUaYBIvUMEBwc=";
  };

  # The commit of the tag v1.0.3.
  src = fetchgit {
    url = "https://projects.blender.org/lab/blender_mcp.git";
    rev = "2cea8d566dde07fbac28a61d698909d69724e853";
    hash = "sha256-pYeByO4Oi5eyynsJhGVd1vBWXHvhGn+Y5LGit6Kazlw=";
  };
in
assert lib.assertMsg (lib.versionOlder python3Packages.mcp.version "2")
  "blender-mcp ${version} needs the Python package mcp below 2, and nixpkgs has mcp ${python3Packages.mcp.version}.";
python3Packages.buildPythonApplication {
  pname = "blender-mcp";
  inherit version;
  pyproject = true;

  inherit src;
  # The server is the folder mcp/ of the repository.
  sourceRoot = "${src.name}/mcp";

  build-system = [ python3Packages.setuptools ];
  dependencies = with python3Packages; [
    docutils
    mcp
    pyyaml
  ];
  doCheck = false;

  passthru = { inherit addon; };

  meta = {
    description = "The official Blender Lab MCP server";
    homepage = "https://www.blender.org/lab/mcp-server/";
    license = lib.licenses.gpl3Plus;
    mainProgram = "blender-mcp";
  };
}
