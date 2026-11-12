# The reflection rays' spatiotemporal blue-noise mask: the output of native/stbn.c, which writes the same 2 MiB on
# every machine. A build output stays out of git (docs/plans/asset-pipeline.md, decision 21), and the store keeps it
# once per machine and per image of the GPU workbench. The dev shell names it in TERTIUS_STBN_MASK, and
# Tertius.Rendering embeds it.
{ lib, stdenv }:

stdenv.mkDerivation {
  pname = "stbn-mask";
  version = "1";

  src = lib.fileset.toSource {
    root = ./.;
    fileset = ./stbn.c;
  };

  buildPhase = ''
    runHook preBuild
    $CC -O2 -o stbn stbn.c -lm
    ./stbn stbn-mask.bin
    runHook postBuild
  '';

  installPhase = ''
    runHook preInstall
    install -Dm644 stbn-mask.bin $out/stbn-mask.bin
    runHook postInstall
  '';

  meta = {
    description = "The STBN mask of the screen tracer's reflection rays: 128 x 128 cells over 64 frames";
    platforms = lib.platforms.unix;
  };
}
