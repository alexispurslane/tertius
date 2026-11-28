# Diagnostic only: replace the 32 octant directions with N spherical Fibonacci directions of equal solid angle.
import sys, pathlib
root = pathlib.Path(sys.argv[1]); n = int(sys.argv[2]); R = root / "src/Tertius.Rendering"
def sub(rel, old, new):
    p = R / rel; s = p.read_text(); assert s.count(old) == 1, (rel, old); p.write_text(s.replace(old, new))
sub("Shaders/volume.glsl", "const int Directions = 32;", f"const int Directions = {n};")
p = R / "Shaders/volume.glsl"; s = p.read_text()
a = s.index("ivec3 directionStep(int d)"); b = s.index("vec3 axisNormal")
s = s[:a] + """vec3 directionStep(int d)
{
    float z = 1.0 - (2.0 * float(d) + 1.0) / float(Directions);
    float r = sqrt(max(0.0, 1.0 - z * z));
    float phi = 2.39996323 * float(d);
    return vec3(r * cos(phi), r * sin(phi), z);
}
""" + s[b:]
p.write_text(s)
sub("Shaders/volume_sweep.comp", "    ivec3 step = directionStep(direction);\n    ivec3 magnitude = abs(step);", "    vec3 step = directionStep(direction);\n    vec3 magnitude = abs(step);")
sub("Shaders/volume_sweep.comp", "                q[axis] -= sign(step[axis]);\n                q[u] -= corner.x * sign(step[u]);\n                q[v] -= corner.y * sign(step[v]);",
    "                q[axis] -= int(sign(step[axis]));\n                q[u] -= corner.x * int(sign(step[u]));\n                q[v] -= corner.y * int(sign(step[v]));")
sub("Passes/LightVolumePass.cs", "private const int Directions = 32;", f"private const int Directions = {n};")
