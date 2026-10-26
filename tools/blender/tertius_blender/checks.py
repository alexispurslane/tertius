"""Checks that model scripts and agents run on the scene. Each returns a report dictionary and changes nothing."""

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def attachment(objects, anchors, tolerance=0.02):
    """Finds the mesh islands that hang free.

    An island is a connected part of the mesh of one of `objects`. It is attached when it touches, overlaps or sits
    inside an island of one of `anchors`, directly or through a chain of attached islands. `objects` and `anchors`
    are object names. `tolerance` is in meters: a gap smaller than it counts as touching.

    Returns {"islands": count, "free": count, "freeIslands": up to 40 of {"object", "lo", "hi"}}. A model script
    that must have no free island raises an exception when "free" is above 0.
    """
    bpy.context.view_layer.update()
    held = [island for name in anchors for island in _islands(bpy.data.objects[name])]
    parts = [island for name in objects for island in _islands(bpy.data.objects[name])]
    attached = [any(_contact(p, h, tolerance) for h in held) for p in parts]
    changed = True
    while changed:
        changed = False
        for i, p in enumerate(parts):
            if attached[i]:
                continue
            if any(attached[j] and _contact(p, q, tolerance) for j, q in enumerate(parts)):
                attached[i] = True
                changed = True
    free = [{"object": p["name"], "lo": [round(c, 2) for c in p["lo"]], "hi": [round(c, 2) for c in p["hi"]]}
            for p, ok in zip(parts, attached) if not ok]
    return {"islands": len(parts), "free": len(free), "freeIslands": free[:40]}


def _islands(ob):
    """The connected parts of the mesh of `ob`, in world space, each with a BVH tree and bounds."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.transform(ob.matrix_world)
    bm.verts.ensure_lookup_table()
    island_of = [-1] * len(bm.verts)
    islands = []
    for v in bm.verts:
        if island_of[v.index] >= 0:
            continue
        index = len(islands)
        island_of[v.index] = index
        members = [v]
        stack = [v]
        while stack:
            a = stack.pop()
            for e in a.link_edges:
                b = e.other_vert(a)
                if island_of[b.index] < 0:
                    island_of[b.index] = index
                    members.append(b)
                    stack.append(b)
        islands.append(members)
    faces = [[] for _ in islands]
    for f in bm.faces:
        faces[island_of[f.verts[0].index]].append(f)
    out = []
    for members, fs in zip(islands, faces):
        remap = {a.index: i for i, a in enumerate(members)}
        verts = [a.co.copy() for a in members]
        polys = [[remap[x.index] for x in f.verts] for f in fs]
        lo = Vector((min(p.x for p in verts), min(p.y for p in verts), min(p.z for p in verts)))
        hi = Vector((max(p.x for p in verts), max(p.y for p in verts), max(p.z for p in verts)))
        out.append({"name": ob.name, "verts": verts, "bvh": BVHTree.FromPolygons(verts, polys), "lo": lo, "hi": hi})
    bm.free()
    return out


def _inside(p, bvh):
    loc, nrm, _index, _dist = bvh.find_nearest(p)
    return loc is not None and (p - loc).dot(nrm) < 0.0


def _near(points, other, tolerance, bounds=None):
    for p in points:
        if bounds is not None and any(p[k] < bounds[0][k] - tolerance or p[k] > bounds[1][k] + tolerance
                                      for k in range(3)):
            continue
        if other["bvh"].find_nearest(p, tolerance)[0] is not None:
            return True
    return False


def _contact(a, b, tolerance):
    if any(a["lo"][k] > b["hi"][k] + tolerance or b["lo"][k] > a["hi"][k] + tolerance for k in range(3)):
        return False
    if a["bvh"].overlap(b["bvh"]):
        return True
    if _near(a["verts"], b, tolerance) or _near(b["verts"], a, tolerance, (a["lo"], a["hi"])):
        return True
    return _inside(a["verts"][0], b["bvh"]) or _inside(b["verts"][0], a["bvh"])


def view_shares(camera, columns=192, rows=108):
    """What the camera object named `camera` sees: casts a grid of columns x rows rays through its frame and returns
    {"camera": name, "share": {object name or "(space)": share of the rays whose first hit is that object}}, largest
    share first. The stage of the views (stage.build) holds the cameras of the shots, named VIEW_<shot name>."""
    scene = bpy.context.scene
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    cam = bpy.data.objects[camera]
    # Corners of the frame in camera space: top right, bottom right, bottom left, top left.
    tr, br, bl, tl = cam.data.view_frame(scene=scene)
    world = cam.matrix_world
    origin = world.translation
    hits = {}
    for j in range(rows):
        v = (j + 0.5) / rows
        for i in range(columns):
            u = (i + 0.5) / columns
            local = tl.lerp(tr, u).lerp(bl.lerp(br, u), v)
            direction = (world.to_3x3() @ local).normalized()
            ok, _loc, _normal, _index, ob, _matrix = scene.ray_cast(depsgraph, origin, direction, distance=5000.0)
            key = ob.name if ok else "(space)"
            hits[key] = hits.get(key, 0) + 1
    total = columns * rows
    return {"camera": camera, "share": {k: round(c / total, 4) for k, c in sorted(hits.items(), key=lambda kv: -kv[1])}}
