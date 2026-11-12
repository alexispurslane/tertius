# A timing bound: the specular accumulation never tries the reflected place.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
p = root + common.SH + 'accumulate_temporal.comp'
common.sub(p, "    bool tryReflected = specular && hasSample && roughness < parameters.reflectedPlaceBelow;",
              "    bool tryReflected = false;")
