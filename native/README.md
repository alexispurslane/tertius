# Native libraries

Tertius uses Jolt Physics through joltc, a C API for Jolt. Tertius needs the double-precision build of joltc. This build stores positions as 64-bit numbers, so large worlds keep their precision. The library file is `libjoltc_double.dylib` on macOS and `libjoltc_double.so` on Linux.

You can get the library in two ways.

## Build it with Nix

The file `joltc.nix` describes the build. It fetches joltc at commit `886e088` and Jolt Physics at tag `v5.6.0`. A hash pins each source, so every build uses the same code.

Run this command from the repository root:

```sh
nix build .#joltc-double
```

Nix puts the library in `result/lib` and the header in `result/include`. The dev shell also sets `TERTIUS_JOLTC_DIR` to the `lib` folder of this build.

## Build it with the script

The script `build-joltc.sh` clones both sources with git and builds them with CMake. Run it inside the dev shell:

```sh
nix develop --command native/build-joltc.sh
```

The script writes the library to `native/osx-arm64` or `native/linux-x64`. The .NET build copies the library from there. The script uses the same source versions and the same CMake options as `joltc.nix`. It does not check hashes, so the Nix build is the reproducible one.

## MoltenVK on the Mac

The file `moltenvk.nix` builds the Mac's Vulkan driver. It is MoltenVK from nixpkgs with the two patches of this folder:

- `moltenvk-execution-dependencies.patch` is upstream's fix of MoltenVK issue 2805.
- `moltenvk-keep-dispatch-encoder.patch` keeps a compute encoder open across a pipeline barrier to the compute stage.

The Mac's dev shell makes this build the default driver. Build it alone with this command:

```sh
nix build .#moltenvk
```

[tools/README.md](../tools/README.md), "The patched MoltenVK", gives the reason, the ordering rules and the switches back to the stock behavior.

## The blue-noise mask

The file `stbn-mask.nix` builds the reflection rays' blue-noise mask. It compiles `stbn.c` and runs it once, which takes about a minute. The program writes the same 2 MiB on every machine. The mask is a build output, so git does not keep it (docs/plans/asset-pipeline.md, decision 21). The dev shell names it in `TERTIUS_STBN_MASK`, and `Tertius.Rendering` embeds it. ARCHITECTURE.md, "Randomness and determinism", describes the mask. Build it alone with this command:

```sh
nix build .#stbn-mask
```
