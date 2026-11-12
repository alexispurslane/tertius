#!/usr/bin/env bash
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0
echo "driver: $VK_ICD_FILENAMES"
echo "commit: $(git rev-parse --short HEAD)"
dotnet test --solution Tertius.slnx -c Checked --no-build
