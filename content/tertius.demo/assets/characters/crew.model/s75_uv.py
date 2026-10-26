# UV maps for the bake objects of model.json, by Smart UV Project, and their texel density.
import math

import bpy

from tertius_blender import run

bake = run.load_model(model.folder)["bake"]
view = bpy.context.view_layer
report = {}
for name in bake["objects"]:
    ob = bpy.data.objects[name]
    for o in view.objects:
        o.select_set(False)
    ob.select_set(True)
    view.objects.active = ob
    me = ob.data
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0015, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(rotate=True, margin=0.002, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")
    # Texel density: texels per meter = bake size * sqrt(uv area / surface area).
    uv = me.uv_layers["UVMap"].data
    area3d = 0.0
    area_uv = 0.0
    for poly in me.polygons:
        area3d += poly.area
        pts = [uv[i].uv for i in poly.loop_indices]
        a = 0.0
        for i in range(len(pts)):
            x0, y0 = pts[i]
            x1, y1 = pts[(i + 1) % len(pts)]
            a += x0 * y1 - x1 * y0
        area_uv += abs(a) / 2
    report[name] = {"surface_m2": round(area3d, 2), "uv_fill": round(area_uv, 3),
                    "texels_per_m": round(bake["size"] * math.sqrt(area_uv / area3d), 1)}
result = report
