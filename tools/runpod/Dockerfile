# syntax=docker/dockerfile:1.7
# The image of the Runpod serverless GPU worker (tools/runpod/README.md). It holds the dev shell's closure, the source
# of one commit with its Release demo built, and the worker. A job fetches its trees' commits from GitHub and builds
# them in this dev shell, so the image changes only when the dev shell or the worker changes. The handler compares the
# dev shell's files of each fetched commit with the baked ones. The workflow .github/workflows/gpu-image.yml builds it
# from the commit's own files, which `git archive` gives exactly, with the build argument BAKED_COMMIT set to that
# commit. Never build it from a checkout, which holds .git, build outputs and untracked files.
FROM ubuntu:24.04

# The NVIDIA container toolkit then mounts the graphics part of the driver, with its Vulkan driver, and not only CUDA.
ENV NVIDIA_DRIVER_CAPABILITIES=all \
    NVIDIA_VISIBLE_DEVICES=all \
    USER=root \
    LANG=C.UTF-8 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    PATH=/root/.nix-profile/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

# The dev shell. This layer changes only when the flake or native/ changes.
COPY flake.nix flake.lock /opt/tertius-flake/
COPY native /opt/tertius-flake/native
COPY tools/runpod/provision.sh /opt/tertius-flake/provision.sh
RUN bash /opt/tertius-flake/provision.sh /opt/tertius-flake

# The worker's Python with the Runpod SDK, apart from the dev shell. The SDK's version is pinned.
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-venv ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && python3 -m venv /opt/tertius-worker \
    && /opt/tertius-worker/bin/pip install --no-cache-dir runpod==2.2.1

# The source of the commit, with its Release demo built. The build restores the NuGet packages into the image, so a
# job's build finds them there, and it makes sure that the commit compiles on Linux.
ARG BAKED_COMMIT
COPY . /opt/tertius
RUN test -n "$BAKED_COMMIT" \
    && echo "$BAKED_COMMIT" > /opt/tertius/BAKED_COMMIT \
    && cd /opt/tertius \
    && nix develop /nix/var/nix/profiles/tertius-dev --offline --command \
        dotnet build src/Tertius.Demo -c Release

WORKDIR /opt/tertius
CMD ["/opt/tertius-worker/bin/python", "-u", "/opt/tertius/tools/runpod/worker.py"]
