#!/usr/bin/env bash
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0
echo "driver: $VK_ICD_FILENAMES"
MTL_DEBUG_LAYER=1 dotnet test --project tests/Tertius.Host.Tests -c Checked --no-build
