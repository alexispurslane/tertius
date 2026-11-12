# A rectangle's response without its shine: the diffuse edge sum alone.
import sys
p = sys.argv[1]; s = open(p).read()
a = "    terms.specular = specularWeight(lobes) * (cutCosine(lobe) / PI);\n    return terms;\n"
assert s.count(a) == 1
s = s.replace(a, "    terms.specular = vec3(0.0);\n    return terms;\n")
open(p, "w").write(s)
