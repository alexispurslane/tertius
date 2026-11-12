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
      packages = forAllSystems (
        pkgs:
        {
          joltc-double = pkgs.callPackage ./native/joltc.nix { };
          jscpd = pkgs.callPackage ./native/jscpd.nix { };
          # The reflection rays' blue-noise mask, which Tertius.Rendering embeds (native/stbn-mask.nix).
          stbn-mask = pkgs.callPackage ./native/stbn-mask.nix { };
          # AMD's FSR 3.1 host sources at the pin, for tools/fsr3-golden/ (native/fidelityfx-sdk.nix).
          fidelityfx-sdk = pkgs.callPackage ./native/fidelityfx-sdk.nix { };
          # The official Blender Lab MCP server of the assets shell. Its add-on is passthru.addon.
          blender-mcp = pkgs.callPackage ./native/blender-mcp.nix { };
          # The dev shell's SDK, so that tools/rider.sh can keep a stable link to it (.nix/dotnet-sdk) for Rider.
          dotnet-sdk = pkgs.dotnet-sdk_11;
        }
        # The Mac's patched MoltenVK (native/moltenvk.nix), which the Mac's dev shell makes the default driver.
        // pkgs.lib.optionalAttrs pkgs.stdenv.hostPlatform.isDarwin {
          moltenvk = pkgs.callPackage ./native/moltenvk.nix { };
        }
      );

      devShells = forAllSystems (
        pkgs:
        let
          dotnet = pkgs.dotnet-sdk_11;
          joltc = pkgs.callPackage ./native/joltc.nix { };
          jscpd = pkgs.callPackage ./native/jscpd.nix { };
          stbnMask = pkgs.callPackage ./native/stbn-mask.nix { };
          fidelityfxSdk = pkgs.callPackage ./native/fidelityfx-sdk.nix { };
          blenderMcp = pkgs.callPackage ./native/blender-mcp.nix { };
          mpfb = pkgs.callPackage ./native/mpfb.nix { };
          makehumanAssets = pkgs.callPackage ./native/makehuman-assets.nix { };
          blenderExtensions = pkgs.callPackage ./native/blender-extensions.nix {
            inherit mpfb;
            mcpAddon = blenderMcp.addon;
          };
          blender = pkgs.lib.getExe pkgs.blender;
          # The Mac's patched MoltenVK (native/moltenvk.nix). Only the Mac's parts below read it, so a Linux shell
          # never builds it.
          moltenvk = pkgs.callPackage ./native/moltenvk.nix { };
          vulkanLibs = [
            pkgs.vulkan-loader
          ]
          ++ pkgs.lib.optionals pkgs.stdenv.hostPlatform.isDarwin [ moltenvk ]
          # The host's NVIDIA Vulkan driver (native/host-nvidia-vulkan.sh) needs these, and a container has none of them.
          ++ pkgs.lib.optionals pkgs.stdenv.hostPlatform.isLinux [
            pkgs.libglvnd
            pkgs.libx11
            pkgs.libxext
          ];
          # The attributes of both shells.
          common = {
            packages = [
              dotnet
              pkgs.roslyn-ls
              # The language server of the Claude Code plugin in tools/claude-plugins (started by tools/csharp-ls.sh).
              pkgs.csharp-ls
              pkgs.csharpier
              pkgs.cmake
              pkgs.ninja
              pkgs.git
              # The duplicate-code report, tools/duplicates.sh.
              jscpd
              # The coverage and Stryker helpers in tools/ are Python. The renderer survey (tools/renderer-survey.py)
              # reads its captures with Pillow and measures them with NumPy and SciPy.
              (pkgs.python3.withPackages (python: [
                python.numpy
                python.scipy
                python.pillow
              ]))
              pkgs.shaderc
              pkgs.glslang
              # spirv-val checks every FSR module in the build (src/Tertius.Rendering/ThirdParty/Fsr3/README.md).
              pkgs.spirv-tools
              pkgs.vulkan-headers
              pkgs.vulkan-loader
              pkgs.vulkan-tools
              pkgs.vulkan-validation-layers
              # Headless runs stream their captures into ffmpeg, which encodes them into one video (--video).
              # ffprobe, from the same package, reads the videos in the Host tests.
              pkgs.ffmpeg-headless
            ]
            ++ pkgs.lib.optionals pkgs.stdenv.hostPlatform.isDarwin [
              moltenvk
              # Mesa's KosmicKrisp, the second Vulkan driver of the Mac, which TERTIUS_KOSMICKRISP_ICD names.
              pkgs.mesa
            ];

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
            # The ICD manifest of KosmicKrisp, LunarG's Mesa driver for Apple GPUs. MoltenVK stays the default, and a run
            # picks KosmicKrisp with VK_ICD_FILENAMES=$TERTIUS_KOSMICKRISP_ICD (tools/README.md, "Vulkan drivers").
            TERTIUS_KOSMICKRISP_ICD = pkgs.lib.optionalString pkgs.stdenv.hostPlatform.isDarwin "${pkgs.mesa}/share/vulkan/icd.d/kosmickrisp_mesa_icd.aarch64.json";

            # The repository's git hooks (.githooks/): cleanup, duplicates and build on commit, docs on push.
            # On Linux, the hook also finds the host's NVIDIA Vulkan driver (tools/README.md, "Vulkan drivers").
            shellHook = ''
              if git rev-parse --git-dir > /dev/null 2>&1; then
                git config core.hooksPath .githooks
              fi
            ''
            + pkgs.lib.optionalString pkgs.stdenv.hostPlatform.isLinux ''
              source ${./native/host-nvidia-vulkan.sh}
            '';

            # The Nix-built double-precision Jolt library, which Directory.Build.targets copies next to each output.
            TERTIUS_JOLTC_DIR = "${joltc}/lib";

            # The reflection rays' blue-noise mask (native/stbn-mask.nix), which Tertius.Rendering embeds.
            TERTIUS_STBN_MASK = "${stbnMask}/stbn-mask.bin";

            # AMD's FSR 3.1 host sources at the pin, which tools/fsr3-golden/ builds from.
            TERTIUS_FFX_SDK = "${fidelityfxSdk}";

            # The Vulkan loader, which Directory.Build.targets copies next to each output, so that Silk.NET finds it
            # without DYLD_FALLBACK_LIBRARY_PATH. macOS removes DYLD_* variables in /usr/bin/env and in apps with the
            # hardened runtime, such as Rider, so a test that Rider starts never sees them.
            TERTIUS_VULKAN_DIR = "${pkgs.vulkan-loader}/lib";
          }
          # The patched MoltenVK is the Mac's default driver, and TERTIUS_STOCK_MOLTENVK_ICD names the ICD manifest of
          # the nixpkgs build without the patches, for comparisons (tools/README.md, "The patched MoltenVK"). On Linux
          # the shell sets no VK_ICD_FILENAMES, so a value from outside the shell stays, and the shellHook can set
          # NVIDIA's.
          // pkgs.lib.optionalAttrs pkgs.stdenv.hostPlatform.isDarwin {
            VK_ICD_FILENAMES = "${moltenvk}/share/vulkan/icd.d/MoltenVK_icd.json";
            TERTIUS_STOCK_MOLTENVK_ICD = "${pkgs.moltenvk}/share/vulkan/icd.d/MoltenVK_icd.json";
          };
        in
        {
          default = pkgs.mkShell common;

          # The assets shell: the default shell plus Blender and the Blender MCP server, for scripted models and
          # conversions (docs/plans/asset-pipeline.md, decision 4 and "Scripted models"). Blender finds the MCP add-on
          # and MPFB in the system extension repository of BLENDER_SYSTEM_EXTENSIONS, so nothing is installed.
          assets = pkgs.mkShell (
            common
            // {
              packages = common.packages ++ [
                pkgs.blender
                blenderMcp
              ];
              TERTIUS_BLENDER = blender;
              TERTIUS_BLENDER_VERSION = pkgs.blender.version;
              # The unpacked MPFB add-on, pinned by hash. Model scripts run it through tertius_blender.mpfb.
              TERTIUS_MPFB = "${mpfb}";
              # The MakeHuman system assets without the clothes: skins, eyes, eyebrows, eyelashes, teeth, tongue, hair.
              TERTIUS_MAKEHUMAN_ASSETS = "${makehumanAssets}";
              # Blender's system extension repository: MPFB and the MCP add-on, read from the Nix store. Every Blender
              # of the shell finds them as bl_ext.system.mpfb and bl_ext.system.mcp.
              BLENDER_SYSTEM_EXTENSIONS = "${blenderExtensions}";
              # blender-mcp runs Blender for some of its tools, and it reaches the bridge at this host and port.
              # tools/blender/bridge.sh starts the bridge on the same port.
              BLENDER_PATH = blender;
              BLENDER_MCP_HOST = "localhost";
              BLENDER_MCP_PORT = "9876";
            }
          );
        }
      );

      formatter = forAllSystems (pkgs: pkgs.nixfmt);
    };
}
