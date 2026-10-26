"""General geometry helpers for model scripts: collections, a mesh builder and beams.

Units are meters, in Blender axes: +Z up, and the front of a model toward -Y
(docs/plans/asset-pipeline.md, "Axes, units and scale").
"""

import math

import bmesh
import bpy
from mathutils import Matrix, Vector


def coll(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c


def remove_object(name):
    ob = bpy.data.objects.get(name)
    if ob is not None:
        data = ob.data
        bpy.data.objects.remove(ob, do_unlink=True)
        if data is not None and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)


def align_z(axis):
    """Rotation (4x4) that turns local +Z onto `axis`."""
    return Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized()).to_matrix().to_4x4()


def frame_matrix(origin, normal, tangent_hint=(0, 1, 0)):
    """4x4 with local Z along `normal` and local Y as close as possible to `tangent_hint`."""
    n = Vector(normal).normalized()
    t = Vector(tangent_hint)
    t = t - n * t.dot(n)
    if t.length < 1e-4:
        t = Vector((1, 0, 0)) - n * n.x
    t.normalize()
    b = t.cross(n)  # local X
    m = Matrix((
        (b.x, t.x, n.x, origin[0]),
        (b.y, t.y, n.y, origin[1]),
        (b.z, t.z, n.z, origin[2]),
        (0, 0, 0, 1),
    ))
    return m


class MB:
    """Accumulates geometry in one bmesh, then becomes one object."""

    def __init__(self):
        self.bm = bmesh.new()

    def _place(self, verts, M):
        bmesh.ops.transform(self.bm, matrix=M, verts=verts)

    def box(self, center, size, M=None, bevel=0.0):
        """Axis box of `size` at `center`; M (optional) is applied after, about the origin."""
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = r["verts"]
        S = Matrix.Diagonal((size[0], size[1], size[2], 1.0))
        self._place(verts, Matrix.Translation(center) @ S)
        if M is not None:
            self._place(verts, M)
        if bevel > 0.0:
            self._bevel(verts, bevel)
        return verts

    def box_local(self, M, center, size, bevel=0.0):
        """Box given in the local frame M."""
        return self.box(center, size, M=M, bevel=bevel)

    def hexa(self, pts, bevel=0.0):
        """Six-faced solid from 8 corners: 4 at the bottom, then the 4 above them."""
        vs = [self.bm.verts.new(p) for p in pts]
        for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            self.bm.faces.new([vs[i] for i in f])
        if bevel > 0.0:
            self._bevel(vs, bevel)
        return vs

    def block(self, y0, y1, rear, front, bevel=0.0):
        """Tapered block along Y. rear/front = (x0, x1, z0, z1) at y0/y1."""
        a, b = rear, front
        pts = [
            (a[0], y0, a[2]), (a[1], y0, a[2]), (b[1], y1, b[2]), (b[0], y1, b[2]),
            (a[0], y0, a[3]), (a[1], y0, a[3]), (b[1], y1, b[3]), (b[0], y1, b[3]),
        ]
        return self.hexa(pts, bevel)

    def _bevel(self, verts, width):
        self.bm.verts.ensure_lookup_table()
        # A dict keeps the order of first use. A set of BMesh edges orders them by memory address, which
        # changes from run to run and makes the bevel, and so the model, differ between builds.
        edges = list(dict.fromkeys(e for v in verts for e in v.link_edges))
        bmesh.ops.bevel(self.bm, geom=list(verts) + edges, offset=width, segments=1,
                        affect="EDGES", profile=0.5, clamp_overlap=True)

    def cyl(self, center, axis, radius, depth, segs=16, radius2=None, caps=True):
        M = Matrix.Translation(center) @ align_z(axis)
        r = bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=segs,
                                  radius1=radius, radius2=radius if radius2 is None else radius2,
                                  depth=depth, matrix=M)
        return r["verts"]

    def cyl_local(self, M, center, axis, radius, depth, segs=16, radius2=None):
        verts = self.cyl(center, axis, radius, depth, segs, radius2)
        self._place(verts, M)
        return verts

    def sphere(self, center, radius, segs=12, rings=6):
        r = bmesh.ops.create_uvsphere(self.bm, u_segments=segs, v_segments=rings, radius=radius,
                                      matrix=Matrix.Translation(center))
        return r["verts"]

    def lathe(self, profile, M, segs=48, closed=True):
        """Revolve a (radius, height) profile around local Z, then place it with M."""
        vs = [self.bm.verts.new((r, 0.0, h)) for r, h in profile]
        es = [self.bm.edges.new((vs[i], vs[i + 1])) for i in range(len(vs) - 1)]
        if closed:
            es.append(self.bm.edges.new((vs[-1], vs[0])))
        bmesh.ops.spin(self.bm, geom=vs + es, cent=(0, 0, 0), axis=(0, 0, 1),
                       angle=2 * math.pi, steps=segs, use_merge=True)
        # Every vertex the spin made is in the connected island of `vs`. A dict keeps a fixed order (see _bevel).
        allv = dict.fromkeys(vs)
        stack = list(vs)
        while stack:
            v = stack.pop()
            for e in v.link_edges:
                o = e.other_vert(v)
                if o not in allv:
                    allv[o] = None
                    stack.append(o)
        self._place(list(allv), M)
        return list(allv)

    def tube(self, points, radius, segs=10, bend=1.5, caps=True):
        """Sweep a circle along a polyline whose corners are rounded with radius `bend`."""
        pts = [Vector(p) for p in points]
        path = [pts[0]]
        for i in range(1, len(pts) - 1):
            a, b, c = pts[i - 1], pts[i], pts[i + 1]
            d1 = (b - a).normalized()
            d2 = (c - b).normalized()
            ang = d1.angle(d2) if d1.dot(d2) < 0.9999 else 0.0
            if ang < 1e-3:
                path.append(b)
                continue
            cut = min(bend * math.tan(ang / 2), (b - a).length * 0.45, (c - b).length * 0.45)
            p0 = b - d1 * cut
            p1 = b + d2 * cut
            steps = max(3, int(ang / (math.pi / 12)))
            for s in range(steps + 1):
                t = s / steps
                # Quadratic Bezier through the corner: close to an arc for these angles.
                q = (1 - t) ** 2 * p0 + 2 * (1 - t) * t * b + t ** 2 * p1
                path.append(q)
        path.append(pts[-1])
        # Rotation-minimizing frames.
        tangents = []
        for i in range(len(path)):
            if i == 0:
                t = path[1] - path[0]
            elif i == len(path) - 1:
                t = path[-1] - path[-2]
            else:
                t = path[i + 1] - path[i - 1]
            tangents.append(t.normalized())
        n = tangents[0].orthogonal().normalized()
        rings = []
        prev_t = tangents[0]
        for p, t in zip(path, tangents):
            q = prev_t.rotation_difference(t)
            n = (q @ n)
            n = (n - t * n.dot(t)).normalized()
            b = t.cross(n)
            ring = []
            for k in range(segs):
                a = 2 * math.pi * k / segs
                ring.append(self.bm.verts.new(p + radius * (math.cos(a) * n + math.sin(a) * b)))
            rings.append(ring)
            prev_t = t
        for r0, r1 in zip(rings, rings[1:]):
            for k in range(segs):
                k2 = (k + 1) % segs
                self.bm.faces.new((r0[k], r0[k2], r1[k2], r1[k]))
        if caps:
            self.bm.faces.new(list(reversed(rings[0])))
            self.bm.faces.new(rings[-1])
        return [v for r in rings for v in r]

    def finish(self, name, material, collection, smooth_angle=35.0, recalc=True):
        remove_object(name)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        if recalc:
            bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        ob = bpy.data.objects.new(name, me)
        coll(collection).objects.link(ob)
        if material is not None:
            me.materials.append(material)
        # Smooth shading with sharp edges past `smooth_angle`.
        me.shade_smooth()
        lim = math.radians(smooth_angle)
        me.set_sharp_from_angle(angle=lim)
        return ob


