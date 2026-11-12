#!/usr/bin/env bash
cd /Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0
echo "driver: $VK_ICD_FILENAMES"
MTL_DEBUG_LAYER=1 dotnet test --project tests/Tertius.Rendering.Tests -c Checked --no-build -- --filter-not-class 'Tertius.Rendering.Tests.A*' 'Tertius.Rendering.Tests.B*' 'Tertius.Rendering.Tests.C*' 'Tertius.Rendering.Tests.D*' 'Tertius.Rendering.Tests.E*' 'Tertius.Rendering.Tests.F*' 'Tertius.Rendering.Tests.G*' 'Tertius.Rendering.Tests.H*' 'Tertius.Rendering.Tests.I*' 'Tertius.Rendering.Tests.L*' 'Tertius.Rendering.Tests.M*' 'Tertius.Rendering.Tests.O*' 'Tertius.Rendering.Tests.P*'
