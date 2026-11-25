"""Tool code. The occupied Pioneer berth, lift openings and wayfinding, in native Z-up metres."""

import bmesh
import bpy
import numpy as np

import habitat


def open_lift_shaft(pieces):
    """Cut the lift envelope through the berth deck, hub skin and inner rim in source axes.

    The deck leaves 5 cm clearance around the 4 m car. Beyond the deck the
    imported skins meet the 15 m wide shaft lining. The outer rim stays intact.
    """
    def cut(obj, low, high):
        mesh = bmesh.new()
        mesh.from_mesh(obj.data)
        faces = [f for f in mesh.faces if all(
            max(v.co[i] for v in f.verts) >= low[i] and min(v.co[i] for v in f.verts) <= high[i]
            for i in range(3)
        )]
        for axis in range(3):
            normal = tuple(int(i == axis) for i in range(3))
            for limit, sign in ((low[axis], 1), (high[axis], -1)):
                point = tuple(limit * n for n in normal)
                geometry = set(faces)
                for face in faces:
                    geometry.update(face.edges)
                    geometry.update(face.verts)
                result = bmesh.ops.bisect_plane(mesh, geom=list(geometry), plane_co=point, plane_no=normal)
                faces = [f for f in result["geom"] if isinstance(f, bmesh.types.BMFace)
                         and (f.calc_center_median()[axis] - limit) * sign >= 0]
        if not faces:
            raise ValueError(f"{obj.name} has no surfaces in the shaft cut")
        bmesh.ops.delete(mesh, geom=faces, context="FACES")
        mesh.to_mesh(obj.data)
        mesh.free()

    cut(pieces["hub"], (-110, -5.85, -16.65), (-101, -1.75, -12.55))
    for piece in ("hub", "ring"):
        cut(pieces[piece], (-1980, -7.5, -22.1), (-110, 7.5, -7.1))


def finish_berth(hub):
    """Replace only the four coplanar landing polygons (and their cut pieces) with metre-scale chequer plate."""
    size = 256
    y, x = np.mgrid[:size, :size] / size
    u, v = x % 0.125 - 0.0625, y % 0.125 - 0.0625
    sense = np.where((np.floor(x * 8) + np.floor(y * 8)) % 2 == 0, 1, -1)
    along = (u + sense * v) / np.sqrt(2)
    across = (v - sense * u) / np.sqrt(2)
    # A stamped diamond is 74 by 21 mm and 2.4 mm high, with sloping shoulders.
    tread = np.maximum(1 - np.abs(along) / 0.037 - np.abs(across) / 0.0105, 0)
    edge = np.minimum.reduce((x, y, 1 - x, 1 - y))
    relief = 0.0024 * tread - 0.002 * np.clip(1 - edge / 0.006, 0, 1)
    du = (np.roll(relief, -1, axis=1) - np.roll(relief, 1, axis=1)) * size / 2
    dv = (np.roll(relief, -1, axis=0) - np.roll(relief, 1, axis=0)) * size / 2
    normal = np.stack((-du, -dv, np.ones_like(du)), axis=2)
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    color = np.stack((0.29 + 0.10 * tread, 0.31 + 0.09 * tread, 0.31 + 0.08 * tread), axis=2)
    color[edge < 0.006] *= 0.7
    plate = bpy.data.materials.new("Berth 14 chequer plate")
    plate.use_nodes = True
    tree = plate.node_tree
    shader = tree.nodes.get("Principled BSDF")
    shader.inputs["Roughness"].default_value = 0.64
    shader.inputs["Metallic"].default_value = 0.44
    paint = tree.nodes.new("ShaderNodeTexImage")
    paint.image = habitat._packed_image("Berth 14 plate paint", color, "sRGB")
    tree.links.new(paint.outputs["Color"], shader.inputs["Base Color"])
    relief_tex = tree.nodes.new("ShaderNodeTexImage")
    relief_tex.image = habitat._packed_image("Berth 14 stamp relief", normal * 0.5 + 0.5, "Non-Color")
    tangent = tree.nodes.new("ShaderNodeNormalMap")
    tree.links.new(relief_tex.outputs["Color"], tangent.inputs["Color"])
    tree.links.new(tangent.outputs["Normal"], shader.inputs["Normal"])
    mesh = hub.data
    slot = len(mesh.materials)
    mesh.materials.append(plate)
    uv = mesh.uv_layers.active.data
    for face in mesh.polygons:
        if not all(abs(mesh.vertices[v].co.x + 104.611) < 0.002 for v in face.vertices):
            continue
        face.material_index = slot
        for i in face.loop_indices:
            p = mesh.vertices[mesh.loops[i].vertex_index].co
            uv[i].uv = (p.y, p.z)


def landing_signs():
    """Wayfinding fixed to the two gate lintels and to the rim waiting room wall."""
    from mathutils import Matrix, Vector

    def enamel(name, rgb, glow):
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        shader = mat.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = (*rgb, 1)
        shader.inputs["Roughness"].default_value = 0.48
        shader.inputs["Emission Color"].default_value = (*rgb, 1)
        shader.inputs["Emission Strength"].default_value = glow
        return mat

    navy = enamel("Transit blue enamel", (0.025, 0.09, 0.13), 0.015)
    ivory = enamel("Backlit lettering", (0.83, 0.91, 0.78), 0.18)
    objects = []
    # Local X follows the baseline, Y points toward the station axis and Z faces the reader.
    for caption, x, y, z, width, height, gate in (
        ("14   /   RIM LIFT", -101.46, 2.51, 3.8, 4.0, 0.62, True),
        ("HUB   /   14", -1996.55, 2.51, 3.8, 4.0, 0.62, True),
        ("RIM   /   ARRIVALS", -1997.54, 0, -9.76, 8.0, 0.85, False),
    ):
        up = Vector((1, 0, 0))
        normal = Vector((0, 0, 1) if gate else (0, -1, 0))
        right = up.cross(normal)
        basis = Matrix((right, up, normal)).transposed().to_quaternion()
        center = Vector((x, -z, y - 14.6))
        bpy.ops.mesh.primitive_cube_add(size=1)
        panel = bpy.context.object
        panel.name = caption + " fascia"
        panel.rotation_mode = "QUATERNION"
        panel.rotation_quaternion = basis
        panel.location = center
        panel.scale = (width, height, 0.08)
        panel.data.materials.append(navy)
        objects.append(panel)
        bpy.ops.object.text_add()
        letters = bpy.context.object
        letters.name = caption
        letters.data.body = caption
        letters.data.align_x = "CENTER"
        letters.data.align_y = "CENTER"
        letters.data.size = height * 0.83
        letters.data.materials.append(ivory)
        letters.rotation_mode = "QUATERNION"
        letters.rotation_quaternion = basis
        letters.location = center + normal * 0.045
        bpy.ops.object.convert(target="MESH")
        objects.append(bpy.context.object)
    return objects
