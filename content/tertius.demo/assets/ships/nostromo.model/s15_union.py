# Merge each hull part's overlapping blocks into one closed surface (an exact self-union),
# so that faces hidden inside other blocks take no texture space.
# In the build frame: nose toward +Y (see s00_scene.py).
import math

import bmesh
import bpy

view = bpy.context.view_layer
report = {}
for name in ("Hull_Fore", "Hull_Mid", "Hull_Aft"):
    ob = bpy.data.objects[name]
    before = sum(p.area for p in ob.data.polygons)
    for o in view.objects:
        o.select_set(False)
    ob.select_set(True)
    view.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.intersect_boolean(operation="UNION", use_swap=False, use_self=True, solver="EXACT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.dissolve_limited(angle_limit=math.radians(0.5))
    bpy.ops.object.mode_set(mode="OBJECT")
    ob.data.set_sharp_from_angle(angle=math.radians(35))
    after = sum(p.area for p in ob.data.polygons)
    # Pieces left after the union: 1 means every block touches the rest.
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    pieces = 0
    seen = set()
    for v in bm.verts:
        if v.index in seen:
            continue
        pieces += 1
        stack = [v]
        seen.add(v.index)
        while stack:
            a = stack.pop()
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen:
                    seen.add(b.index)
                    stack.append(b)
    bm.free()
    report[name] = {"area_before": round(before), "area_after": round(after),
                    "faces": len(ob.data.polygons), "pieces": pieces}
result = report
