#!/usr/bin/env bash
# Tool code (tools/README.md, "Linux NVIDIA machines"): makes an Ubuntu container ready to build and run Tertius, as
# root. Both the bring-up on a Runpod pod and tools/runpod/Dockerfile run it, so a pod tests the image's recipe.
#   bash tools/runpod/provision.sh <flake folder> [<source folder>]
# It installs a pinned single-user Nix, then builds or downloads the dev shell of <flake folder> and keeps it as the
# profile /nix/var/nix/profiles/tertius-dev, so that a later `nix develop /nix/var/nix/profiles/tertius-dev` downloads
# nothing. The flake folder needs only flake.nix, flake.lock and native/. With a source folder, it also restores the
# NuGet packages of Tertius.slnx into /root/.nuget/packages. A second run skips the steps that are done.
set -euo pipefail

usage="usage: tools/runpod/provision.sh <flake folder> [<source folder>]"
flake=$(realpath "${1:?$usage}")
source_folder=${2:+$(realpath "$2")}
nix_version=2.34.8
# The SHA-256 of the installer of $nix_version. The installer holds the hash of the Nix tarball that it downloads.
nix_install_sha256=96c10e102c88809dd9ec0bee89200c4a51eae4c9f6d8698c26b16788d131e078
profile=/nix/var/nix/profiles/tertius-dev

if [[ "$(id -u)" -ne 0 ]]; then
    echo "Run provision.sh as root: it installs packages and Nix." >&2
    exit 1
fi
export USER=${USER:-root}

missing=()
for pair in curl:curl xz:xz-utils git:git rsync:rsync; do
    command -v "${pair%%:*}" > /dev/null || missing+=("${pair#*:}")
done
if [[ ${#missing[@]} -gt 0 ]]; then
    apt-get update -q
    DEBIAN_FRONTEND=noninteractive apt-get install -yq --no-install-recommends ca-certificates "${missing[@]}"
fi

# A container has no nixbld users and no user namespaces for the build sandbox, so root builds without either.
if [[ ! -x /root/.nix-profile/bin/nix ]]; then
    mkdir -p /etc/nix
    cat > /etc/nix/nix.conf <<'CONF'
experimental-features = nix-command flakes
build-users-group =
sandbox = false
max-jobs = auto
CONF
    curl -fsSL -o /tmp/nix-install "https://releases.nixos.org/nix/nix-$nix_version/install"
    echo "$nix_install_sha256  /tmp/nix-install" | sha256sum -c --quiet -
    # The installer creates /nix with sudo, which a plain image such as ubuntu:24.04 lacks. As root, make it here.
    mkdir -m 0755 -p /nix
    sh /tmp/nix-install --no-daemon --no-channel-add --no-modify-profile < /dev/null
    rm /tmp/nix-install
fi
# A command over ssh starts no login shell, so nix must be on the default PATH.
ln -sfn /root/.nix-profile/bin/nix /usr/local/bin/nix
# shellcheck source=/dev/null
. /root/.nix-profile/etc/profile.d/nix.sh

# Without a sandbox, a build's HOME is the real folder /homeless-shelter, and Nix refuses to start a build while it
# exists. The cargo vendoring of jscpd (native/jscpd.nix) writes $HOME/.cargo, so the build after it fails. A container
# allows no user namespaces for a sandbox. So the step removes the folder and runs again: each run keeps what it built.
start=$(date +%s)
for attempt in 1 2 3; do
    rm -rf /homeless-shelter
    if nix develop "path:$flake" --profile "$profile" --command true; then
        break
    fi
    if [[ "$attempt" -eq 3 ]]; then
        exit 1
    fi
done
rm -rf /homeless-shelter
# `nix develop <profile>` takes its interactive bash from the registry's nixpkgs, which is the floating channel
# nixpkgs-unstable by default, so its first start downloads the channel. Pin the registry to the flake's locked nixpkgs,
# and start the profile once, so a job downloads nothing.
rev=$(nix eval --raw --impure --expr "(builtins.fromJSON (builtins.readFile $flake/flake.lock)).nodes.nixpkgs.locked.rev")
nix registry add nixpkgs "github:NixOS/nixpkgs/$rev"
nix develop "$profile" --command true
echo "provision: the dev shell took $(($(date +%s) - start)) s."

if [[ -n "$source_folder" ]]; then
    start=$(date +%s)
    nix develop "$profile" --command bash -c 'cd "$1" && dotnet restore Tertius.slnx -v quiet' restore "$source_folder"
    echo "provision: the NuGet restore took $(($(date +%s) - start)) s."
fi
