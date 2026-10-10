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
