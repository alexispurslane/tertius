#!/usr/bin/env bash
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0
echo "driver: $VK_ICD_FILENAMES"
dotnet test --solution Tertius.slnx -c Release --no-build
