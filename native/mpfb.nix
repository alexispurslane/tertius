# MPFB 2.0.18: the MakeHuman add-on for Blender, unpacked. Scripted models of people run its code inside the build,
# through tools/blender/tertius_blender/mpfb.py: its sliders, its base mesh and targets, its rigs and its fitting of
# the MakeHuman body-part assets (docs/plans/asset-pipeline.md, "Humans from MPFB"). Nothing installs it into the
# user's Blender: native/blender-extensions.nix links this folder as the system extension "mpfb", and a run enables
# it for itself only. The assets shell gives the folder as TERTIUS_MPFB.
# Source: the Blender extensions platform, https://extensions.blender.org/add-ons/mpfb/ (45,956,992 bytes).
# License: the add-on code is GPL-3.0-or-later. The MakeHuman base mesh, targets and rig weights are CC0.
{
  fetchurl,
  runCommand,
  unzip,
}:

let
  version = "2.0.18";
  zip = fetchurl {
    url = "https://extensions.blender.org/download/sha256:c075b3e9b94b8e5556a5f03f4653602d02ea306de08126e8f55ed6dee28a2dad/add-on-mpfb-v${version}.zip";
    # sha256 c075b3e9b94b8e5556a5f03f4653602d02ea306de08126e8f55ed6dee28a2dad
    hash = "sha256-wHWz6blLjlVWpfA/RlNgLQLqMG3ggSbo9V7W3uKKLa0=";
  };
in
runCommand "mpfb-${version}"
  {
    nativeBuildInputs = [ unzip ];
    passthru = { inherit zip version; };
  }
  ''
    mkdir -p $out
    unzip -q ${zip} -d $out
  ''
