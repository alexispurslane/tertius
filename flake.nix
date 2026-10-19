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
        # The dev shell's SDK, so that tools/rider.sh can keep a stable link to it (.nix/dotnet-sdk) for Rider.
        dotnet-sdk = pkgs.dotnet-sdk_11;
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
              # Headless runs stream their captures into ffmpeg, which encodes them into one video (--video).
              # ffprobe, from the same package, reads the videos in the Host tests.
              pkgs.ffmpeg-headless
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

            # The Nix-built double-precision Jolt library, which Directory.Build.targets copies next to each output.
            TERTIUS_JOLTC_DIR = "${joltc}/lib";

            # The Vulkan loader, which Directory.Build.targets copies next to each output, so that Silk.NET finds it
            # without DYLD_FALLBACK_LIBRARY_PATH. macOS removes DYLD_* variables in /usr/bin/env and in apps with the
            # hardened runtime, such as Rider, so a test that Rider starts never sees them.
            TERTIUS_VULKAN_DIR = "${pkgs.vulkan-loader}/lib";
          };
        }
      );

      formatter = forAllSystems (pkgs: pkgs.nixfmt);
    };
}
