# The three main hull surfaces. Each one gets its own baked texture set later.
#   Hull_Aft:  the engine block, y in [-103, -56].
#   Hull_Mid:  the spine, deck, keel and sponsons, y in [-58, 58].
#   Hull_Fore: the nose, chin, head and the raised bridge, y in [56, 112].
# In the build frame: nose toward +Y (see s00_scene.py).
import bpy

from tertius_blender.geometry import MB, coll

M = bpy.data.materials

# --- Engine block -----------------------------------------------------------
aft = MB()
# Main block: the rear face is a little larger than the front face.
aft.block(-100, -56, (-30, 30, -16, 20), (-28, 28, -14, 18), bevel=0.8)
# Rear collar plate that frames the engine bells.
aft.box((0, -101.5, 2), (62, 3, 38), bevel=0.5)
# Two towers on top, with slanted fronts.
for s in (-1, 1):
    x0, x1 = sorted((s * 13, s * 25))
    # The block's top slopes from z 20 (rear) to 18 (front), so the towers start at z 18.
    aft.block(-96, -70, (x0, x1, 18, 37), (x0 + 1, x1 - 1, 18, 33), bevel=0.5)
    # A cap block on each tower; the tower's top falls from z 36.4 to 34.5 under it, so the cap
    # reaches down to z 34. Its top (z 39.2) carries the lattice mast.
    aft.box((s * 19, -86, 36.6), (8, 12, 5.2), bevel=0.3)
# Central dorsal block between the towers.
aft.block(-98, -60, (-8, 8, 19, 27), (-7, 7, 18, 24), bevel=0.5)
# Belly block, which houses the rear landing struts.
aft.block(-95, -62, (-20, 20, -22, -15), (-18, 18, -20, -13), bevel=0.5)
# Side shoulder pods. The block's sides taper from x 30 (rear) to 28 (front), so the pods
# start at x 27.5 and sink into the sides along their whole length.
for s in (-1, 1):
    x0, x1 = sorted((s * 27.5, s * 35))
    aft.block(-94, -64, (x0, x1, -8, 12), (x0, x1, -6, 10), bevel=0.5)
aft.finish("Hull_Aft", M["M_Hull_Aft"], "Hull")

# --- Mid hull ---------------------------------------------------------------
mid = MB()
mid.box((0, 0, 0), (32, 116, 18), bevel=1.0)                 # spine: x +-16, y +-58, z +-9
mid.box((0, -6, 11.5), (20, 104, 5), bevel=0.6)              # upper deck: y -58..46, z 9..14
mid.box((0, 0, -11.5), (14, 100, 5), bevel=0.5)              # keel: z -14..-9
for s in (-1, 1):
    for y0, y1 in ((-50, -28), (-24, -2), (2, 24), (28, 44)):
        x0, x1 = sorted((s * 15.5, s * 22))
        xo0, xo1 = sorted((s * 15.5, s * 21.5))
        mid.block(y0, y1, (x0, x1, -7, 5), (xo0, xo1, -6, 4.5), bevel=0.4)
mid.finish("Hull_Mid", M["M_Hull_Mid"], "Hull")

# --- Fore hull and bridge ---------------------------------------------------
fore = MB()
fore.block(56, 98, (-19, 19, -11, 10), (-13, 13, -7, 5), bevel=0.8)        # nose block
fore.block(60, 94, (-6, 6, -14, -10), (-5, 5, -10, -6.5), bevel=0.4)       # chin
fore.block(97, 110, (-9, 9, -5.5, 3.5), (-6, 6, -3.5, 2), bevel=0.4)       # head
# Bridge: bottom face y 62..92 at z 10; top face y 62..86 at z 22 (a slanted brow).
fore.hexa([
    (-12, 62, 9.5), (12, 62, 9.5), (12, 92, 9.5), (-12, 92, 9.5),
    (-10, 62, 22), (10, 62, 22), (10, 86, 22), (-10, 86, 22),
], bevel=0.4)
# Plinth under the bridge: the nose's top falls from z 9.3 (y 62) to 5.7 (y 92), and the bridge's
# floor is flat at z 9.5, so the plinth fills the gap and sinks 0.3 into the nose.
fore.hexa([
    (-12, 62, 9.0), (12, 62, 9.0), (12, 92, 5.4), (-12, 92, 5.4),
    (-12, 62, 9.8), (12, 62, 9.8), (12, 92, 9.8), (-12, 92, 9.8),
], bevel=0.3)
fore.box((0, 74, 23.5), (14, 16, 3), bevel=0.4)                            # bridge cap
fore.block(62, 70, (-10, 10, 9, 18), (-10, 10, 9, 16), bevel=0.3)         # rear of bridge step
fore.finish("Hull_Fore", M["M_Hull_Fore"], "Hull")

result = {o.name: len(o.data.polygons) for o in coll("Hull").objects}
