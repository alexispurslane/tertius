# The reference of the reflections: no pixel shares its block's reflection ray, so every pixel traces its own in every draw.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.envexp(root)
common.sub(root + common.SH + 'screen_trace_plan.comp',
    "    fit = fit && (kind != KindReflection || insideLobe(p, c));", "    fit = fit && kind != KindReflection;")
