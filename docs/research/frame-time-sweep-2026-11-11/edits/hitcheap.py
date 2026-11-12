# Timing proxy of a hit light image: a ray's hit reads its light with one image read, instead of the hit pixel's material,
# its two reprojections, eight history taps and both direct lights. The light is wrong (the direct diffuse light alone).
import sys, re; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]; common.fixexp(root)
p = root + common.SH + 'screen_trace.glsl'
s = open(p).read()
start = s.index('vec3 hitLightOf(ivec2 q, uint kind)\n{')
end = s.index('    return min(light, vec3(MaxRadiance));\n}\n', start) + len('    return min(light, vec3(MaxRadiance));\n}\n')
s = s[:start] + 'vec3 hitLightOf(ivec2 q, uint kind)\n{\n    return min(imageLoad(directDiffuseLight, q).rgb, vec3(MaxRadiance));\n}\n' + s[end:]
open(p, 'w').write(s)
