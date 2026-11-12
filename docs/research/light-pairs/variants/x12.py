# The cascade lookup's code stays in the shader, but no pixel runs it: what its presence alone costs.
import sys
p = sys.argv[1] + '/src/Tertius.Rendering/Shaders/lighting.glsl'
s = open(p).read()
a = "        if (visibility > 0.0)\n            visibility *= cascadeShadow(set, position, geometricNormal, sinTheta);\n"
assert s.count(a) == 1
s = s.replace(a, "        if (visibility > 0.0 && frame.renderParams.w < -1.0)\n            visibility *= cascadeShadow(set, position, geometricNormal, sinTheta);\n")
open(p, 'w').write(s)
