import shutil, sys, os
src = '/Users/alexispurslane/.t3/worktrees/tertius/t3-52bfcd29-81249085ce80dd9f/src/Tertius.Rendering/Shaders'
base = open(src + '/shadows.glsl').read()
def sub(s, a, b, n=1):
    assert s.count(a) == n, (a, s.count(a)); return s.replace(a, b)
base = sub(base, 'if (frame.renderParams.w < 0.0) // PROBE debug', 'if (false) // PROBE debug', 2)
base = sub(base, 'float form = floor(v / 1000.0);', 'float form = 1.0;')
base = sub(base, 'float debug = floor(v / 100.0);', 'float debug = 0.0;')
base = sub(base, 'float debug = floor(mod(frame.renderParams.w, 1000.0) / 100.0);', 'float debug = 0.0;')
READS = 'return floor(mode) == 90.0 ? probeCap8(t, w) : probeCap8Gathers(t, w);'
WIDTH = 'float w = probeTrueAndGap(light, tile, r, max(probeNearestSmooth(1, uv, t.layer), r), L, d).x;'
BOUNDS = 'vec2 bounds = probeCap8Bounds(t);'
SIDE = '    uint kind = uint(light.intensityKind.w + 0.5);'
def mode(s, m): return sub(s, 'float mode = v - 100.0 * debug;', f'float mode = {m};')
v = {}
v['means'] = mode(base, '90.0')
v['gathers'] = mode(base, '92.0')
noreads = sub(v['means'], READS, 'return w * 0.1;')
v['noreads'] = noreads
v['noreads-nobounds'] = sub(noreads, BOUNDS, 'vec2 bounds = vec2(0.0, 1.0);')
v['noreads-side-const'] = sub(noreads, SIDE, '    return 0.3;\n' + SIDE)
v['noreads-w-const'] = sub(noreads, WIDTH, 'float w = 6.0;')
v['noreads-w-noread'] = sub(noreads, WIDTH, 'float w = probeTrueAndGap(light, tile, r, r * 1.01, L, d).x;')
v['means-w-const'] = sub(v['means'], WIDTH, 'float w = 6.0;')
v['means-w-const-nobounds'] = sub(v['means-w-const'], BOUNDS, 'vec2 bounds = vec2(0.0, 1.0);')
v['gathers-w-const-nobounds'] = sub(sub(v['gathers'], WIDTH, 'float w = 6.0;'), BOUNDS, 'vec2 bounds = vec2(0.0, 1.0);')
for name, s in v.items():
    d = f'/private/tmp/t0-gbgj/regs-p1c/{name}'
    if os.path.exists(d): shutil.rmtree(d)
    shutil.copytree(src, d)
    open(d + '/shadows.glsl', 'w').write(s)
print(' '.join(v))
v2 = {}
START = '    float outside = debug == 1.0'
v2['stub'] = sub(v['means'], START, '    decided = true;\n    return 0.5;\n' + START)
v2['lookup-only'] = sub(v['means'], 'float w = probeTrueAndGap(', 'decided = true; return t.ndc.z * 0.5 + t.bounds.x;\n        float w = probeTrueAndGap(')
v2['gathers-w-only'] = sub(v['gathers'], BOUNDS, 'vec2 bounds = vec2(0.0, 1.0);')
for name, s in v2.items():
    d = f'/private/tmp/t0-gbgj/regs-p1c/{name}'
    if os.path.exists(d): shutil.rmtree(d)
    shutil.copytree(src, d)
    open(d + '/shadows.glsl', 'w').write(s)
