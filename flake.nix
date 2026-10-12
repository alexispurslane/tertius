{
  description = "Tertius: an engine for immersive sims and space sims";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";

  outputs =
    { nixpkgs, ... }:
    let
      systems = [
        "aarch64-darwin"
        "x86_64-linux"
        "aarch64-linux"
      ];
      forAllSystems = f: nixpkgs.lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});
    in
    {
      packages = forAllSystems (pkgs: {
        joltc-double = pkgs.callPackage ./native/joltc.nix { };
      });

      devShells = forAllSystems (
        pkgs:
        let
          dotnet = pkgs.dotnet-sdk_11;
          joltc = pkgs.callPackage ./native/joltc.nix { };
          vulkanLibs = [
            pkgs.vulkan-loader
          ]
          ++ pkgs.lib.optionals pkgs.stdenv.hostPlatform.isDarwin [ pkgs.moltenvk ];
        in
        {
          default = pkgs.mkShell {
            packages = [
              dotnet
              pkgs.roslyn-ls
              pkgs.csharpier
              pkgs.cmake
              pkgs.ninja
              pkgs.git
              # The coverage and Stryker helpers in tools/ are Python.
              pkgs.python3
              pkgs.shaderc
              pkgs.glslang
              pkgs.vulkan-headers
              pkgs.vulkan-loader
              pkgs.vulkan-tools
              pkgs.vulkan-validation-layers
            ]
            ++ pkgs.lib.optionals pkgs.stdenv.hostPlatform.isDarwin [ pkgs.moltenvk ];

            # The wrapped SDK links its install tree to $out/share/dotnet.
            DOTNET_ROOT = "${dotnet}/share/dotnet";
            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1";
            DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK = "1";
            MSBUILDTERMINALLOGGER = "false";

            # Silk.NET opens libvulkan by name, so the loader must be on the search path.
            # The loader then finds MoltenVK through its ICD manifest.
            DYLD_FALLBACK_LIBRARY_PATH = pkgs.lib.makeLibraryPath vulkanLibs;
            LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath vulkanLibs;
            VK_LAYER_PATH = "${pkgs.vulkan-validation-layers}/share/vulkan/explicit_layer.d";
            VK_ICD_FILENAMES = pkgs.lib.optionalString pkgs.stdenv.hostPlatform.isDarwin "${pkgs.moltenvk}/share/vulkan/icd.d/MoltenVK_icd.json";

            # The Nix-built double-precision Jolt library. MSBuild does not read this yet.
            TERTIUS_JOLTC_DIR = "${joltc}/lib";
          };
        }
      );

      formatter = forAllSystems (pkgs: pkgs.nixfmt);
    };
}
