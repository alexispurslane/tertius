"""Tool code. Converts one Blue Danube model into a .glb in engine axes, inside headless Blender (tools/README.md,
"The Blue Danube art"). setup.py runs it once per sidecar:

    blender --background --factory-startup --threads 1 --python convert.py -- \
        --sidecar <x.import.json> --sources <folder of its fetched files> --out <model.glb> --report <report.json>

Every source becomes Blender objects first: a Pioneer model through the COLLADA reader of pioneer.py, a glTF through
Blender's importer. Then the steps run in this order: the finish of the textures, the glow bake, the overlay, the turn
to the engine's front, and the export through tertius_blender.export, which the scripted models use too. The order
matters: the glow bake must not touch the overlay's own emissive groups, and the overlay is authored in the source's
axes, before the turn.

The output is in engine axes: meters, +Y up, the model's front toward -Z. Blender's exporter turns Blender's +Z up into
glTF's +Y up and Blender's +Y into glTF's -Z, so the converter turns each model until its front faces Blender's +Y."""

import argparse
import hashlib
import importlib
import inspect
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "tools" / "blender"))
sys.path.insert(0, str(HERE))

import finish  # noqa: E402
import glow  # noqa: E402
import pioneer  # noqa: E402
from tertius_blender import export  # noqa: E402

# Pioneer's materials have no roughness or metal maps: a plain painted non-metal.
PIONEER_ROUGHNESS = 0.6

# The turn about Blender's Z that brings a model's front, as its sidecar names it, to Blender's +Y.
FRONT_TURNS = {"+Y": 0.0, "-Y": math.pi, "+X": math.pi / 2, "-X": -math.pi / 2}


def main(argv):
    args = _arguments(argv)
    sidecar = json.loads(Path(args.sidecar).read_text(encoding="utf-8"))
    convert = sidecar["convert"]
    sources = Path(args.sources)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    report = {"model": args.model, "notes": []}

    if convert["kind"] == "pioneer":
        pieces = _pioneer(convert, sources, report)
    elif convert["kind"] == "gltf":
        pieces = _gltf(convert, sources)
    elif convert["kind"] == "blend":
        pieces = _blend(convert, sources, report)
    else:
        raise ValueError(f"unknown conversion kind {convert['kind']!r}")

    if "finish" in convert:
        report["finish"] = finish.apply(convert["finish"], pieces, sources)
    report["glow"] = glow.bake(pieces, convert.get("glow", {}))
    _overlay(convert, pieces, report)
    _turn_to_front(convert.get("front", "+Y"))
    _limit_textures(convert.get("maxTextureSize"))
    report["pieces"] = _measure()
    export.write_glb(str(args.out))
    report["textures"] = sorted({(img.name, img.size[0], img.size[1]) for img in bpy.data.images if img.users})
    Path(args.report).write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def _arguments(argv):
    parser = argparse.ArgumentParser(prog="convert.py")
    parser.add_argument("--sidecar", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--sources", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--report", required=True)
    return parser.parse_args(argv)


# Pioneer models.


def _pioneer(convert, sources, report):
    """Builds one Blender object per piece of the sidecar's mesh list, with Pioneer's materials. Returns the pieces by
    name. Each object stands at the identity, with its vertices in the COLLADA file's world axes (meters, +Z up)."""
    materials_by_name = pioneer.parse_model_file((sources / convert["modelFile"]).read_text(encoding="utf-8"))
    overrides = convert.get("materials", {})
    blender_materials = {}
    pieces = {}
    dropped = []
    for mesh in convert["meshes"]:
        text = (sources / mesh["file"]).read_text(encoding="utf-8")
        geometries, instances, dropped_here = pioneer.read_collada(text, convert.get("dropNodes", []))
        dropped.extend(dropped_here)
        pieces[mesh["piece"]] = _pioneer_piece(
            name=mesh["piece"],
            geometries=geometries,
            instances=instances,
            material_for=lambda name: _pioneer_material(
                name, materials_by_name, overrides.get(name, {}), sources, blender_materials
            ),
        )
    report["dropped"] = _drop_summary(dropped)
    return pieces


def _drop_summary(names):
    """The dropped node names, counted by their name without the trailing number."""
    counts = {}
    for name in names:
        key = name.rstrip("0123456789_.") or name
        counts[key] = counts.get(key, 0) + 1
    return dict(sorted(counts.items()))


