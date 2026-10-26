# The MakeHuman system assets, CC0: the skins, eyes, eyebrows, eyelashes, teeth, tongue and hair that MPFB puts on a
# human. Scripted models load them through MPFB's own code (tools/blender/tertius_blender/mpfb.py), which finds them
# as MPFB's "second root" of asset folders. The assets shell gives the folder as TERTIUS_MAKEHUMAN_ASSETS.
#
# The pack also holds clothes, shoes, hats and proxy meshes. This derivation keeps none of them: every garment and
# every piece of equipment of a Tertius model comes from the model's own scripts (docs/plans/asset-pipeline.md,
# decision 27). It also leaves out the two skins named "special_suit" and the toon skin.
# The pack is an input of the build tool, like Blender, and not a third-party source of a content package
# (docs/plans/asset-pipeline.md, decision 26 and "Humans from MPFB"): no asset id names it, and Nix pins its bytes.
# Source: the MakeHuman community, https://static.makehumancommunity.org/assets/assetpacks/makehuman_system_assets.html
# (280,737,770 bytes, last modified 2024-04-14). License: CC0, every asset of the pack (its packs/*.json and the
# header of each file say so).
{
  fetchurl,
  runCommand,
  unzip,
}:

let
  zip = fetchurl {
    # The two mirrors that the pack's page names. The first was about ten times faster on 2026-10-26.
    urls = [
      "https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip"
      "https://files2.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip"
    ];
    # sha256 b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107
    hash = "sha256-tUISeo4lVHx8KcGfLR0q25pmTIA5bs1pQJXbyAKKAQc=";
  };
in
runCommand "makehuman-system-assets"
  {
    nativeBuildInputs = [ unzip ];
    passthru = { inherit zip; };
  }
  ''
    mkdir unpacked
    unzip -q ${zip} -d unpacked
    mkdir -p $out
    for kind in skins eyes eyebrows eyelashes teeth tongue hair; do
      cp -R unpacked/$kind $out/$kind
    done
    rm -rf $out/skins/*special_suit* $out/skins/toon*
  ''
