#!/usr/bin/env bash
# Tool code (tools/README.md, "GPU calibration"): counts the work in each probe at three levels, so that a compiler
# that removed or folded work shows. Run it from the worktree root, after the Release build of the test project, the
# builds of probes and kernel-code, and a run of the Vulkan probes with MVK_CONFIG_SHADER_DUMP_DIR=<dump folder>. Run it
# outside the dev shell, whose environment hides Xcode's Metal toolchain from xcrun. It takes spirv-dis from the shell,
# and the binaries from $GPU_CALIBRATION_BIN, by default captures/gpu-calibration/bin:
#   bash tools/gpu-calibration/count.sh <dump folder> <out folder>
# 1. SPIR-V, the glslc output that the engine's device gets: FMAs, samples, loads and loops, by spirv-dis.
# 2. AIR, Apple's intermediate code, compiled by xcrun metal from the native kernels and from MoltenVK's MSL: the calls
#    of fma, of sample and the loads of the device address space, per kernel.
# 3. The native code: the size of the __compute section of each kernel's binary archive, for the native kernels and for
#    MoltenVK's MSL.
set -euo pipefail

dump=${1:?dump folder of MoltenVK}
out=${2:?out folder}
mkdir -p "$out"
spirv=tests/Tertius.Rendering.Tests/obj/Release/net11.0/test-shaders
toolchain=$(dirname "$(xcrun --find metal)")
bin=${GPU_CALIBRATION_BIN:-captures/gpu-calibration/bin}

echo "## SPIR-V (glslc --target-env=vulkan1.3 -O)"
nix develop --command bash -c "for module in $spirv/calibration_*.spv; do spirv-dis \$module -o $out/\$(basename \$module).txt; done"
for module in "$spirv"/calibration_*.spv; do
    text="$out/$(basename "$module").txt"
    printf '%s: Fma %s, ImageSampleExplicitLod %s, OpLoad of uvec4 %s, LoopMerge %s, float-controls modes %s\n' \
        "$(basename "$module")" "$(grep -c 'Fma ' "$text" || true)" "$(grep -c 'OpImageSampleExplicitLod' "$text" || true)" \
        "$(grep -c 'OpLoad %v4uint' "$text" || true)" "$(grep -c 'OpLoopMerge' "$text" || true)" \
        "$(grep -cE 'SignedZeroInfNanPreserve|ContractionOff|FPFastMathDefault|DenormPreserve|RoundingMode' "$text" || true)"
done

air() {
    # Per kernel: the fma calls and their vector width, the sample calls and the loads from device memory.
    xcrun metal -O2 -S -emit-llvm -o "$2" "$1"
    awk '/^define /{name=$0; sub(/.*@/, "", name); sub(/\(.*/, "", name)}
        /call .*@air\.fma\./{fma[name]++; if ($0 ~ /v4f32/) wide[name]++}
        /call .*@air\.sample_texture_2d/{sample[name]++}
        /load .*addrspace\(1\)/{load[name]++}
        /^}/ && name != "" {printf "  %s: fma calls %d (of float4 %d), sample calls %d, device loads %d\n", name, fma[name], wide[name], sample[name], load[name]; name=""}' "$2"
}

echo "## AIR of the native kernels (tools/gpu-calibration/probes.swift)"
$bin/probes dump-msl > "$out/native.metal"
air "$out/native.metal" "$out/native.ll"
echo "## AIR of MoltenVK's MSL"
for msl in "$dump"/*.metal; do
    echo "$(basename "$msl"):"
    air "$msl" "$out/$(basename "$msl" .metal).ll"
done

code_size() {
    "$toolchain/metal-lipo" "$1" -thin applegpu_g13s -output "$1.g13s"
    local size
    size=$("$toolchain/metal-objdump" -h "$1.g13s" | awk '$2 == "__compute" {print $3}')
    printf '  %s: __compute %d bytes\n' "$(basename "$1" .bin)" "0x$size"
}

echo "## Native code of the native kernels"
for kernel in $($bin/kernel-code "$out/native.metal" "$out/native-code"); do code_size "$kernel"; done
echo "## Native code of MoltenVK's MSL"
for msl in "$dump"/*.metal; do
    echo "$(basename "$msl"):"
    for kernel in $($bin/kernel-code "$msl" "$out/$(basename "$msl" .metal)-code"); do code_size "$kernel"; done
done
