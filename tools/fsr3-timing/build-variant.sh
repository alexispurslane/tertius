#!/usr/bin/env bash
# Tool code (tools/README.md, "FSR 3.1 timing"): builds the Rendering tests in Release with one variant of AMD's FSR 3.1
# passes, and copies the test build to captures/fsr-builds/<name>. Run it in the dev shell from the worktree root:
#   nix develop --command bash tools/fsr3-timing/build-variant.sh <half> <wave> <name>
# <half> 1 compiles AMD's four 16-bit passes with FFX_HALF=1, as AMD's host does. <wave> 1 leaves the define
# FFX_SPD_NO_WAVE_OPERATIONS out, so that the pyramids use subgroup operations. 0 0 is the engine's own build.
#
# The csproj builds only the engine's modules. So the script builds once, writes the variant's modules over them in
# obj, and builds again: the modules are newer than their sources, so only the C# compile runs and embeds them. At the
# end it touches the csproj and builds again, so that obj and bin hold the engine's modules.
set -euo pipefail

half=${1:?half: 0 or 1}
wave=${2:?wave: 0 or 1}
name=${3:?the name of the copy under captures/fsr-builds}
[ -e "captures/fsr-builds/$name" ] && { echo "captures/fsr-builds/$name exists" >&2; exit 1; }

project=src/Tertius.Rendering/Tertius.Rendering.csproj
tests=tests/Tertius.Rendering.Tests
root=src/Tertius.Rendering/ThirdParty/Fsr3
gpu=$root/sdk/include/FidelityFX/gpu
# MSBuild gives the path with a backslash after obj.
obj=src/Tertius.Rendering/$(dotnet msbuild "$project" -p:Configuration=Release -getProperty:IntermediateOutputPath | tr '\\' /)shaders

# The engine's defines without FFX_HALF, which the loop sets per pass, and without the SPD define when <wave> is 1.
defines=$(dotnet msbuild "$project" -getProperty:Fsr3Defines | sed 's/ -DFFX_HALF=0//')
[ "$wave" = 1 ] && defines=$(echo "$defines" | sed 's/ -DFFX_SPD_NO_WAVE_OPERATIONS=1//')

build() { dotnet build "$tests" -c Release -v q -nologo 2>&1 | tail -2; }

build
for pass in "$root"/sdk/src/backends/vk/shaders/fsr3upscaler/*.glsl; do
    file=$(basename "$pass")
    case $file in *rcas_pass.glsl | *debug_view_pass.glsl) continue ;; esac
    passHalf=0
    case $file in *autogen_reactive* | *prepare_reactivity* | *shading_change_pass* | *accumulate*) passHalf=$half ;; esac
    # shellcheck disable=SC2086 # The defines are separate arguments.
    log=$(glslangValidator -V --target-env vulkan1.3 -S comp -Os -DFFX_HALF=$passHalf $defines -I"$gpu" \
        -I"$gpu/fsr3upscaler" -o "$obj/$file.spv" "$pass") || { echo "$log" >&2; exit 1; }
    spirv-val --target-env vulkan1.3 "$obj/$file.spv"
    echo "$file: $(spirv-dis "$obj/$file.spv" | grep -o 'OpCapability [A-Za-z0-9]*' | sed 's/OpCapability //' | sort -u | tr '\n' ' ')"
done
build
mkdir -p captures/fsr-builds
cp -R "$tests/bin/Release/net11.0" "captures/fsr-builds/$name"

touch "$project"
build
