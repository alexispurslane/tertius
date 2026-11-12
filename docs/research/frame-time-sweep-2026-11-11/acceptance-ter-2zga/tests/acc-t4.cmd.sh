#!/usr/bin/env bash
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0
echo "driver: $VK_ICD_FILENAMES"
echo "commit: $(git rev-parse --short HEAD)"
dotnet test --project tests/Tertius.Rendering.Tests -c Checked --no-build -- --filter-class 'Tertius.Rendering.Tests.HitLight*' 'Tertius.Rendering.Tests.ScreenTrace*' 'Tertius.Rendering.Tests.DescriptorLayouts*' 'Tertius.Rendering.Tests.Reflection*'