def constant_material(name, color, metallic=0.0, roughness=0.5):
    """A material whose Principled BSDF has constant inputs. The glTF exporter writes it as it is, with no bake."""
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    p = m.node_tree.nodes.get("Principled BSDF")
    if p is not None:
        p.inputs["Base Color"].default_value = (*color, 1.0)
        p.inputs["Metallic"].default_value = metallic
        p.inputs["Roughness"].default_value = roughness
    return m


def beam_matrix(p0, p1, up=(0, 0, 1)):
    """Frame at the midpoint of p0-p1: local Y along the beam, local Z as close to `up` as it can."""
    p0, p1 = Vector(p0), Vector(p1)
    d = (p1 - p0).normalized()
    u = Vector(up)
    if abs(d.dot(u.normalized())) > 0.98:
        u = Vector((1, 0, 0))
    s = d.cross(u).normalized()  # local X
    n = s.cross(d).normalized()  # local Z
    c = (p0 + p1) / 2
    return Matrix((
        (s.x, d.x, n.x, c.x),
        (s.y, d.y, n.y, c.y),
        (s.z, d.z, n.z, c.z),
        (0, 0, 0, 1),
    )), (p1 - p0).length


def beam(mb, p0, p1, w, h, up=(0, 0, 1), bevel=0.05):
    """A box beam from p0 to p1 with cross-section w (local X) by h (local Z)."""
    Mb, length = beam_matrix(p0, p1, up)
    return mb.box_local(Mb, (0, 0, 0), (w, length, h), bevel=bevel)


def rod(mb, p0, p1, r, segs=10):
    """A cylinder from p0 to p1."""
    p0, p1 = Vector(p0), Vector(p1)
    return mb.cyl((p0 + p1) / 2, p1 - p0, r, (p1 - p0).length, segs=segs)