def _pioneer_piece(name, geometries, instances, material_for):
    """One mesh object of every instance in `instances`, with each instance's world matrix baked into its points and
    normals."""
    points, faces, loop_normals, loop_uvs, face_materials = [], [], [], [], []
    slots = []
    for inst in instances:
        geometry = geometries[inst.geometry]
        m = np.array(inst.matrix, dtype=np.float64)
        normal_matrix = np.linalg.inv(m[:3, :3]).T
        flip = np.linalg.det(m[:3, :3]) < 0
        base = len(points)
        p = np.array(geometry.positions, dtype=np.float64).reshape(-1, 3)
        points.extend((p @ m[:3, :3].T + m[:3, 3]).tolist())
        for primitive in geometry.primitives:
            material = material_for(inst.materials.get(primitive.material, primitive.material).removesuffix("-material"))
            if material.name not in slots:
                slots.append(material.name)
            slot = slots.index(material.name)
            corners = primitive.corners
            for t in range(0, len(corners), 3):
                tri = corners[t : t + 3]
                if flip:
                    # A mirroring matrix turns the winding over; glTF's rule restores it.
                    tri = [tri[0], tri[2], tri[1]]
                indices = [base + c[0] for c in tri]
                if len(set(indices)) < 3:
                    continue
                faces.append(indices)
                face_materials.append(slot)
                for c in tri:
                    n = normal_matrix @ np.array(c[1] if c[1] is not None else (0.0, 0.0, 1.0))
                    length = np.linalg.norm(n)
                    loop_normals.append(tuple(n / length) if length > 0 else (0.0, 0.0, 1.0))
                    loop_uvs.append(c[2])
    data = bpy.data.meshes.new(name)
    data.from_pydata(points, [], faces)
    for material_name in slots:
        data.materials.append(bpy.data.materials[material_name])
    data.polygons.foreach_set("material_index", face_materials)
    uv = data.uv_layers.new(name="UVMap")
    uv.data.foreach_set("uv", [x for pair in loop_uvs for x in pair])
    data.normals_split_custom_set(loop_normals)
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def _pioneer_material(name, materials_by_name, override, sources, cache):
    """The Blender material of the Pioneer material `name`: its diffuse map times its diffuse color, a roughness of
    0.6, its opacity, and its glow map in the custom property tertius_glow for the glow bake. `override` may replace a
    map by a file that its path and SHA-256 pin (the hook of the station's texture recipe)."""
    if name in cache:
        return cache[name]
    spec = materials_by_name.get(name, pioneer.PioneerMaterial(name))
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.use_backface_culling = True
    tree = material.node_tree
    bsdf = tree.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = PIONEER_ROUGHNESS
    bsdf.inputs["Metallic"].default_value = 0.0
    diffuse = _map(override.get("diffuse"), spec.diffuse, sources)
    if diffuse is not None:
        if spec.diffuse_color != (1.0, 1.0, 1.0):
            diffuse = _tinted(diffuse, spec.diffuse_color)
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = diffuse
        tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = (*spec.diffuse_color, 1.0)
    normal = _map(override.get("normal"), None, sources)
    if normal is not None:
        normal.colorspace_settings.name = "Non-Color"
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = normal
        normal_node = tree.nodes.new("ShaderNodeNormalMap")
        tree.links.new(node.outputs["Color"], normal_node.inputs["Color"])
        tree.links.new(normal_node.outputs["Normal"], bsdf.inputs["Normal"])
    if spec.opacity < 100:
        bsdf.inputs["Alpha"].default_value = spec.opacity / 100
        material.surface_render_method = "BLENDED"
    glow_image = _map(override.get("glow"), spec.glow, sources)
    if glow_image is not None:
        material["tertius_glow"] = glow_image.name
    cache[name] = material
    return material


def _map(pinned, file_name, sources):
    """The image of a map: a pinned replacement {path, sha256} relative to the repository when the sidecar gives one,
    else the fetched file `file_name`, else none. A file that the record names but the fetch did not get is none."""
    if pinned:
        path = REPO / pinned["path"]
        digest = "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()
        if digest != pinned["sha256"]:
            raise ValueError(f"{path} has {digest}, not the pinned {pinned['sha256']}")
        return bpy.data.images.load(str(path), check_existing=True)
    if file_name is None or not (sources / file_name).exists():
        return None
    return bpy.data.images.load(str(sources / file_name), check_existing=True)


