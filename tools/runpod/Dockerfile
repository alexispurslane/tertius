# syntax=docker/dockerfile:1.7
# The image of the Runpod serverless GPU worker (tools/README.md, "Linux NVIDIA machines"). It holds the dev shell's
# closure and the NuGet packages, so a job downloads no Nix package and no NuGet package at its start. Build it from
# the repository root with BuildKit, which reads tools/runpod/Dockerfile.dockerignore:
#   docker build --platform linux/amd64 -f tools/runpod/Dockerfile -t <image> .
# The job brings its own source. The image keeps no source folder.
FROM ubuntu:24.04

# The NVIDIA container toolkit then mounts the graphics part of the driver, with its Vulkan driver, and not only CUDA.
ENV NVIDIA_DRIVER_CAPABILITIES=all \
    NVIDIA_VISIBLE_DEVICES=all \
    USER=root \
    PATH=/root/.nix-profile/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

# The dev shell. This layer changes only when the flake or native/ changes.
COPY flake.nix flake.lock /opt/tertius-flake/
COPY native /opt/tertius-flake/native
COPY tools/runpod/provision.sh /opt/tertius-flake/provision.sh
RUN bash /opt/tertius-flake/provision.sh /opt/tertius-flake

# The NuGet packages. The source is a bind mount, so it adds no layer, and the restore's obj folders stay out.
RUN --mount=type=bind,target=/tmp/tertius-src,rw \
    bash /opt/tertius-flake/provision.sh /opt/tertius-flake /tmp/tertius-src

WORKDIR /root
CMD ["bash"]
