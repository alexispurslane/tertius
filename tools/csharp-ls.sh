#!/usr/bin/env bash
# Starts csharp-ls (the C# language server) inside the dev shell, for the Claude Code plugin in tools/claude-plugins.
# stdout carries the LSP protocol, so nothing here may print to it.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
# csharp-ls targets .NET 10, but the dev shell's DOTNET_ROOT holds only .NET 11, so the host must roll forward.
exec nix develop "$root" --command env DOTNET_ROLL_FORWARD=Major csharp-ls --solution Tertius.slnx
