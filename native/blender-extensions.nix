# The system extension repository of the assets shell's Blender: a folder "system" that holds the MPFB add-on and
# the add-on of the Blender MCP server. The assets shell gives the folder as BLENDER_SYSTEM_EXTENSIONS, so every
# Blender of the shell finds both add-ons in its repository "system", as the modules bl_ext.system.mpfb and
# bl_ext.system.mcp. Nothing is installed into the user's Blender: Blender reads the repository from the Nix store,
# and a run enables an add-on for itself only (the bridge with --addons, a model script through
# tools/blender/tertius_blender/mpfb.py).
{
  runCommand,
  unzip,
  mpfb,
  mcpAddon,
}:

runCommand "tertius-blender-extensions"
  {
    nativeBuildInputs = [ unzip ];
  }
  ''
    mkdir -p $out/system
    ln -s ${mpfb} $out/system/mpfb
    unzip -q ${mcpAddon} -d $out/system/mcp
  ''
