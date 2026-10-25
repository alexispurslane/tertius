#!/usr/bin/env bash
# Builds libjoltc_double (Jolt with double-precision positions) for this machine.
# Run it inside the dev shell: nix develop --command native/build-joltc.sh
set -euo pipefail

JOLTC_REV=886e088675bae3a086f8318c7803f8ee962c2f2c   # amerkoleci/joltc, matches JoltPhysicsSharp 2.23.0
JOLT_TAG=v5.6.0                                     # jrouwe/JoltPhysics, the version joltc pins

here="$(cd "$(dirname "$0")" && pwd)"
work="$here/build"
mkdir -p "$work"

if [ ! -d "$work/joltc" ]; then
  git clone --quiet https://github.com/amerkoleci/joltc "$work/joltc"
fi
git -C "$work/joltc" fetch --quiet origin "$JOLTC_REV" || true
git -C "$work/joltc" checkout --quiet --force "$JOLTC_REV"
# The same patches as native/joltc.nix: one joltc temp allocator per calling thread, and a lock for the map of systems.
git -C "$work/joltc" apply "$here/joltc-thread-temp-allocator.patch"
git -C "$work/joltc" apply "$here/joltc-system-map-lock.patch"

if [ ! -d "$work/JoltPhysics" ]; then
  git clone --quiet --depth 1 --branch "$JOLT_TAG" https://github.com/jrouwe/JoltPhysics "$work/JoltPhysics"
fi

case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) rid=osx-arm64; ext=dylib; extra=(-DCMAKE_OSX_ARCHITECTURES=arm64) ;;
  Linux-x86_64) rid=linux-x64; ext=so; extra=() ;;
  *) echo "unsupported platform: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac

cmake -S "$work/joltc" -B "$work/out-$rid" -G Ninja \
  -DCMAKE_BUILD_TYPE=Distribution \
  -DDOUBLE_PRECISION=ON \
  -DJPH_BUILD_SHARED=ON \
  -DJPH_SAMPLES=OFF \
  -DJPH_TESTS=OFF \
  -DJPH_USE_DX12=OFF -DJPH_USE_VK=OFF -DJPH_USE_MTL=OFF \
  -DJOLT_PHYSICS_ROOT="$work/JoltPhysics" \
  "${extra[@]}"
cmake --build "$work/out-$rid" -j

mkdir -p "$here/$rid"
cp "$work/out-$rid/lib/libjoltc_double.$ext" "$here/$rid/"
echo "built $here/$rid/libjoltc_double.$ext"
