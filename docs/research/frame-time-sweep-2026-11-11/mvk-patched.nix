# The MoltenVK of the flake with the sweep's one patch: a barrier to the compute stage keeps an open compute encoder.
let
  flake = builtins.getFlake "/Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0";
  pkgs = flake.inputs.nixpkgs.legacyPackages.aarch64-darwin;
in
pkgs.moltenvk.overrideAttrs (old: {
  patches = (old.patches or [ ]) ++ [ /private/tmp/tertius-sweep-20261111/mvk-keep-dispatch-encoder.patch ];
})
