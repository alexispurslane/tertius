# Sourced by the dev shell's shellHook on Linux (flake.nix). It makes the host's NVIDIA Vulkan driver visible to the
# Nix-built Vulkan loader, for a container with the NVIDIA container toolkit (NVIDIA_DRIVER_CAPABILITIES=all) or a
# desktop with NVIDIA's driver.
#
# The Nix loader does not search /usr/share/vulkan/icd.d, where the toolkit can put nvidia_icd.json, and the manifest
# names its library as the bare libGLX_nvidia.so.0. Nix's glibc does not search /usr/lib/x86_64-linux-gnu, so dlopen
# finds that library only through LD_LIBRARY_PATH. The folder of the system libraries cannot go on LD_LIBRARY_PATH,
# because its libstdc++, libz and others would replace the Nix builds that dotnet and the tools link. So the script
# links only NVIDIA's libraries into a folder of its own and puts that folder first.
#
# A VK_ICD_FILENAMES from outside the shell wins, and a host without NVIDIA's manifest changes nothing.

tertius_host_nvidia_vulkan() {
    local icd="" candidate dir="" folder lib links
    if [ -n "${VK_ICD_FILENAMES:-}" ]; then
        return 0
    fi
    for candidate in /etc/vulkan/icd.d/nvidia_icd.json /usr/share/vulkan/icd.d/nvidia_icd.json; do
        if [ -f "$candidate" ]; then
            icd=$candidate
            break
        fi
    done
    if [ -z "$icd" ]; then
        return 0
    fi
    for folder in /usr/lib/x86_64-linux-gnu /usr/lib/aarch64-linux-gnu /usr/lib64 /usr/lib; do
        if [ -e "$folder/libGLX_nvidia.so.0" ]; then
            dir=$folder
            break
        fi
    done
    if [ -z "$dir" ]; then
        echo "tertius: $icd exists, but no system library folder holds libGLX_nvidia.so.0. The container lacks the" \
            "graphics part of NVIDIA's driver: set NVIDIA_DRIVER_CAPABILITIES=all, or use another host." >&2
        return 0
    fi
    links="${XDG_RUNTIME_DIR:-/tmp}/tertius-nvidia-$(id -u)"
    mkdir -p "$links"
    for lib in "$dir"/libGLX_nvidia.so* "$dir"/libEGL_nvidia.so* "$dir"/libnvidia-*.so* "$dir"/libcuda.so*; do
        if [ -e "$lib" ]; then
            ln -sfn "$lib" "$links/"
        fi
    done
    export VK_ICD_FILENAMES="$icd"
    export LD_LIBRARY_PATH="$links${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
}

tertius_host_nvidia_vulkan
unset -f tertius_host_nvidia_vulkan