def _tinted(image, color):
    """A copy of `image` whose RGB is multiplied by `color`, as Pioneer multiplies its diffuse color into the map."""
    pixels = glow.pixels(image)
    pixels[..., :3] *= np.array(color, dtype=np.float32)
    return glow.new_image(image.name + ".tinted", pixels)


# glTF models.


def _gltf(convert, sources):
    """Imports the glTF file of the sidecar. Returns its mesh objects by name."""
    bpy.ops.import_scene.gltf(filepath=str(sources / convert["file"]))
    return {ob.name: ob for ob in bpy.context.scene.objects if ob.type == "MESH"}


# Blender files with a rig.


def _blend(convert, sources, report):
    """Opens the .blend file of the sidecar, poses its armature with the sidecar's fixed bone angles, applies the pose to
    the kept meshes, rebuilds their materials from the fetched maps, and joins them into one piece standing on height 0,
    scaled to the sidecar's height. Every other object is removed."""
    bpy.ops.wm.open_mainfile(filepath=str(sources / convert["file"]))
    armature = next(ob for ob in bpy.data.objects if ob.type == "ARMATURE")
    _pose(armature, convert.get("pose", {}))
    keep = set(convert["keep"])
    depsgraph = bpy.context.evaluated_depsgraph_get()
    kept = []
    for ob in list(bpy.data.objects):
        if ob.type != "MESH" or ob.name not in keep:
            continue
        posed = bpy.data.meshes.new_from_object(ob.evaluated_get(depsgraph))
        posed.transform(ob.matrix_world)
        baked = bpy.data.objects.new(ob.name + ".posed", posed)
        bpy.context.scene.collection.objects.link(baked)
        kept.append(baked)
    for ob in list(bpy.data.objects):
        if ob not in kept:
            bpy.data.objects.remove(ob, do_unlink=True)
    _blend_materials(convert, sources)
    piece = _join(kept, convert["piece"])
    report["sourceHeight"] = _stand(piece, float(convert["height"]))
    return {piece.name: piece}


def _pose(armature, angles):
    """Clears the armature's animation and constraints, then turns each named bone about the armature's axis by its fixed
    angle in degrees, about the bone's head, parents before children in the sidecar's order."""
    armature.animation_data_clear()
    for bone in armature.pose.bones:
        for constraint in list(bone.constraints):
            bone.constraints.remove(constraint)
        bone.matrix_basis.identity()
    bpy.context.view_layer.update()
    for name, (axis, degrees) in angles.items():
        bone = armature.pose.bones[name]
        head = bone.matrix.to_translation()
        turn = Matrix.Translation(head) @ Matrix.Rotation(math.radians(degrees), 4, axis) @ Matrix.Translation(-head)
        bone.matrix = turn @ bone.matrix
        bpy.context.view_layer.update()


def _blend_materials(convert, sources):
    """Rebuilds each material whose name ends in a key of the sidecar's materials: its diffuse map, its normal map, with
    the green channel flipped for a map in the DirectX convention, and the sidecar's roughness."""
    for material in bpy.data.materials:
        key = material.name.rsplit("/", 1)[-1]
        spec = convert.get("materials", {}).get(key)
        if spec is None:
            continue
        tree = material.node_tree
        tree.nodes.clear()
        bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
        output = tree.nodes.new("ShaderNodeOutputMaterial")
        tree.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])
        bsdf.inputs["Roughness"].default_value = float(convert.get("roughness", 0.5))
        bsdf.inputs["Metallic"].default_value = 0.0
        material.use_backface_culling = True
        diffuse = tree.nodes.new("ShaderNodeTexImage")
        diffuse.image = bpy.data.images.load(str(sources / spec["diffuse"]), check_existing=True)
        tree.links.new(diffuse.outputs["Color"], bsdf.inputs["Base Color"])
        normal_image = bpy.data.images.load(str(sources / spec["normal"]), check_existing=True)
        normal_image.colorspace_settings.name = "Non-Color"
        if spec.get("flipGreen"):
            rgba = glow.pixels(normal_image)
            rgba[..., 1] = 1.0 - rgba[..., 1]
            normal_image = glow.new_image(normal_image.name + ".opengl", rgba)
            normal_image.colorspace_settings.name = "Non-Color"
        normal = tree.nodes.new("ShaderNodeTexImage")
        normal.image = normal_image
        normal_map = tree.nodes.new("ShaderNodeNormalMap")
        tree.links.new(normal.outputs["Color"], normal_map.inputs["Color"])
        tree.links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])


