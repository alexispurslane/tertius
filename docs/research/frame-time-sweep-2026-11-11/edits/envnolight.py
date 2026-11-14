# Timing ablation: an environment face shades no light, only the glow. The difference from main is the cost of the light
# loop over every light in the faces' fragments. The image of the map is wrong, so this is a bound, not a candidate.
import sys; sys.path.insert(0, '/private/tmp/tertius-sweep-20261111/edits'); import common
root = sys.argv[1]
common.sub(root + '/src/Tertius.Rendering/Shaders/environment_face.frag',
    "    vec3 radiance = directRadiance(shading, glow, V, ShadowMapsHolding, draw.map);\n",
    "    vec3 radiance = glow;\n")
