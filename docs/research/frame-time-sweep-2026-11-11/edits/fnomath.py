# The diffuse filter's taps keep their reads, but the weight takes no pow, no exp and no sqrt.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
p = root + common.SH + 'diffuse_filter.glsl'
common.sub(p, "    float normal = pow(max(dot(a.normal, b.normal), 0.0), NormalPower);",
              "    float normal = max(dot(a.normal, b.normal), 0.0);")
common.sub(p, "    float deviation = LuminanceDeviations * sqrt(a.variance + b.variance) + LeastDeviation;\n    float light = exp(-abs(a.luminance - b.luminance) / deviation);",
              "    float deviation = LuminanceDeviations * (a.variance + b.variance) + LeastDeviation;\n    float light = max(1.0 - abs(a.luminance - b.luminance) / deviation, 0.0);")