def _join(objects, name):
    bpy.context.view_layer.update()
    for ob in bpy.context.scene.objects:
        ob.select_set(ob in objects)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = name
    joined.data.name = name
    return joined


def _stand(obj, height):
    """Scales `obj`'s mesh to `height` meters tall, with its lowest point at height 0 and its footprint centered on the
    origin. Returns the height before, in the file's units."""
    points = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", points)
    points = points.reshape(-1, 3)
    low, high = points.min(axis=0), points.max(axis=0)
    source_height = float(high[2] - low[2])
    scale = height / source_height
    center = Matrix.Translation((-(low[0] + high[0]) / 2, -(low[1] + high[1]) / 2, -low[2]))
    obj.data.transform(Matrix.Scale(scale, 4) @ center)
    return round(source_height, 4)


# The steps after the load.


def _overlay(convert, pieces, report):
    """Runs the overlay module that the sidecar names, such as the ring's galleries in habitat.py or the fitted shuttle
    cabin in shuttle_cabin.py. Objects marked with tertius_piece = 1 stay separate pieces at root; all others are
    parented to the piece `parent` so they export as part of that piece. The module works in the source's axes and in
    the scene's collection, after the glow bake, so the bake leaves its constant emission alone (tools/README.md, "The
    station overlay")."""
    overlay = convert.get("overlay")
    if not overlay:
        return
    module = importlib.import_module(overlay["module"])
    parent = pieces[overlay["parent"]]
    sig = inspect.signature(module.build)
    if len(sig.parameters) == 0:
        result = module.build()
    else:
        result = module.build(pieces, convert)
    if isinstance(result, tuple) and len(result) == 2 and isinstance(result[1], dict):
        added, overlay_report = result
        report["overlay"] = overlay_report
    else:
        added = result
    added = list(added)
    if overlay.get("openLiftShaft"):
        import station_dock
        station_dock.open_lift_shaft(pieces)
        station_dock.finish_berth(pieces["hub"])
        added.extend(station_dock.landing_signs())
    for obj in added:
        if obj.get("tertius_piece") == 1:
            continue
        obj.parent = parent
        obj.matrix_parent_inverse = parent.matrix_world.inverted()
    report["notes"].append(f"Overlay {overlay['module']}.build() added {', '.join(o.name for o in added)} to "
                           f"{overlay['parent']}.")


def _turn_to_front(front):
    """Turns every root object about Blender's Z so that the model's front faces Blender's +Y."""
    turn = Matrix.Rotation(FRONT_TURNS[front], 4, "Z")
    for ob in bpy.context.scene.objects:
        if ob.parent is None:
            ob.matrix_world = turn @ ob.matrix_world
    bpy.context.view_layer.update()


def _limit_textures(limit):
    """Scales every used image whose larger side exceeds `limit` pixels down to it, keeping its aspect."""
    if limit is None:
        return
    for image in bpy.data.images:
        width, height = image.size
        if image.users and max(width, height) > limit:
            factor = limit / max(width, height)
            image.scale(max(1, round(width * factor)), max(1, round(height * factor)))


def _measure():
    """The bounds in engine axes (x, z, -y of Blender), in meters, and the triangle count of each mesh object."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    out = {}
    for ob in sorted(bpy.context.scene.objects, key=lambda o: o.name):
        if ob.type != "MESH":
            continue
        mesh = ob.evaluated_get(depsgraph).data
        count = len(mesh.vertices)
        points = np.empty(count * 3, dtype=np.float64)
        mesh.vertices.foreach_get("co", points)
        points = points.reshape(-1, 3)
        world = np.array(ob.matrix_world)
        points = points @ world[:3, :3].T + world[:3, 3]
        engine = np.stack([points[:, 0], points[:, 2], -points[:, 1]], axis=1)
        mesh.calc_loop_triangles()
        out[ob.name] = {
            "min": [round(float(v), 3) for v in engine.min(axis=0)],
            "max": [round(float(v), 3) for v in engine.max(axis=0)],
            "triangles": len(mesh.loop_triangles),
        }
    return out


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else [])
