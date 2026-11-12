# Timing ablation: the transparent pass keeps its material sample, its discard and its blend, but computes no light.
# What it still pays is the floor of the pass.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
common.sub(root + common.SH + 'transparent.frag',
    "lobesRadiance(shading, lobes, exposed(s.emissive, exposure), V, ShadowMapsMainView, exposure),",
    "exposed(s.emissive, exposure) + lobes.diffuseColor * 1e-6,")
