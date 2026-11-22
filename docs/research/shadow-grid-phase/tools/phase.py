import math, numpy as np
near, far_all, lam, N = 0.05, 2000.0, 0.9, 4
f32 = np.float32
splits = [lam*near*(far_all/near)**(i/N) + (1-lam)*(near+(far_all-near)*i/N) for i in range(1,N+1)]
far = float(f32(splits[0]))
def texel(aspect):
    ty = f32(math.tan(math.pi/6)); tx = f32(f32(aspect)*ty)
    k2 = float(tx)*float(tx)+float(ty)*float(ty)
    c = min(0.5*(far+near)*(1+k2), far)
    r = float(f32(math.sqrt((far-c)**2+far*far*k2)))
    return float(f32(2*r/(2048-8)))
def lit_fraction(d):
    """lit fraction of the 4x4 bilinear filter, lookup d texels outside the map edge (edge at 0, caster at <0)."""
    lf = 0
    for k in range(4):
        y = d + (k-1.5)          # tap position, texels
        # bilinear between texel centers at floor(y-0.5)+0.5 and +1
        c0 = math.floor(y-0.5)+0.5; w1 = y-c0; 
        lit = lambda c: 1.0 if c > 0 else 0.0
        lf += (1-w1)*lit(c0) + w1*lit(c0+1)
    return lf/4
share = lambda d: min(max(2*lit_fraction(d)-1,0),1)
print('far', far)
for name,(w,h) in {'1920x1080':(1920,1080),'1800x1008':(1800,1008)}.items():
    t = texel(w/h); 
    for x in (10.0,-10.0):
        q = x/t; f = q-math.floor(q)
        e = (math.floor(q-0.5)+1)        # map edge, texels from anchor (caster covers centers < q... for +x side)
        gap = q-e
        print(f'{name} err {(w/h)/(16/9)-1:+.3%} texel {t:.5f} wall x={x:+} q={q:.4f} phase {f:.4f}', end=' ')
        if x>0: print(f'map edge inside wall by {gap:+.3f} texel; share at wall edge {share(gap):.3f}, max share on face {share(max(gap,0)):.3f}')
        else:
            # mirrored: caster covers centers > q
            e2 = math.ceil(q+0.5)-1; gap2 = e2 - q
            print(f'map edge inside wall by {gap2:+.3f} texel; share at wall edge {share(gap2):.3f}')
print('share vs distance outside map edge (texels):', [(d, round(share(d),3)) for d in (0,0.1,0.25,0.5,1,1.5,2,2.5)])
# sweep the aspect error: a tooth is where the strip jumps from none to its widest (gap from -0.5 to +0.5 texel)
errs = np.arange(-0.012, 0.016, 0.00005)
for side, gap_of in (('right', lambda q: q - (math.floor(q - 0.5) + 1)), ('left', lambda q: (math.ceil(q + 0.5) - 1) - q)):
    prev = None
    for e in errs:
        q = (10 if side == 'right' else -10) / texel(16 / 9 * (1 + e))
        g = gap_of(q)
        if prev is not None and g > prev + 0.5:
            print(f'{side} wall: tooth at aspect error {e:+.3%}')
        prev = g
